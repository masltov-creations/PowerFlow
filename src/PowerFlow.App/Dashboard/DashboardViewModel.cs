using System.ComponentModel;
using System.Runtime.CompilerServices;
using PowerFlow.App.Controller;
using PowerFlow.App.Telemetry;
using PowerFlow.Core.Envelope;
using PowerFlow.Core.Policy;
using PowerFlow.Core.Rules;
using PowerFlow.Windows.Activity;

namespace PowerFlow.App.Dashboard;

public sealed class DashboardViewModel : INotifyPropertyChanged
{
    private readonly List<DashboardSample> _samples = [];
    private readonly List<OperatingObservation> _operatingHistory = [];
    private ControllerSnapshot? _snapshot;
    private DashboardTelemetry? _telemetry;
    private PowerFlowConfig _config = PowerFlowConfig.Default;
    private DateTimeOffset? _lastSampleAt;
    private IReadOnlyList<TransitionRecord> _history = Array.Empty<TransitionRecord>();
    private string _governorDryRunLabel = "DRY RUN · OBSERVING";
    private string _governorDryRunExplanation = "Waiting for retained observations";
    private GovernorDecision? _latestGovernorDecision;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string StateLabel => _snapshot switch
    {
        { IsLatched: true, LatchType: "Manual", State: PowerState.PowerSaver } => "POWER SAVER LOCKED",
        { IsLatched: true, LatchType: "Manual", State: PowerState.Balanced } => "BALANCED LOCKED",
        { IsLatched: true, State: PowerState.HighPerformance } => "PERFORMANCE LOCKED",
        { State: PowerState.PowerSaver } => "POWER SAVER",
        { State: PowerState.Balanced } => "BALANCED",
        { State: PowerState.HighPerformance } => "PERFORMANCE",
        _ => "STARTING"
    };

    public string Reason => _snapshot?.Reason ?? "Initializing controller";
    public string BadgeLabel => _snapshot?.LatchType ?? (_snapshot?.CooldownRemaining is not null ? "Cooldown" : "Automatic");
    public string TriggerApplication => string.IsNullOrWhiteSpace(_snapshot?.TriggerApplication) ? "No explicit trigger" : _snapshot!.TriggerApplication!;
    public double CpuPercent => Math.Clamp(_snapshot?.CpuPercent ?? 0, 0, 100);
    public string CpuLabel => _snapshot is null ? "—" : $"{_snapshot.CpuPercent:0.0}%";
    public string WattsLabel => _telemetry?.PackageWatts is double watts ? $"{watts:0.0} W" : "—";
    public string FrequencyLabel => _telemetry?.AverageMhz is double mhz ? $"{mhz / 1000d:0.00} GHz" : "—";
    public double? MemoryPercent => _telemetry?.MemoryUsedPercent;
    public string MemoryLabel => MemoryPercent is double value ? $"{value:0}%" : "-";
    public string MachineLabel => string.IsNullOrWhiteSpace(_telemetry?.MachineName) ? string.Empty : _telemetry!.MachineName!;
    public bool HasMachineLabel => !string.IsNullOrWhiteSpace(MachineLabel);
    public bool IsPowerSaverSelected => _snapshot?.State == PowerState.PowerSaver;
    public bool IsBalancedSelected => _snapshot?.State == PowerState.Balanced;
    public bool IsPerformanceSelected => _snapshot?.State == PowerState.HighPerformance;
    public bool IsAutoSelected => _snapshot is null || !(_snapshot.IsLatched && string.Equals(_snapshot.LatchType, "Manual", StringComparison.OrdinalIgnoreCase));
    public bool ManualModeEnabled => !(_snapshot?.IsLatched == true && string.Equals(_snapshot.LatchType, "Game", StringComparison.OrdinalIgnoreCase));
    public string ControlBadgeLabel => _snapshot switch
    {
        { IsLatched: true, LatchType: "Manual" } => "MANUAL LOCK",
        { IsLatched: true, LatchType: "Game" } => "GAME LATCH",
        _ => "AUTO"
    };
    public string AutoModeDetail => _snapshot switch
    {
        { IsLatched: true, LatchType: "Manual" } => "Release manual lock",
        { IsLatched: true, LatchType: "Game" } => "Game latch active",
        _ => "Rules · apps · smart"
    };
    public bool IsManualLatch => _snapshot?.IsLatched == true && string.Equals(_snapshot.LatchType, "Manual", StringComparison.OrdinalIgnoreCase);
    public string ControlOwnerLabel => _snapshot switch
    {
        { IsLatched: true, LatchType: "Manual" } => "Manual lock",
        { IsLatched: true, LatchType: "Game" } => "Game latch",
        _ => "Auto"
    };
    public string ControlDetailLabel => _snapshot switch
    {
        { IsLatched: true, LatchType: "Manual" } => $"Manual hold on {StateLabel.Replace(" LOCKED", "", StringComparison.Ordinal)}",
        { IsLatched: true, LatchType: "Game", TriggerApplication: { Length: > 0 } app } => $"Held by {app}",
        { IsLatched: true, LatchType: "Game" } => "Held until the tracked game exits",
        _ => "Rules and sustained load choose the state"
    };
    public string CauseLabel => string.IsNullOrWhiteSpace(_snapshot?.TriggerApplication)
        ? Reason
        : $"{_snapshot!.TriggerApplication} - {Reason}";
    public string CooldownLabel => _snapshot?.CooldownRemaining is { } remaining && remaining > TimeSpan.Zero
        ? $"Cooldown {Math.Ceiling(remaining.TotalSeconds):0}s"
        : "No cooldown";
    public string PolicyProgressLabel => _snapshot is null
        ? "Establishing policy"
        : $"{Math.Clamp(_snapshot.ThresholdProgress, 0, 1) * 100:0}% toward decision";
    public IReadOnlyList<string> ActiveSourceLines
    {
        get
        {
            var lines = new List<string>();
            if (!string.IsNullOrWhiteSpace(_snapshot?.TriggerApplication))
                lines.Add($"Trigger: {_snapshot!.TriggerApplication}");
            foreach (var rule in RuleCards.Where(x => x.Status is "ACTIVE" or "LOCKED").Take(3))
                lines.Add($"{rule.Title}: {rule.Status}");
            if (lines.Count == 0) lines.Add("No explicit active source");
            return lines;
        }
    }
    public double FlowProgress => Math.Clamp(_snapshot?.ThresholdProgress ?? 0, 0, 1);
    public IReadOnlyList<TransitionRecord> History => _history;
    public IReadOnlyList<string> RecentEventLines => _history.Take(4).Select(FormatTransition).ToArray();
    public IReadOnlyList<DashboardSample> Samples => _samples;
    public IReadOnlyList<OperatingObservation> OperatingHistory => _operatingHistory;
    public string GovernorDryRunLabel => _governorDryRunLabel;
    public string GovernorDryRunExplanation => _governorDryRunExplanation;
    public GovernorDecision? LatestGovernorDecision => _latestGovernorDecision;
    public double PromotionThresholdPercent => _config.CpuPromotionThresholdPercent;
    public double QuietThresholdPercent => _config.QuietThresholdPercent;
    public string PromotionRuleLabel => $"CPU > {_config.CpuPromotionThresholdPercent:0.#}% for {_config.CpuPromotionWindow.TotalSeconds:0.#}s";
    public string QuietRuleLabel => $"CPU < {_config.QuietThresholdPercent:0.#}% for {_config.QuietWindow.TotalSeconds:0.#}s";
    public IReadOnlyList<DashboardRuleCard> RuleCards => _snapshot is null ? Array.Empty<DashboardRuleCard>() : DashboardRuleProjection.Create(_config, _snapshot);
    public DecisionMeterState? DecisionMeter => _snapshot is null ? null : DecisionMeterProjection.Create(_config, _snapshot);

    public string NextActionLabel => _snapshot switch
    {
        { IsLatched: true, LatchType: "Game" } => "Held until game exits",
        { IsLatched: true, LatchType: "Manual" } => "Held until you release",
        { CooldownRemaining: TimeSpan t } => $"Cooling down · {Math.Max(0, (int)Math.Ceiling(t.TotalSeconds))}s",
        { State: PowerState.PowerSaver } => "Watching for sustained load",
        { State: PowerState.Balanced } => "Returns to Power Saver when quiet",
        { State: PowerState.HighPerformance } => "Performance active",
        _ => "Establishing state"
    };

    public void Configure(PowerFlowConfig config)
    {
        _config = config;
        if (_samples.Count > 0) RebuildOperatingHistory();
        RaiseAll(historyChanged: false);
    }

    public void UpdateContinuity(ControllerSnapshot snapshot, IReadOnlyList<ContinuitySample> continuity, DashboardTelemetry? telemetry)
    {
        var projectedHistory = snapshot.History.Reverse().ToArray();
        var historyChanged = projectedHistory.Length != _history.Count || !projectedHistory.SequenceEqual(_history);
        if (historyChanged) _history = projectedHistory;
        _snapshot = snapshot;
        _telemetry = telemetry;
        _samples.Clear();
        foreach (var sample in continuity.OrderBy(x => x.At))
            _samples.Add(new DashboardSample(sample.At, sample.CpuPercent, sample.PackageWatts, sample.AverageMhz, sample.State, sample.TriggerApplication, sample.ActiveCores, sample.TotalCores, sample.DemandPressure?.PressurePercent));
        _lastSampleAt = _samples.Count > 0 ? _samples[^1].At : null;
        RebuildOperatingHistory();
        RaiseAll(historyChanged);
    }
    public void Update(ControllerSnapshot snapshot, DashboardTelemetry? telemetry)
    {
        var projectedHistory = snapshot.History.Reverse().ToArray();
        var historyChanged = projectedHistory.Length != _history.Count || !projectedHistory.SequenceEqual(_history);
        if (historyChanged) _history = projectedHistory;

        _snapshot = snapshot;
        if (telemetry is not null)
        {
            _telemetry = telemetry;
            if (_lastSampleAt != telemetry.At)
            {
                _samples.Add(new DashboardSample(telemetry.At, snapshot.CpuPercent, telemetry.PackageWatts, telemetry.AverageMhz, snapshot.State, snapshot.TriggerApplication, telemetry.ActiveCores, telemetry.TotalCores, DemandPressureModel.Project(telemetry)?.PressurePercent));
                _lastSampleAt = telemetry.At;
                while (_samples.Count > 120) _samples.RemoveAt(0);
                RebuildOperatingHistory();
            }
        }
        RaiseAll(historyChanged);
    }

    private void RebuildOperatingHistory()
    {
        _operatingHistory.Clear();
        var orderedSamples = _samples.OrderBy(sample => sample.At).ToArray();
        var hasObservedPressure = orderedSamples.Any(sample => sample.PressurePercent is not null);
        var raw = orderedSamples
            .Where(sample => !hasObservedPressure || sample.PressurePercent is not null)
            .Select(OperatingObservationProjection.FromDashboardSample)
            .ToArray();
        if (raw.Length == 0)
        {
            _governorDryRunLabel = "DRY RUN · OBSERVING";
            _governorDryRunExplanation = "Waiting for retained observations";
            return;
        }

        var calibration = EnvelopeCalibration.Calibrate(raw);
        var settings = _config.EffectiveAdaptiveGovernorSettings;
        var learningModel = settings.ResolveLearningModel(calibration);
        var tuning = settings.EffectiveTuning;
        var effectiveEnvelope = tuning.ApplyTo(learningModel.Envelope);
        var governor = new EnvelopeGovernor();
        GovernorDecision? latest = null;
        foreach (var observation in raw)
        {
            var entitlement = tuning.ApplyTo(ResolveDryRunEntitlement(observation.Actor));
            var decision = governor.Evaluate(observation, effectiveEnvelope, entitlement, observation.At, learningModel.Confidence, tuning.ManualOverrideZone);
            latest = decision;
            _operatingHistory.Add(new OperatingObservation(
                observation.At,
                observation.CpuPressurePercent,
                observation.PackageWatts,
                observation.EffectiveClockMhz,
                observation.ActiveCores,
                observation.TotalCores,
                decision.AllowedZone,
                observation.Actor,
                decision.Kind,
                observation.ProcessorPerformancePercent));
        }

        _governorDryRunLabel = latest!.Kind switch
        {
            EnvelopeDecisionKind.Brake => "DRY RUN · BRAKE",
            EnvelopeDecisionKind.Qualifying => "DRY RUN · QUALIFYING",
            EnvelopeDecisionKind.Lease => "DRY RUN · LEASE",
            _ => $"DRY RUN · {latest.AllowedZone.ToString().ToUpperInvariant()}"
        };
        _latestGovernorDecision = latest;
        _governorDryRunExplanation = latest.Explanation;
    }

    private PerformanceEntitlement ResolveDryRunEntitlement(string? actor)
    {
        if (string.IsNullOrWhiteSpace(actor)) return PerformanceEntitlement.LegacyPerformance;
        var rule = _config.AppRules.FirstOrDefault(candidate => DryRunActorMatches(candidate.ExecutablePath, actor));
        return rule?.EffectiveEntitlement ?? PerformanceEntitlement.LegacyPerformance;
    }

    private static bool DryRunActorMatches(string executablePath, string actor)
    {
        if (string.Equals(executablePath, actor, StringComparison.OrdinalIgnoreCase)) return true;
        try { return string.Equals(Path.GetFileName(executablePath), Path.GetFileName(actor), StringComparison.OrdinalIgnoreCase); }
        catch { return false; }
    }

    private void RaiseAll(bool historyChanged)
    {
        OnPropertyChanged(nameof(StateLabel));
        OnPropertyChanged(nameof(Reason));
        OnPropertyChanged(nameof(BadgeLabel));
        OnPropertyChanged(nameof(TriggerApplication));
        OnPropertyChanged(nameof(CpuLabel));
        OnPropertyChanged(nameof(WattsLabel));
        OnPropertyChanged(nameof(FrequencyLabel));
        OnPropertyChanged(nameof(OperatingHistory));
        OnPropertyChanged(nameof(GovernorDryRunLabel));
        OnPropertyChanged(nameof(GovernorDryRunExplanation));
        OnPropertyChanged(nameof(MemoryPercent));
        OnPropertyChanged(nameof(MemoryLabel));
        OnPropertyChanged(nameof(MachineLabel));
        OnPropertyChanged(nameof(HasMachineLabel));
        OnPropertyChanged(nameof(IsPowerSaverSelected));
        OnPropertyChanged(nameof(IsBalancedSelected));
        OnPropertyChanged(nameof(IsPerformanceSelected));
        OnPropertyChanged(nameof(IsAutoSelected));
        OnPropertyChanged(nameof(ManualModeEnabled));
        OnPropertyChanged(nameof(ControlBadgeLabel));
        OnPropertyChanged(nameof(AutoModeDetail));
        OnPropertyChanged(nameof(CpuPercent));
        OnPropertyChanged(nameof(IsManualLatch));
        OnPropertyChanged(nameof(ControlOwnerLabel));
        OnPropertyChanged(nameof(ControlDetailLabel));
        OnPropertyChanged(nameof(CauseLabel));
        OnPropertyChanged(nameof(CooldownLabel));
        OnPropertyChanged(nameof(PolicyProgressLabel));
        OnPropertyChanged(nameof(ActiveSourceLines));
        OnPropertyChanged(nameof(FlowProgress));
        if (historyChanged) OnPropertyChanged(nameof(History));
        if (historyChanged) OnPropertyChanged(nameof(RecentEventLines));
        OnPropertyChanged(nameof(Samples));
        OnPropertyChanged(nameof(PromotionThresholdPercent));
        OnPropertyChanged(nameof(QuietThresholdPercent));
        OnPropertyChanged(nameof(PromotionRuleLabel));
        OnPropertyChanged(nameof(QuietRuleLabel));
        OnPropertyChanged(nameof(RuleCards));
        OnPropertyChanged(nameof(DecisionMeter));
        OnPropertyChanged(nameof(NextActionLabel));
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    private static string FormatTransition(TransitionRecord transition)
        => $"{transition.At:HH:mm}  {StateName(transition.To)} — {transition.Reason}";

    private static string StateName(PowerState state) => state switch
    {
        PowerState.PowerSaver => "Power Saver",
        PowerState.Balanced => "Balanced",
        PowerState.HighPerformance => "Performance",
        _ => state.ToString()
    };
}
