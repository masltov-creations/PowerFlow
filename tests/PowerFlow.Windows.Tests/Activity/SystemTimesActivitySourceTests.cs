using PowerFlow.Windows.Activity;
using Xunit;

namespace PowerFlow.Windows.Tests.Activity;

public sealed class SystemTimesActivitySourceTests
{
    [Fact]
    public void Sample_FirstSnapshotReturnsZeroAndEstablishesBaseline()
    {
        var native = new FakeSystemTimes(new SystemTimesSnapshot(10, 20, 20), new SystemTimesSnapshot(20, 40, 40));
        var source = new SystemTimesActivitySource(native);
        var sample = source.Sample(DateTimeOffset.UnixEpoch);
        Assert.Equal(0, sample.CpuPercent);
    }

    [Theory]
    [InlineData(100, 100, 0, 0)]
    [InlineData(100, 100, 100, 50)]
    [InlineData(0, 100, 100, 100)]
    public void Sample_ComputesBusyPercentFromDeltas(ulong idle, ulong kernel, ulong user, double expected)
    {
        var native = new FakeSystemTimes(new SystemTimesSnapshot(0, 0, 0), new SystemTimesSnapshot(idle, kernel, user));
        var source = new SystemTimesActivitySource(native);
        source.Sample(DateTimeOffset.UnixEpoch);
        var sample = source.Sample(DateTimeOffset.UnixEpoch.AddSeconds(2));
        Assert.Equal(expected, sample.CpuPercent, 5);
    }

    [Fact]
    public void Sample_CounterRegressionDoesNotProduceGarbage()
    {
        var native = new FakeSystemTimes(new SystemTimesSnapshot(100, 100, 100), new SystemTimesSnapshot(50, 50, 50));
        var source = new SystemTimesActivitySource(native);
        source.Sample(DateTimeOffset.UnixEpoch);
        var sample = source.Sample(DateTimeOffset.UnixEpoch.AddSeconds(2));
        Assert.Equal(0, sample.CpuPercent);
        Assert.False(sample.Valid);
    }

    private sealed class FakeSystemTimes(params SystemTimesSnapshot[] values) : ISystemTimesNative
    {
        private int _index;
        public SystemTimesSnapshot Read() => values[Math.Min(_index++, values.Length - 1)];
    }
}
