using PowerFlow.App.Controller;
using PowerFlow.App.Dashboard;
using PowerFlow.Core.Policy;
using PowerFlow.Windows.Activity;
using Xunit;

namespace PowerFlow.App.Tests.Dashboard;

public sealed class DashboardSessionTests
{
    [Fact]
    public async Task VisibleSession_StartsOneSecondTelemetryAndDisposesOnStop()
    {
        var source = new FakeTelemetrySource();
        var ticks = new FakeTickFactory();
        await using var sut = new DashboardTelemetrySession(() => source, ticks, new FakeClock());

        await sut.StartAsync();
        Assert.True(sut.IsRunning);
        Assert.Equal(TimeSpan.FromSeconds(1), ticks.LastPeriod);

        ticks.Source.ReleaseOne();
        await sut.FlushAsync();
        Assert.Equal(1, source.ReadCount);
        Assert.NotNull(sut.Latest);

        await sut.StopAsync();
        Assert.False(sut.IsRunning);
        Assert.True(source.Disposed);
    }

    [Fact]
    public async Task HiddenSession_DoesNotKeepSampling()
    {
        var source = new FakeTelemetrySource();
        var ticks = new FakeTickFactory();
        await using var sut = new DashboardTelemetrySession(() => source, ticks, new FakeClock());
        await sut.StartAsync();
        await sut.StopAsync();

        ticks.Source.ReleaseOne();
        await Task.Delay(20);

        Assert.Equal(0, source.ReadCount);
    }

    private sealed class FakeTelemetrySource : IDashboardTelemetrySource
    {
        public int ReadCount { get; private set; }
        public bool Disposed { get; private set; }
        public DashboardTelemetry Read(DateTimeOffset at)
        {
            ReadCount++;
            return new DashboardTelemetry(54.2, 3375, at);
        }
        public void Dispose() => Disposed = true;
    }

    private sealed class FakeClock : IControllerClock
    {
        public DateTimeOffset UtcNow => new(2026, 9, 8, 20, 0, 0, TimeSpan.Zero);
    }

    private sealed class FakeTickFactory : IControllerTickSourceFactory
    {
        public FakeTickSource Source { get; } = new();
        public TimeSpan LastPeriod { get; private set; }
        public IControllerTickSource Create(TimeSpan period) { LastPeriod = period; return Source; }
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

