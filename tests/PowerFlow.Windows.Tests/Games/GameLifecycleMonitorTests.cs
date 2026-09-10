using PowerFlow.Core.Rules;
using PowerFlow.Core.Envelope;
using PowerFlow.Windows.Games;
using Xunit;

namespace PowerFlow.Windows.Tests.Games;

public sealed class GameLifecycleMonitorTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ExplicitPerformanceRule_LatchesAndStopsStartWatcherForDirectGame()
    {
        var source = new FakeSource();
        var factory = new FakeHandleFactory();
        var sut = new GameLifecycleMonitor(source, factory);
        sut.UpdateRules([new AppRule(@"C:\Games\Foo\game.exe", AppRuleMode.Performance, "Foo", FollowChildren: true)]);
        sut.Start();

        source.Raise(new ProcessStartEvent(42, 1, @"C:\Games\Foo\game.exe", T0));

        Assert.True(sut.IsLatched);
        Assert.Equal(1, sut.TrackedCount);
        Assert.False(source.IsRunning);
        Assert.Contains("explicit", sut.LatchReason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ExplicitSemanticEntitlement_DoesNotBecomeLegacyPerformanceLatch()
    {
        var source = new FakeSource();
        var factory = new FakeHandleFactory();
        var sut = new GameLifecycleMonitor(source, factory);
        var entitlement = new PerformanceEntitlement(EnvelopeZone.Responsive, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(5), true);
        sut.UpdateRules([new AppRule(@"C:\Apps\Editor\editor.exe", AppRuleMode.Performance, "Editor", FollowChildren: true, Entitlement: entitlement)]);
        sut.Start();
        source.Raise(new ProcessStartEvent(42, 1, @"C:\Apps\Editor\editor.exe", T0));
        Assert.False(sut.IsLatched);
        Assert.Equal(0, sut.TrackedCount);
        Assert.True(source.IsRunning);
    }
    [Fact]
    public void LauncherToChildHandoff_PreservesLatchAndThenStopsWatcher()
    {
        var source = new FakeSource();
        var factory = new FakeHandleFactory();
        var sut = new GameLifecycleMonitor(source, factory);
        sut.UpdateRules([new AppRule(@"C:\Launcher\launcher.exe", AppRuleMode.Performance, "Launcher", FollowChildren: true)]);
        sut.Start();

        source.Raise(new ProcessStartEvent(10, 1, @"C:\Launcher\launcher.exe", T0));
        Assert.True(source.IsRunning);
        source.Raise(new ProcessStartEvent(20, 10, @"C:\Games\RealGame\realgame.exe", T0.AddSeconds(2)));

        Assert.Equal(2, sut.TrackedCount);
        Assert.False(source.IsRunning);

        factory.Exit(10);
        Assert.True(sut.IsLatched);
        factory.Exit(20);
        Assert.False(sut.IsLatched);
    }

    [Fact]
    public void MultipleTrackedGameProcesses_ReleaseOnlyWhenSetIsEmpty()
    {
        var source = new FakeSource();
        var factory = new FakeHandleFactory();
        var sut = new GameLifecycleMonitor(source, factory);
        sut.UpdateRules([new AppRule("game.exe", AppRuleMode.Performance)]);
        sut.Start();
        source.Raise(new ProcessStartEvent(42, 1, "game.exe", T0));
        sut.AddRelatedProcess(new GameProcess(43, "helper.exe", 42, T0.AddSeconds(1), "child"));
        factory.Exit(42);
        Assert.True(sut.IsLatched);
        factory.Exit(43);
        Assert.False(sut.IsLatched);
    }

    [Fact]
    public void StaleExitForReusedPid_DoesNotReleaseNewerProcess()
    {
        var source = new FakeSource();
        var factory = new FakeHandleFactory();
        var sut = new GameLifecycleMonitor(source, factory);
        sut.AddRelatedProcess(new GameProcess(55, "game.exe", null, T0, "test"));
        sut.AddRelatedProcess(new GameProcess(55, "game.exe", null, T0.AddMinutes(1), "test-new"));
        factory.Exit(55, T0);
        Assert.True(sut.IsLatched);
        Assert.Equal(1, sut.TrackedCount);
    }

    [Fact]
    public void GameProcessExit_RaisesReleaseOnlyWhenActuallyGone()
    {
        var source = new FakeSource();
        var factory = new FakeHandleFactory();
        var sut = new GameLifecycleMonitor(source, factory);
        var released = 0;
        sut.GameLatchReleased += (_, _) => released++;
        sut.AddRelatedProcess(new GameProcess(77, "game.exe", null, T0, "test"));
        factory.Exit(77);
        Assert.Equal(1, released);
        Assert.False(sut.IsLatched);
    }


    [Fact]
    public void ExistingGameAtStart_IsLatchedFromOneBoundedSnapshot()
    {
        var source = new FakeSource();
        source.Existing.Add(new ProcessStartEvent(88, 1, @"C:\Games\Existing\game.exe", T0));
        var factory = new FakeHandleFactory();
        var sut = new GameLifecycleMonitor(source, factory);
        sut.UpdateRules([new AppRule(@"C:\Games\Existing\game.exe", AppRuleMode.Performance, "Existing Game")]);
        sut.Start();
        Assert.True(sut.IsLatched);
        Assert.Equal(1, source.SnapshotCalls);
        Assert.False(source.IsRunning);
    }
    private sealed class FakeSource : IProcessEventSource
    {
        public event EventHandler<ProcessStartEvent>? ProcessStarted;
        public List<ProcessStartEvent> Existing { get; } = [];
        public int SnapshotCalls { get; private set; }
        public bool IsRunning { get; private set; }
        public IReadOnlyList<ProcessStartEvent> SnapshotExisting() { SnapshotCalls++; return Existing; }
        public void Start() => IsRunning = true;
        public void Stop() => IsRunning = false;
        public void Dispose() => Stop();
        public void Raise(ProcessStartEvent e) { if (IsRunning) ProcessStarted?.Invoke(this, e); }
    }

    private sealed class FakeHandleFactory : IProcessHandleFactory
    {
        private readonly Dictionary<(int, DateTimeOffset), FakeHandle> _handles = new();
        public ITrackedProcessHandle Open(GameProcess process)
        {
            var handle = new FakeHandle(process);
            _handles[(process.ProcessId, process.StartTime)] = handle;
            return handle;
        }
        public void Exit(int pid) => _handles.Where(x => x.Key.Item1 == pid).OrderByDescending(x => x.Key.Item2).First().Value.RaiseExit();
        public void Exit(int pid, DateTimeOffset start) => _handles[(pid, start)].RaiseExit();
    }

    private sealed class FakeHandle(GameProcess process) : ITrackedProcessHandle
    {
        public GameProcess Process { get; } = process;
        public event EventHandler? Exited;
        public void EnableExitEvents() { }
        public void Dispose() { }
        public void RaiseExit() => Exited?.Invoke(this, EventArgs.Empty);
    }
}


