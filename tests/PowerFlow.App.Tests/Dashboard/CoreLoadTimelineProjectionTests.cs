using PowerFlow.App.Dashboard;
using PowerFlow.Windows.Activity;
using Xunit;

namespace PowerFlow.App.Tests.Dashboard;

public sealed class CoreLoadTimelineProjectionTests
{
    [Fact]
    public void Project_PreservesLoadAndFrequencyForActiveCores()
    {
        var sample = CoreStateTimelineProjection.Project(DateTimeOffset.UtcNow, new[]
        {
            new LogicalProcessorTelemetry(0, 0, false, 60, 4200, 84),
            new LogicalProcessorTelemetry(1, 0, false, 30, 4200, 84),
            new LogicalProcessorTelemetry(2, 1, false, 2, 1800, 36),
            new LogicalProcessorTelemetry(3, 1, false, 1, 1800, 36),
            new LogicalProcessorTelemetry(4, 2, true, 0, 0, 0),
            new LogicalProcessorTelemetry(5, 2, true, 0, 0, 0)
        });

        Assert.Equal(1, sample.ActiveCores);
        Assert.Equal(1, sample.AwakeIdleCores);
        Assert.Equal(1, sample.ParkedCores);
        Assert.Equal(90, Assert.Single(sample.ActiveCoreLoadsPercent!), 3);
        Assert.Equal(46.5, sample.AverageAwakeLoadPercent, 3);
        Assert.Equal(3000, sample.AverageAwakeFrequencyMhz!.Value, 3);
        Assert.Equal(60, sample.AverageAwakePercentOfMaximumFrequency!.Value, 3);
    }
}