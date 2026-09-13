using PowerFlow.App.Telemetry;
using PowerFlow.Windows.Activity;
using Xunit;

namespace PowerFlow.App.Tests.Telemetry;

public sealed class DemandPressureTelemetryTests
{
    [Fact]
    public void Project_UsesAvailableCapacitySaturationWhenFewSlowProcessorsCarryTheWork()
    {
        var telemetry = new DashboardTelemetry(null, null, DateTimeOffset.UtcNow,
            LogicalProcessors: new[]
            {
                Thread(0, 0, false, 80, 80), Thread(1, 0, false, 0, 80),
                Thread(2, 1, false, 20, 80), Thread(3, 1, false, 0, 80),
                Thread(4, 2, true, 0, 80), Thread(5, 2, true, 0, 80),
                Thread(6, 3, true, 0, 80), Thread(7, 3, true, 0, 80)
            }, ProcessorQueueLength: 0);

        var result = Assert.IsType<DemandPressureTelemetry>(DemandPressureModel.Project(telemetry));
        Assert.Equal(12.5, result.DemandPercent, 3);
        Assert.Equal(40, result.AvailableCapacityPercent, 3);
        Assert.Equal(31.25, result.CapacitySaturationPercent, 3);
        Assert.Equal(31.25, result.PressurePercent, 3);
        Assert.Equal("SATURATION", result.Driver);
        Assert.Equal(2, result.AwakeCores);
    }

    [Fact]
    public void Project_SameDemandProducesHigherPressureWhenCapacityIsReduced()
    {
        var full = new DashboardTelemetry(null, null, DateTimeOffset.UtcNow,
            LogicalProcessors: Enumerable.Range(0, 8).Select(i => Thread(i, i / 2, false, 10, 100)).ToArray(),
            ProcessorQueueLength: 0);
        var constrained = new DashboardTelemetry(null, null, DateTimeOffset.UtcNow,
            LogicalProcessors: new[]
            {
                Thread(0, 0, false, 20, 50), Thread(1, 0, false, 20, 50),
                Thread(2, 1, false, 20, 50), Thread(3, 1, false, 20, 50),
                Thread(4, 2, true, 0, 50), Thread(5, 2, true, 0, 50),
                Thread(6, 3, true, 0, 50), Thread(7, 3, true, 0, 50)
            }, ProcessorQueueLength: 0);

        var fullResult = Assert.IsType<DemandPressureTelemetry>(DemandPressureModel.Project(full));
        var constrainedResult = Assert.IsType<DemandPressureTelemetry>(DemandPressureModel.Project(constrained));
        Assert.Equal(fullResult.DemandPercent, constrainedResult.DemandPercent, 3);
        Assert.Equal(100, fullResult.AvailableCapacityPercent, 3);
        Assert.Equal(25, constrainedResult.AvailableCapacityPercent, 3);
        Assert.Equal(10, fullResult.PressurePercent, 3);
        Assert.Equal(40, constrainedResult.PressurePercent, 3);
    }

    [Fact]
    public void Project_QueueContentionCanBecomeDominantPressure()
    {
        var telemetry = new DashboardTelemetry(null, null, DateTimeOffset.UtcNow,
            LogicalProcessors: new[]
            {
                Thread(0, 0, false, 10, 80), Thread(1, 0, false, 0, 80),
                Thread(2, 1, false, 10, 80), Thread(3, 1, false, 0, 80),
                Thread(4, 2, true, 0, 80), Thread(5, 2, true, 0, 80),
                Thread(6, 3, true, 0, 80), Thread(7, 3, true, 0, 80)
            }, ProcessorQueueLength: 2);

        var result = Assert.IsType<DemandPressureTelemetry>(DemandPressureModel.Project(telemetry));
        Assert.Equal(100, result.QueuePressurePercent, 3);
        Assert.Equal(100, result.PressurePercent, 3);
        Assert.Equal("QUEUE", result.Driver);
    }

    [Fact]
    public void Project_UsesProcessorUtilityForDemandWhenBusyTimeDiffers()
    {
        var telemetry = new DashboardTelemetry(null, null, DateTimeOffset.UtcNow,
            LogicalProcessors: new[]
            {
                new LogicalProcessorTelemetry(0, 0, false, UtilizationPercent: 25, FrequencyMhz: 4000, PercentOfMaximumFrequency: 100, ProcessorPerformancePercent: 130, ProcessorUtilityPercent: 65),
                new LogicalProcessorTelemetry(1, 0, false, UtilizationPercent: 25, FrequencyMhz: 4000, PercentOfMaximumFrequency: 100, ProcessorPerformancePercent: 130, ProcessorUtilityPercent: 65)
            }, ProcessorQueueLength: 0);

        var result = Assert.IsType<DemandPressureTelemetry>(DemandPressureModel.Project(telemetry));
        Assert.Equal(65, result.DemandPercent, 3);
        Assert.Equal(130, result.AvailableCapacityPercent, 3);
    }
    private static LogicalProcessorTelemetry Thread(int logical, int core, bool parked, double utility, double frequencyPercent)
        => new(logical, core, parked, utility, 4000, frequencyPercent);
}