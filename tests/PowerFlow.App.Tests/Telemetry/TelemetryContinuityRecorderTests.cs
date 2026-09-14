using PowerFlow.App.Controller;
using PowerFlow.App.Telemetry;
using PowerFlow.Core.Policy;
using PowerFlow.Windows.Activity;
using Xunit;

namespace PowerFlow.App.Tests.Telemetry;

public sealed class TelemetryContinuityRecorderTests
{
    [Fact]
    public async Task HiddenAutoStartsFiveSecondRichCadenceAndVisibleLeaseRaisesToOneSecond()
    {
        var source = new FakeTelemetrySource();
        var ticks = new FakeTickFactory();
        await using var sut = NewRecorder(source, ticks, capacity: 8);
        sut.UpdateControllerSnapshot(Snapshot(PowerState.PowerSaver, 8));
        await sut.StartAsync();
        Assert.Equal(TelemetryCadenceMode.HiddenAuto, sut.Mode);
        Assert.Equal(TimeSpan.FromSeconds(5), ticks.Periods[^1]);

        using var lease = sut.AcquireVisibility();
        Assert.Equal(TelemetryCadenceMode.Visible, sut.Mode);
        Assert.Equal(TimeSpan.FromSeconds(1), ticks.Periods[^1]);
        Assert.Equal(1, source.FactoryCount);
    }

    [Fact]
    public async Task TwoVisibleLeasesStillUseOneTelemetrySourceAndLastReleaseReturnsHiddenCadence()
    {
        var source = new FakeTelemetrySource();
        var ticks = new FakeTickFactory();
        await using var sut = NewRecorder(source, ticks, capacity: 8);
        sut.UpdateControllerSnapshot(Snapshot(PowerState.Balanced, 30));
        await sut.StartAsync();

        var a = sut.AcquireVisibility();
        var b = sut.AcquireVisibility();
        Assert.Equal(1, source.FactoryCount);
        Assert.Equal(TelemetryCadenceMode.Visible, sut.Mode);
        a.Dispose();
        Assert.Equal(TelemetryCadenceMode.Visible, sut.Mode);
        b.Dispose();
        Assert.Equal(TelemetryCadenceMode.HiddenAuto, sut.Mode);
        Assert.Equal(TimeSpan.FromSeconds(5), ticks.Periods[^1]);
    }

    [Theory]
    [InlineData("Game")]
    [InlineData("Manual")]
    public async Task HiddenLatchKeepsLowOverheadTelemetryAndVisibleLeaseRaisesCadence(string latchType)
    {
        var source = new FakeTelemetrySource();
        var ticks = new FakeTickFactory();
        await using var sut = NewRecorder(source, ticks, capacity: 8);
        sut.UpdateControllerSnapshot(Snapshot(PowerState.HighPerformance, 4, true, latchType));
        await sut.StartAsync();
        Assert.Equal(TelemetryCadenceMode.HiddenAuto, sut.Mode);
        Assert.Equal(TimeSpan.FromSeconds(5), ticks.Periods[^1]);

        using var lease = sut.AcquireVisibility();
        Assert.Equal(TelemetryCadenceMode.Visible, sut.Mode);
        Assert.Equal(TimeSpan.FromSeconds(1), ticks.Periods[^1]);
    }

    [Fact]
    public async Task UpdateCadence_ReconfiguresCurrentVisibilityModeWithoutReplacingTelemetrySource()
    {
        var source = new FakeTelemetrySource();
        var ticks = new FakeTickFactory();
        await using var sut = NewRecorder(source, ticks, capacity: 8);
        sut.UpdateControllerSnapshot(Snapshot(PowerState.Balanced, 20));
        await sut.StartAsync();
        using var lease = sut.AcquireVisibility();

        sut.UpdateCadence(TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(2));

        Assert.Equal(TelemetryCadenceMode.Visible, sut.Mode);
        Assert.Equal(TimeSpan.FromMilliseconds(500), ticks.Periods[^1]);
        Assert.Equal(1, source.FactoryCount);
    }
    [Fact]
    public async Task ControllerSnapshotsPopulateCpuStateWithoutReadingRichSourceAndRingIsBounded()
    {
        var source = new FakeTelemetrySource();
        var ticks = new FakeTickFactory();
        await using var sut = NewRecorder(source, ticks, capacity: 3);
        for (var i = 0; i < 5; i++) sut.UpdateControllerSnapshot(Snapshot(PowerState.Balanced, 10 + i, at: BaseTime.AddSeconds(i)));
        await sut.StartAsync();

        Assert.Equal(0, source.ReadCount);
        Assert.Equal(3, sut.History.Count);
        Assert.Equal(12, sut.History[0].CpuPercent);
        Assert.All(sut.History, x => Assert.Equal(PowerState.Balanced, x.State));
    }

    [Fact]
    public async Task RichSampleMergesLatestControllerStateAndPreservesUnavailableValuesAsNull()
    {
        var source = new FakeTelemetrySource { Next = new DashboardTelemetry(null, null, BaseTime.AddSeconds(5)) };
        var ticks = new FakeTickFactory();
        await using var sut = NewRecorder(source, ticks, capacity: 8);
        sut.UpdateControllerSnapshot(Snapshot(PowerState.PowerSaver, 7));
        await sut.StartAsync();
        ticks.Current.ReleaseOne();
        await sut.WaitForRichSampleAsync();

        var latest = sut.History[^1];
        Assert.Equal(7, latest.CpuPercent);
        Assert.Equal(PowerState.PowerSaver, latest.State);
        Assert.Null(latest.PackageWatts);
        Assert.Null(latest.AverageMhz);
    }

    [Fact]
    public async Task RichSamplePreservesTruthfulCoreAvailability()
    {
        var source = new FakeTelemetrySource { Next = new DashboardTelemetry(42, 3800, BaseTime.AddSeconds(5), null, "TEST-HOST", null, 24) };
        var ticks = new FakeTickFactory();
        await using var sut = NewRecorder(source, ticks, capacity: 8);
        sut.UpdateControllerSnapshot(Snapshot(PowerState.Balanced, 35));
        await sut.StartAsync();

        ticks.Current.ReleaseOne();
        await sut.WaitForRichSampleAsync();

        var latest = sut.History[^1];
        Assert.Null(latest.ActiveCores);
        Assert.Equal(24, latest.TotalCores);
    }
    [Fact]
    public async Task RichSourceExceptionDoesNotEscapeRecorderOrEraseContinuity()
    {
        var source = new FakeTelemetrySource { ThrowOnRead = true };
        var ticks = new FakeTickFactory();
        await using var sut = NewRecorder(source, ticks, capacity: 8);
        sut.UpdateControllerSnapshot(Snapshot(PowerState.PowerSaver, 6));
        await sut.StartAsync();
        var before = sut.History.Count;
        ticks.Current.ReleaseOne();
        await Task.Delay(30);
        Assert.Equal(before, sut.History.Count);
        Assert.True(sut.IsRichTelemetryDegraded);
    }

    private static readonly DateTimeOffset BaseTime = new(2026, 9, 8, 21, 0, 0, TimeSpan.Zero);

    private static TelemetryContinuityRecorder NewRecorder(FakeTelemetrySource source, FakeTickFactory ticks, int capacity) =>
        new(() => { source.FactoryCount++; return source; }, ticks, new FakeClock(), capacity);

    private static ControllerSnapshot Snapshot(PowerState state, double cpu, bool latched = false, string? latchType = null, DateTimeOffset? at = null) =>
        new(state, "test", latched, latchType, cpu, .25, null, null, at ?? BaseTime, Array.Empty<TransitionRecord>(), 0, 0);

    private sealed class FakeTelemetrySource : IDashboardTelemetrySource
    {
        public int FactoryCount { get; set; }
        public int ReadCount { get; private set; }
        public bool ThrowOnRead { get; set; }
        public DashboardTelemetry Next { get; set; } = new(55.5, 3600, BaseTime.AddSeconds(5));
        public DashboardTelemetry Read(DateTimeOffset at)
        {
            ReadCount++;
            if (ThrowOnRead) throw new InvalidOperationException("boom");
            return Next with { At = at };
        }
        public void Dispose() { }
    }

    private sealed class FakeClock : IControllerClock
    {
        private long _ticks;
        public DateTimeOffset UtcNow => BaseTime.AddSeconds(Interlocked.Increment(ref _ticks));
    }

    private sealed class FakeTickFactory : IControllerTickSourceFactory
    {
        public List<TimeSpan> Periods { get; } = [];
        public FakeTickSource Current { get; private set; } = new();
        public IControllerTickSource Create(TimeSpan period)
        {
            Periods.Add(period);
            Current = new FakeTickSource();
            return Current;
        }
    }

    private sealed class FakeTickSource : IControllerTickSource
    {
        private readonly SemaphoreSlim _semaphore = new(0);
        public void ReleaseOne() => _semaphore.Release();
        public async ValueTask<bool> WaitForNextTickAsync(CancellationToken token)
        {
            await _semaphore.WaitAsync(token);
            return true;
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
