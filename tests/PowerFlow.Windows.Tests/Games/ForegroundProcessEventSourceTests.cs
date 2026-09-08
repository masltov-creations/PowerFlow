using PowerFlow.Windows.Games;
using Xunit;

namespace PowerFlow.Windows.Tests.Games;

public sealed class ForegroundProcessEventSourceTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void StartAndStop_ControlHookWithoutEnumeratingProcesses()
    {
        var hook = new FakeHook();
        var resolver = new FakeResolver();
        using var sut = new ForegroundProcessEventSource(hook, resolver);

        sut.Start();
        Assert.True(sut.IsRunning);
        Assert.Equal(1, hook.StartCalls);
        Assert.Equal(0, resolver.SnapshotCalls);

        sut.Stop();
        Assert.False(sut.IsRunning);
        Assert.Equal(1, hook.StopCalls);
    }

    [Fact]
    public void ForegroundEvent_ResolvesAndEmitsCandidate()
    {
        var hook = new FakeHook();
        var resolver = new FakeResolver
        {
            ByWindow = { [new IntPtr(42)] = new ProcessStartEvent(777, 88, @"C:\Games\Example\game.exe", T0) }
        };
        using var sut = new ForegroundProcessEventSource(hook, resolver);
        ProcessStartEvent? seen = null;
        sut.ProcessStarted += (_, e) => seen = e;

        sut.Start();
        hook.Raise(new IntPtr(42));

        Assert.NotNull(seen);
        Assert.Equal(777, seen!.ProcessId);
        Assert.Equal(88, seen.ParentProcessId);
        Assert.Equal(@"C:\Games\Example\game.exe", seen.ExecutablePath);
        Assert.Equal(1, resolver.ResolveCalls);
    }

    [Fact]
    public void SnapshotExisting_IsOneExplicitBoundedOperation()
    {
        var expected = new[] { new ProcessStartEvent(55, null, @"C:\Games\AlreadyRunning.exe", T0) };
        var resolver = new FakeResolver { Snapshot = expected };
        using var sut = new ForegroundProcessEventSource(new FakeHook(), resolver);

        var actual = sut.SnapshotExisting();

        Assert.Same(expected, actual);
        Assert.Equal(1, resolver.SnapshotCalls);
    }

    private sealed class FakeHook : IForegroundEventHook
    {
        public event EventHandler<IntPtr>? ForegroundChanged;
        public int StartCalls { get; private set; }
        public int StopCalls { get; private set; }
        public bool IsRunning { get; private set; }
        public void Start() { StartCalls++; IsRunning = true; }
        public void Stop() { if (!IsRunning) return; StopCalls++; IsRunning = false; }
        public void Raise(IntPtr hwnd) { if (IsRunning) ForegroundChanged?.Invoke(this, hwnd); }
        public void Dispose() => Stop();
    }

    private sealed class FakeResolver : IWindowProcessResolver
    {
        public Dictionary<IntPtr, ProcessStartEvent> ByWindow { get; } = new();
        public IReadOnlyList<ProcessStartEvent> Snapshot { get; set; } = Array.Empty<ProcessStartEvent>();
        public int ResolveCalls { get; private set; }
        public int SnapshotCalls { get; private set; }
        public ProcessStartEvent? Resolve(IntPtr hwnd) { ResolveCalls++; return ByWindow.GetValueOrDefault(hwnd); }
        public IReadOnlyList<ProcessStartEvent> SnapshotExisting() { SnapshotCalls++; return Snapshot; }
    }
}
