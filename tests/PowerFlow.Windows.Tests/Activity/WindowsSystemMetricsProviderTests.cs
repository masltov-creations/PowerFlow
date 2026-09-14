using PowerFlow.Windows.Activity;
using Xunit;

namespace PowerFlow.Windows.Tests.Activity;

public sealed class WindowsSystemMetricsProviderTests
{
    [Fact]
    public void MemoryPercent_IsDerivedFromNativeMemoryStatus()
    {
        Assert.Equal(75d, WindowsSystemMetricsProvider.CalculateUsedPercent(1000, 250), 3);
    }

    [Fact]
    public void MemoryPercent_ClampsAvailableMemoryToPhysicalTotal()
    {
        Assert.Equal(0d, WindowsSystemMetricsProvider.CalculateUsedPercent(1000, 1200), 3);
    }

    [Fact]
    public void MemoryPercent_HandlesZeroTotalWithoutNaN()
    {
        Assert.Equal(0d, WindowsSystemMetricsProvider.CalculateUsedPercent(0, 0), 3);
    }
    [Fact]
    public void TotalProcessorCount_NormalizesNativeCount()
    {
        Assert.Equal(24, WindowsSystemMetricsProvider.NormalizeTotalProcessorCount(24));
        Assert.Null(WindowsSystemMetricsProvider.NormalizeTotalProcessorCount(0));
    }

    [Fact]
    public void AwakePhysicalCoreCount_CountsCoreOnceWhenAnySiblingIsUnparked()
    {
        var states = new[]
        {
            new CoreParkingState(0, IsParked: false),
            new CoreParkingState(0, IsParked: true),
            new CoreParkingState(1, IsParked: true),
            new CoreParkingState(1, IsParked: true),
            new CoreParkingState(2, IsParked: false),
            new CoreParkingState(2, IsParked: false)
        };

        Assert.Equal(2, WindowsSystemMetricsProvider.CountAwakePhysicalCores(states));
    }

    [Fact]
    public void DashboardTelemetrySource_ForwardsCoreCountsFromSystemProvider()
    {
        var now = DateTimeOffset.UtcNow;
        var logical = new[] { new LogicalProcessorTelemetry(0, 0, false, 22), new LogicalProcessorTelemetry(1, 0, true, 0) };
        using var source = new DashboardTelemetrySource(new FakeSystemMetricsProvider(new SystemMetricsSnapshot(42, "TEST-HOST", 7, 16, logical)));

        var telemetry = source.Read(now);

        Assert.Equal(7, telemetry.ActiveCores);
        Assert.Equal(16, telemetry.TotalCores);
        Assert.Equal(logical, telemetry.LogicalProcessors);
    }

    [Fact]
    public void DashboardTelemetrySource_PrefersAggregateTotalProcessorPerformance()
    {
        var logical = new[]
        {
            new LogicalProcessorTelemetry(0, 0, false, 20, ProcessorPerformancePercent: 95),
            new LogicalProcessorTelemetry(1, 0, false, 20, ProcessorPerformancePercent: 100)
        };
        using var source = new DashboardTelemetrySource(new FakeSystemMetricsProvider(
            new SystemMetricsSnapshot(42, "TEST-HOST", 1, 1, logical, AggregateProcessorPerformancePercent: 130)));

        var telemetry = source.Read(DateTimeOffset.UtcNow);

        Assert.Equal(130, telemetry.ProcessorPerformancePercent);
    }

    [Fact]
    public void AggregateSpeed_UsesNominalBaseTimesTotalProcessorPerformance()
    {
        Assert.Equal(4421.3, DashboardTelemetrySource.EstimateAggregateSpeedMhz(3401, 130)!.Value, 1);
        Assert.Equal(3401, DashboardTelemetrySource.EstimateAggregateSpeedMhz(3401, null));
    }
    private sealed class FakeSystemMetricsProvider(SystemMetricsSnapshot snapshot) : ISystemMetricsProvider
    {
        public SystemMetricsSnapshot Read() => snapshot;
    }
}
