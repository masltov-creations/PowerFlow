using PowerFlow.Core.Policy;
using PowerFlow.Core.Envelope;
using PowerFlow.Core.Rules;
using PowerFlow.Windows.Activity;
using PowerFlow.Windows.Games;
using PowerFlow.Windows.Power;

namespace PowerFlow.App.Controller;

public sealed class PowerFlowController : IAsyncDisposable
{
    private static readonly TimeSpan AdaptiveMinimumBalancedResidency = TimeSpan.FromSeconds(60);
    private PowerFlowConfig _config;
    private readonly IPowerPlanController _plans;
    private readonly IActivitySource _activity;
    private readonly IGameLifecycleMonitor _games;
    private readonly IControllerTickSourceFactory _tickFactory;
    private readonly IControllerDelay _delay;
    private readonly IControllerClock _clock;
    private readonly IActivePowerPlanObserver? _planObserver;
    private readonly PowerPolicyEngine _engine;
    private readonly object _queueLock = new();
    private readonly object _backgroundLock = new();
    private readonly Queue<TransitionRecord> _history = new();
    private readonly List<Task> _backgroundTasks = new();
    private Task _queue = Task.CompletedTask;
    private CancellationTokenSource? _lifetimeCts;
    private CancellationTokenSource? _samplingCts;
    private CancellationTokenSource? _cooldownCts;
    private PowerState _currentState;
    private PowerState? _adaptivePendingTarget;
    private DateTimeOffset? _adaptivePendingSince;
    private DateTimeOffset _stateEnteredAt;
    private string? _activeGameKey;
    private bool _started;

    public PowerFlowController(
        PowerFlowConfig config,
        IPowerPlanController plans,
        IActivitySource activity,
        IGameLifecycleMonitor games,
        IControllerTickSourceFactory tickFactory,
        IControllerDelay delay,
        IControllerClock clock,
        IActivePowerPlanObserver? planObserver = null)
    {
        _config = config;
        _plans = plans;
        _activity = activity;
        _games = games;
        _tickFactory = tickFactory;
        _delay = delay;
        _clock = clock;
        _stateEnteredAt = clock.UtcNow;
        _planObserver = planObserver;
        _engine = new PowerPolicyEngine(new PolicyConfig(
            config.RestingState,
            config.CpuPromotionThresholdPercent,
            config.CpuPromotionWindow,
            config.QuietThresholdPercent,
            config.QuietWindow,
            config.PostGameCooldown));
        _currentState = config.RestingState;
        Snapshot = NewSnapshot(_currentState, "Not started", false, null, 0, null, null);
    }

    public event EventHandler<ControllerSnapshot>? SnapshotChanged;
    public ControllerSnapshot Snapshot { get; private set; }
    public bool SamplingEnabled => _samplingCts is not null && !_samplingCts.IsCancellationRequested;
    public long ActivitySampleCount { get; private set; }
    public long DashboardTelemetrySampleCount { get; private set; }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_started) return;
        _started = true;
        _lifetimeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        _games.GameDetected += OnGameDetected;
        _games.GameProcessAdded += OnGameProcessAdded;
        _games.GameLatchReleased += OnGameLatchReleased;

        var active = await _plans.GetActiveAsync(cancellationToken);
        _currentState = MapPlan(active.Id) ?? _config.RestingState;
        _engine.SynchronizeObservedState(_currentState);
        _stateEnteredAt = _clock.UtcNow;
        ResetAdaptiveTransition();
        Publish(_currentState, $"Observed active Windows plan: {active.Name}", false, null, 0, null, null);

        if (_planObserver is not null)
        {
            _planObserver.ActivePlanChanged += OnActivePlanChanged;
            _planObserver.Start();
        }

        _games.UpdateRules(_config.AppRules);
        _games.Start();
        await DrainAsync();

        if (!_games.IsLatched && _currentState == PowerState.HighPerformance)
            await EnqueueAsync(() => ActivateDirectAsync(PowerState.Balanced, "Recovery from unlatched High Performance", null, false));

        if (!_games.IsLatched && !Snapshot.IsLatched)
            StartSampling();
    }

    public async Task StopAsync()
    {
        if (!_started) return;
        _started = false;
        StopSampling();
        _cooldownCts?.Cancel();
        _lifetimeCts?.Cancel();
        _games.Stop();
        _games.GameDetected -= OnGameDetected;
        _games.GameProcessAdded -= OnGameProcessAdded;
        _games.GameLatchReleased -= OnGameLatchReleased;
        if (_planObserver is not null)
        {
            _planObserver.ActivePlanChanged -= OnActivePlanChanged;
            _planObserver.Stop();
        }
        await DrainAsync();

        Task[] background;
        lock (_backgroundLock) background = _backgroundTasks.ToArray();
        try { await Task.WhenAll(background); } catch (OperationCanceledException) { } catch { }

        _samplingCts?.Dispose();
        _cooldownCts?.Dispose();
        _lifetimeCts?.Dispose();
        _samplingCts = null;
        _cooldownCts = null;
        _lifetimeCts = null;
    }

    public Task UpdatePolicyConfigAsync(PowerFlowConfig config) => EnqueueAsync(() =>
    {
        _config = config;
        ResetAdaptiveTransition();
        _engine.Reconfigure(new PolicyConfig(
            config.RestingState,
            config.CpuPromotionThresholdPercent,
            config.CpuPromotionWindow,
            config.QuietThresholdPercent,
            config.QuietWindow,
            config.PostGameCooldown));
        Publish(_currentState,
            Snapshot.IsLatched ? Snapshot.Reason : "Policy updated live",
            Snapshot.IsLatched,
            Snapshot.LatchType,
            Snapshot.CpuPercent,
            Snapshot.TriggerApplication,
            Snapshot.CooldownRemaining);
        return Task.CompletedTask;
    });
    public Task SetManualStateAsync(PowerState state) => EnqueueAsync(async () =>
    {
        StopSampling();
        CancelCooldown();
        ResetAdaptiveTransition();
        var decision = _engine.Evaluate(new ManualStateRequested(_clock.UtcNow, state));
        await ApplyDecisionAsync(decision, "Manual override");
        if (!Snapshot.IsLatched && !_games.IsLatched) StartSampling();
    });
    public Task ReleaseManualLatchAsync() => EnqueueAsync(async () =>
    {
        ResetAdaptiveTransition();
        var decision = _engine.Evaluate(new ManualStateReleased(_clock.UtcNow));
        await ApplyDecisionAsync(decision, "Manual override released");
        if (!_games.IsLatched) StartSampling();
    });

    public Task ApplyAdaptiveGovernorDecisionAsync(
        GovernorDecision decision,
        PerformanceEntitlement entitlement,
        string? trigger = null) => EnqueueAsync(async () =>
    {
        ArgumentNullException.ThrowIfNull(decision);
        ArgumentNullException.ThrowIfNull(entitlement);

        var manualLatched = Snapshot.IsLatched && string.Equals(Snapshot.LatchType, "Manual", StringComparison.OrdinalIgnoreCase);
        var gameLatched = _games.IsLatched || (Snapshot.IsLatched && string.Equals(Snapshot.LatchType, "Game", StringComparison.OrdinalIgnoreCase));
        var autoEnabled = !Snapshot.IsLatched && !_games.IsLatched;
        var actuation = EnvelopeActuationPolicy.Evaluate(
            _config.AdaptiveActuationEnabled,
            autoEnabled,
            decision.Confidence,
            manualLatched,
            gameLatched,
            decision,
            entitlement);

        if (!actuation.Eligible || actuation.TargetState is not PowerState target)
        {
            ResetAdaptiveTransition();
            return;
        }

        if (!ShouldApplyAdaptiveTarget(target, _clock.UtcNow, out var smoothingReason))
        {
            Publish(_currentState, smoothingReason, false, null, Snapshot.CpuPercent, trigger ?? Snapshot.TriggerApplication, Snapshot.CooldownRemaining);
            return;
        }

        ResetAdaptiveTransition();
        await ActivateDirectAsync(
            target,
            $"Adaptive governor: {actuation.Reason}",
            trigger,
            latched: false);
    });
    public Task<IReadOnlyList<PowerPlanInfo>> ListPowerPlansAsync(CancellationToken cancellationToken = default) => _plans.ListAsync(cancellationToken);

    public Task DrainAsync()
    {
        lock (_queueLock) return _queue;
    }

    public async Task WaitForBackgroundAsync()
    {
        Task[] background;
        lock (_backgroundLock) background = _backgroundTasks.ToArray();
        if (background.Length > 0)
        {
            try { await Task.WhenAll(background); } catch (OperationCanceledException) { }
        }
        await DrainAsync();
    }

    public ValueTask DisposeAsync() => new(StopAsync());

    private void OnActivePlanChanged(object? sender, ActivePowerPlanChangedEventArgs e)
    {
        _ = EnqueueAsync(async () =>
        {
            var observed = MapPlan(e.SchemeId);
            if (observed is not PowerState state || state == _currentState) return;

            StopSampling();
            CancelCooldown();
            ResetAdaptiveTransition();
            var from = _currentState;
            _currentState = state;
            _stateEnteredAt = _clock.UtcNow;
            AddHistory(from, state, "External Windows power plan change", true);
            var decision = _engine.Evaluate(new ManualStateRequested(_clock.UtcNow, state));

            if (decision.Target == state)
            {
                Publish(state, $"Windows Power Options selected {state} - Manual lock", true, "Manual", Snapshot.CpuPercent, "Windows Power Options", null);
                return;
            }

            await ApplyDecisionAsync(decision, "Windows Power Options");
        });
    }
    private void OnGameDetected(object? sender, GameDetectedEventArgs e)
    {
        _activeGameKey = GameKey(e.Process);
        _ = EnqueueAsync(async () =>
        {
            StopSampling();
            CancelCooldown();
            ResetAdaptiveTransition();
            var decision = _engine.Evaluate(new GameStarted(_clock.UtcNow, _activeGameKey));
            await ApplyDecisionAsync(decision, e.Process.ExecutablePath);
        });
    }

    private void OnGameProcessAdded(object? sender, GameDetectedEventArgs e)
    {
        Publish(_currentState, Snapshot.Reason, Snapshot.IsLatched, Snapshot.LatchType, Snapshot.CpuPercent, e.Process.ExecutablePath, Snapshot.CooldownRemaining);
    }

    private void OnGameLatchReleased(object? sender, EventArgs e)
    {
        var key = _activeGameKey ?? "game";
        _activeGameKey = null;
        _ = EnqueueAsync(async () =>
        {
            ResetAdaptiveTransition();
            var decision = _engine.Evaluate(new GameExited(_clock.UtcNow, key, true));
            await ApplyDecisionAsync(decision, Snapshot.TriggerApplication);
            if (!decision.IsLatched && decision.Target == PowerState.HighPerformance)
                StartCooldown();
        });
    }

    private void StartSampling()
    {
        if (!_started || _lifetimeCts is null || SamplingEnabled || Snapshot.IsLatched) return;
        _samplingCts?.Dispose();
        _samplingCts = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCts.Token);
        var token = _samplingCts.Token;
        var ticks = _tickFactory.Create(TimeSpan.FromSeconds(2));
        var task = RunSamplingAsync(ticks, token);
        TrackBackground(task);
    }


    private async Task RunSamplingAsync(IControllerTickSource ticks, CancellationToken token)
    {
        try
        {
            await using (ticks.ConfigureAwait(false))
            {
                while (await ticks.WaitForNextTickAsync(token).ConfigureAwait(false))
                {
                    var sample = _activity.Sample(_clock.UtcNow);
                    ActivitySampleCount++;
                    if (!sample.Valid) continue;
                    await EnqueueAsync(async () =>
                    {
                        if (Snapshot.IsLatched) return;
                        if (_config.AdaptiveActuationEnabled)
                        {
                            Publish(_currentState, "Adaptive governor evaluating CPU pressure.", false, null, sample.CpuPercent, Snapshot.TriggerApplication, Snapshot.CooldownRemaining);
                            return;
                        }
                        var decision = _engine.Evaluate(new CpuSample(sample.At, sample.CpuPercent));
                        await ApplyDecisionAsync(decision, null, sample.CpuPercent);
                    }).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }
    private void StopSampling()
    {
        var cts = _samplingCts;
        _samplingCts = null;
        if (cts is null) return;
        try { cts.Cancel(); } catch { }
        cts.Dispose();
    }

    private void StartCooldown()
    {
        CancelCooldown();
        if (_lifetimeCts is null) return;
        _cooldownCts = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCts.Token);
        var token = _cooldownCts.Token;
        Publish(_currentState, Snapshot.Reason, false, null, Snapshot.CpuPercent, Snapshot.TriggerApplication, _config.PostGameCooldown);
        var task = RunCooldownAsync(token);
        TrackBackground(task);
    }


    private async Task RunCooldownAsync(CancellationToken token)
    {
        try
        {
            await _delay.DelayAsync(_config.PostGameCooldown, token);
            await EnqueueAsync(async () =>
            {
                var decision = _engine.Evaluate(new CooldownExpired(_clock.UtcNow));
                await ApplyDecisionAsync(decision, null);
                if (!decision.IsLatched) StartSampling();
            });
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }    private void CancelCooldown()
    {
        var cts = _cooldownCts;
        _cooldownCts = null;
        if (cts is null) return;
        try { cts.Cancel(); } catch { }
        cts.Dispose();
    }

    private async Task ApplyDecisionAsync(PolicyDecision decision, string? trigger, double? cpu = null)
    {
        if (decision.Target != _currentState)
        {
            var targetId = ResolvePlan(decision.Target);
            var result = await _plans.ActivateAsync(targetId);
            if (!result.Success)
            {
                AddHistory(_currentState, decision.Target, $"Activation failed: {result.Error}", false);
                var requestedLatchType = decision.IsLatched ? LatchType(decision.Reason) : null;
                if (string.Equals(requestedLatchType, "Manual", StringComparison.OrdinalIgnoreCase))
                    _ = _engine.Evaluate(new ManualStateReleased(_clock.UtcNow));
                _engine.SynchronizeObservedState(_currentState);
                var gameStillLatched = _games.IsLatched;
                Publish(_currentState, $"Power plan activation failed: {result.Error}", gameStillLatched, gameStillLatched ? "Game" : null, cpu ?? Snapshot.CpuPercent, trigger, Snapshot.CooldownRemaining);
                return;
            }
            var from = _currentState;
            _currentState = decision.Target;
            _stateEnteredAt = _clock.UtcNow;
            ResetAdaptiveTransition();
            AddHistory(from, _currentState, decision.Reason, true);
        }

        Publish(_currentState, decision.Reason, decision.IsLatched, decision.IsLatched ? LatchType(decision.Reason) : null, cpu ?? Snapshot.CpuPercent, trigger ?? Snapshot.TriggerApplication, decision.IsLatched ? null : Snapshot.CooldownRemaining);
    }

    private async Task ActivateDirectAsync(PowerState target, string reason, string? trigger, bool latched)
    {
        if (target != _currentState)
        {
            var result = await _plans.ActivateAsync(ResolvePlan(target));
            if (!result.Success)
            {
                AddHistory(_currentState, target, $"Activation failed: {result.Error}", false);
                Publish(_currentState, $"Power plan activation failed: {result.Error}", Snapshot.IsLatched, Snapshot.LatchType, Snapshot.CpuPercent, trigger, Snapshot.CooldownRemaining);
                return;
            }
            var from = _currentState;
            _currentState = target;
            _stateEnteredAt = _clock.UtcNow;
            ResetAdaptiveTransition();
            _engine.SynchronizeObservedState(target);
            AddHistory(from, target, reason, true);
        }
        Publish(_currentState, reason, latched, latched ? "Manual" : null, Snapshot.CpuPercent, trigger, null);
    }

    private Task EnqueueAsync(Func<Task> work)
    {
        lock (_queueLock)
        {
            _queue = _queue.ContinueWith(_ => work(), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default).Unwrap();
            return _queue;
        }
    }

    private void TrackBackground(Task task)
    {
        lock (_backgroundLock) _backgroundTasks.Add(task);
        _ = task.ContinueWith(_ => { lock (_backgroundLock) _backgroundTasks.Remove(task); }, TaskScheduler.Default);
    }

    private bool ShouldApplyAdaptiveTarget(PowerState target, DateTimeOffset now, out string reason)
    {
        if (target == _currentState)
        {
            ResetAdaptiveTransition();
            reason = $"Adaptive governor stable in {_currentState}; no Windows plan transition required.";
            return false;
        }

        if (_adaptivePendingTarget != target || _adaptivePendingSince is null)
        {
            _adaptivePendingTarget = target;
            _adaptivePendingSince = now;
        }

        var qualifiedFor = now - _adaptivePendingSince.Value;
        if (_currentState == PowerState.PowerSaver && target == PowerState.Balanced)
        {
            if (qualifiedFor < _config.CpuPromotionWindow)
            {
                reason = $"Adaptive governor qualifying Balanced for {_config.CpuPromotionWindow.TotalSeconds:0.#}s; sustained demand has held {Math.Max(0, qualifiedFor.TotalSeconds):0.#}s.";
                return false;
            }

            reason = "Adaptive governor sustained demand qualified Balanced.";
            return true;
        }

        if (_currentState == PowerState.Balanced && target == PowerState.PowerSaver)
        {
            var residency = now - _stateEnteredAt;
            if (qualifiedFor < _config.QuietWindow || residency < AdaptiveMinimumBalancedResidency)
            {
                reason = $"Adaptive governor holding Balanced; Eco must remain qualified for {_config.QuietWindow.TotalSeconds:0.#}s and Balanced residency must reach {AdaptiveMinimumBalancedResidency.TotalSeconds:0}s.";
                return false;
            }

            reason = "Adaptive governor sustained quiet qualified Power Saver after minimum Balanced residency.";
            return true;
        }

        reason = $"Adaptive governor qualified transition {_currentState} -> {target}.";
        return true;
    }

    private void ResetAdaptiveTransition()
    {
        _adaptivePendingTarget = null;
        _adaptivePendingSince = null;
    }
    private Guid ResolvePlan(PowerState state) => state switch
    {
        PowerState.PowerSaver => _config.PowerSaverPlanId ?? PowerPlanIds.PowerSaver,
        PowerState.Balanced => _config.BalancedPlanId ?? PowerPlanIds.Balanced,
        PowerState.HighPerformance => _config.HighPerformancePlanId ?? PowerPlanIds.HighPerformance,
        _ => throw new ArgumentOutOfRangeException(nameof(state))
    };

    private PowerState? MapPlan(Guid id)
    {
        if (id == (_config.PowerSaverPlanId ?? PowerPlanIds.PowerSaver)) return PowerState.PowerSaver;
        if (id == (_config.BalancedPlanId ?? PowerPlanIds.Balanced)) return PowerState.Balanced;
        if (id == (_config.HighPerformancePlanId ?? PowerPlanIds.HighPerformance)) return PowerState.HighPerformance;
        return null;
    }

    private void AddHistory(PowerState from, PowerState to, string reason, bool success)
    {
        _history.Enqueue(new TransitionRecord(_clock.UtcNow, from, to, reason, success));
        while (_history.Count > 100) _history.Dequeue();
    }

    private void Publish(PowerState state, string reason, bool latched, string? latchType, double cpu, string? trigger, TimeSpan? cooldown)
    {
        var thresholdProgress = state == PowerState.PowerSaver && _config.CpuPromotionThresholdPercent > 0
            ? Math.Clamp(cpu / _config.CpuPromotionThresholdPercent, 0, 1.5)
            : 0;
        Snapshot = new ControllerSnapshot(state, reason, latched, latchType, cpu, thresholdProgress, cooldown, trigger, _clock.UtcNow, _history.ToArray(), ActivitySampleCount, DashboardTelemetrySampleCount);
        SnapshotChanged?.Invoke(this, Snapshot);
    }

    private ControllerSnapshot NewSnapshot(PowerState state, string reason, bool latched, string? latchType, double cpu, string? trigger, TimeSpan? cooldown) =>
        new(state, reason, latched, latchType, cpu, 0, cooldown, trigger, _clock.UtcNow, Array.Empty<TransitionRecord>(), 0, 0);

    private static string GameKey(GameProcess process) => $"{process.ProcessId}:{process.StartTime.UtcTicks}";
    private static string LatchType(string reason) => reason.Contains("manual", StringComparison.OrdinalIgnoreCase) ? "Manual" : "Game";
}
