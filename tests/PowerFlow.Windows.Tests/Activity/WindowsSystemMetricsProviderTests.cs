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
}
