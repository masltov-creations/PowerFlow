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
        using var source = new DashboardTelemetrySource(new FakeSystemMetricsProvider(new SystemMetricsSnapshot(42, "reference-host", 7, 16)));

        var telemetry = source.Read(now);

        Assert.Equal(7, telemetry.ActiveCores);
        Assert.Equal(16, telemetry.TotalCores);
    }

    private sealed class FakeSystemMetricsProvider(SystemMetricsSnapshot snapshot) : ISystemMetricsProvider
    {
        public SystemMetricsSnapshot Read() => snapshot;
    }
}
