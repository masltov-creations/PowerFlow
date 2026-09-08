using PowerFlow.App.Controller;
using PowerFlow.Core.Policy;
using PowerFlow.Core.Rules;
using PowerFlow.Windows.Activity;
using PowerFlow.Windows.Games;
using PowerFlow.Windows.Power;
using Xunit;

namespace PowerFlow.App.Tests.Controller;

public sealed class PowerFlowControllerTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task StartAsync_ReadsActualPlanAndCreatesTwoSecondSamplingLoop()
    {
        var f = new Fixture(PowerPlanIds.Balanced);
        await f.Controller.StartAsync();
        Assert.Equal(PowerState.Balanced, f.Controller.Snapshot.State);
        Assert.Equal(TimeSpan.FromSeconds(2), f.TickFactory.LastPeriod);
        Assert.True(f.Controller.SamplingEnabled);
        Assert.Empty(f.Plans.Activations);
        await f.Controller.StopAsync();
    }

    [Fact]
    public async Task GameStart_StopsSamplingAndLocksHighPerformance()
    {
        var f = new Fixture(PowerPlanIds.PowerSaver);
        await f.Controller.StartAsync();
        f.Games.RaiseGame(new GameProcess(42, "game.exe", null, T0, "test"));
        await f.Controller.DrainAsync();
        Assert.False(f.Controller.SamplingEnabled);
        Assert.Equal(PowerState.HighPerformance, f.Controller.Snapshot.State);
        Assert.True(f.Controller.Snapshot.IsLatched);
        Assert.Equal(PowerPlanIds.HighPerformance, f.Plans.Active);
        await f.Controller.StopAsync();
    }

    [Fact]
    public async Task GameExit_WaitsCooldownThenReturnsBalancedAndRestartsSampling()
    {
        var f = new Fixture(PowerPlanIds.PowerSaver);
        await f.Controller.StartAsync();
        f.Games.RaiseGame(new GameProcess(42, "game.exe", null, T0, "test"));
        await f.Controller.DrainAsync();
        f.Games.RaiseReleased();
        await f.Controller.DrainAsync();
        Assert.Equal(PowerState.HighPerformance, f.Controller.Snapshot.State);
        Assert.False(f.Controller.Snapshot.IsLatched);
        Assert.False(f.Controller.SamplingEnabled);
        Assert.Equal(TimeSpan.FromSeconds(8), f.Delay.LastDelay);

        f.Delay.Complete();
        await f.Controller.WaitForBackgroundAsync();
        Assert.Equal(PowerState.Balanced, f.Controller.Snapshot.State);
        Assert.True(f.Controller.SamplingEnabled);
        await f.Controller.StopAsync();
    }

    [Fact]
    public async Task ManualPerformance_IsLatchedUntilRelease()
    {
        var f = new Fixture(PowerPlanIds.PowerSaver);
        await f.Controller.StartAsync();
        await f.Controller.SetManualStateAsync(PowerState.HighPerformance);
        Assert.False(f.Controller.SamplingEnabled);
        Assert.True(f.Controller.Snapshot.IsLatched);
        await f.Controller.ReleaseManualLatchAsync();
        Assert.Equal(PowerState.Balanced, f.Controller.Snapshot.State);
        Assert.True(f.Controller.SamplingEnabled);
        await f.Controller.StopAsync();
    }

    [Fact]
    public async Task ActivationFailure_DoesNotPretendStateChanged()
    {
        var f = new Fixture(PowerPlanIds.PowerSaver) { };
        f.Plans.FailNext = true;
        await f.Controller.StartAsync();
        f.Games.RaiseGame(new GameProcess(42, "game.exe", null, T0, "test"));
        await f.Controller.DrainAsync();
        Assert.Equal(PowerState.PowerSaver, f.Controller.Snapshot.State);
        Assert.Contains("failed", f.Controller.Snapshot.Reason, StringComparison.OrdinalIgnoreCase);
        await f.Controller.StopAsync();
    }

    [Fact]
    public async Task StartupOnUnlatchedHighPerformance_RecoversToBalancedBeforeSampling()
    {
        var f = new Fixture(PowerPlanIds.HighPerformance);
        await f.Controller.StartAsync();
        Assert.Equal(PowerState.Balanced, f.Controller.Snapshot.State);
        Assert.Contains(PowerPlanIds.Balanced, f.Plans.Activations);
        Assert.True(f.Controller.SamplingEnabled);
        await f.Controller.StopAsync();
    }


    [Fact]
    public async Task GameLatch_RejectsManualDowngradeUntilGameActuallyExits()
    {
        var f = new Fixture(PowerPlanIds.PowerSaver);
        await f.Controller.StartAsync();
        f.Games.RaiseGame(new GameProcess(42, "game.exe", null, T0, "test"));
        await f.Controller.DrainAsync();

        await f.Controller.SetManualStateAsync(PowerState.PowerSaver);
        await f.Controller.SetManualStateAsync(PowerState.Balanced);

        Assert.Equal(PowerState.HighPerformance, f.Controller.Snapshot.State);
        Assert.True(f.Controller.Snapshot.IsLatched);
        Assert.Equal("Game", f.Controller.Snapshot.LatchType);
        Assert.Equal(PowerPlanIds.HighPerformance, f.Plans.Active);
        Assert.DoesNotContain(PowerPlanIds.PowerSaver, f.Plans.Activations.Skip(1));
        Assert.DoesNotContain(PowerPlanIds.Balanced, f.Plans.Activations.Skip(1));
        await f.Controller.StopAsync();
    }    private sealed class Fixture
    {
        public Fixture(Guid active)
        {
            Plans = new FakePlans(active);
            Activity = new FakeActivity();
            Games = new FakeGames();
            TickFactory = new FakeTickFactory();
            Delay = new FakeDelay();
            Clock = new FakeClock(T0);
            Config = PowerFlowConfig.Default;
            Controller = new PowerFlowController(Config, Plans, Activity, Games, TickFactory, Delay, Clock);
        }
        public PowerFlowConfig Config { get; }
        public FakePlans Plans { get; }
        public FakeActivity Activity { get; }
        public FakeGames Games { get; }
        public FakeTickFactory TickFactory { get; }
        public FakeDelay Delay { get; }
        public FakeClock Clock { get; }
        public PowerFlowController Controller { get; }
    }

    private sealed class FakePlans(Guid active) : IPowerPlanController
    {
        public Guid Active { get; set; } = active;
        public bool FailNext { get; set; }
        public List<Guid> Activations { get; } = [];
        public Task<IReadOnlyList<PowerPlanInfo>> ListAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<PowerPlanInfo>>([
            new(PowerPlanIds.PowerSaver, "Power saver"), new(PowerPlanIds.Balanced, "Balanced"), new(PowerPlanIds.HighPerformance, "High performance")]);
        public Task<PowerPlanInfo> GetActiveAsync(CancellationToken cancellationToken = default) => Task.FromResult(new PowerPlanInfo(Active, Active.ToString()));
        public Task<PowerPlanSwitchResult> ActivateAsync(Guid schemeId, CancellationToken cancellationToken = default)
        {
            Activations.Add(schemeId);
            if (FailNext) { FailNext = false; return Task.FromResult(new PowerPlanSwitchResult(false, schemeId, Active, "forced failure")); }
            Active = schemeId;
            return Task.FromResult(new PowerPlanSwitchResult(true, schemeId, Active, null));
        }
    }

    private sealed class FakeActivity : IActivitySource
    {
        public ActivitySample Sample(DateTimeOffset at) => new(5, at, true, TimeSpan.Zero);
    }

    private sealed class FakeGames : IGameLifecycleMonitor
    {
        public event EventHandler<GameDetectedEventArgs>? GameDetected;
        public event EventHandler<GameDetectedEventArgs>? GameProcessAdded;
        public event EventHandler? GameLatchReleased;
        public bool IsLatched { get; private set; }
        public int TrackedCount => IsLatched ? 1 : 0;
        public string? LatchReason => IsLatched ? "test game" : null;
        public void UpdateRules(IReadOnlyList<AppRule> rules) { }
        public void Start() { }
        public void Stop() { }
        public void AddRelatedProcess(GameProcess process) { }
        public void Dispose() { }
        public void RaiseGame(GameProcess process) { IsLatched = true; GameDetected?.Invoke(this, new GameDetectedEventArgs(process, "test game")); }
        public void RaiseReleased() { IsLatched = false; GameLatchReleased?.Invoke(this, EventArgs.Empty); }
    }

    private sealed class FakeTickFactory : IControllerTickSourceFactory
    {
        public TimeSpan LastPeriod { get; private set; }
        public IControllerTickSource Create(TimeSpan period) { LastPeriod = period; return new NeverTickSource(); }
    }
    private sealed class NeverTickSource : IControllerTickSource
    {
        public async ValueTask<bool> WaitForNextTickAsync(CancellationToken token) { await Task.Delay(Timeout.InfiniteTimeSpan, token); return false; }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeDelay : IControllerDelay
    {
        private TaskCompletionSource _tcs = NewTcs();
        public TimeSpan LastDelay { get; private set; }
        public Task DelayAsync(TimeSpan delay, CancellationToken token) { LastDelay = delay; token.Register(() => _tcs.TrySetCanceled(token)); return _tcs.Task; }
        public void Complete() => _tcs.TrySetResult();
        private static TaskCompletionSource NewTcs() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class FakeClock(DateTimeOffset now) : IControllerClock
    {
        public DateTimeOffset UtcNow { get; set; } = now;
    }
}

