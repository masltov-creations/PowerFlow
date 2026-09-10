using PowerFlow.App.Telemetry;
using PowerFlow.Windows.Activity;
using Xunit;

namespace PowerFlow.App.Tests.Telemetry;

public sealed class DemandPressureTelemetryTests
{
    [Fact]
    public void Project_UsesAwakeSaturationWhenFewCoresCarryTheWork()
    {
        var telemetry = new DashboardTelemetry(null, null, DateTimeOffset.UtcNow,
            LogicalProcessors: new[]
            {
                Thread(0, 0, false, 80), Thread(1, 0, false, 0),
                Thread(2, 1, false, 20), Thread(3, 1, false, 0),
                Thread(4, 2, true, 0), Thread(5, 2, true, 0),
                Thread(6, 3, true, 0), Thread(7, 3, true, 0)
            }, ProcessorQueueLength: 0);

        var result = Assert.IsType<DemandPressureTelemetry>(DemandPressureModel.Project(telemetry));
        Assert.Equal(25, result.DemandPercent, 3);
        Assert.Equal(50, result.AwakeSaturationPercent, 3);
        Assert.Equal(50, result.PressurePercent, 3);
        Assert.Equal("SATURATION", result.Driver);
        Assert.Equal(2, result.AwakeCores);
    }

    [Fact]
    public void Project_QueueContentionCanBecomeDominantPressure()
    {
        var telemetry = new DashboardTelemetry(null, null, DateTimeOffset.UtcNow,
            LogicalProcessors: new[]
            {
                Thread(0, 0, false, 10), Thread(1, 0, false, 0),
                Thread(2, 1, false, 10), Thread(3, 1, false, 0),
                Thread(4, 2, true, 0), Thread(5, 2, true, 0),
                Thread(6, 3, true, 0), Thread(7, 3, true, 0)
            }, ProcessorQueueLength: 2);

        var result = Assert.IsType<DemandPressureTelemetry>(DemandPressureModel.Project(telemetry));
        Assert.Equal(100, result.QueuePressurePercent, 3);
        Assert.Equal(100, result.PressurePercent, 3);
        Assert.Equal("QUEUE", result.Driver);
    }

    private static LogicalProcessorTelemetry Thread(int logical, int core, bool parked, double utility)
        => new(logical, core, parked, utility, 4000, 80);
}