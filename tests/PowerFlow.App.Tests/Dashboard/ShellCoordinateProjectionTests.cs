using PowerFlow.App.Dashboard;
using Xunit;

namespace PowerFlow.App.Tests.Dashboard;

public sealed class ShellCoordinateProjectionTests
{
    [Theory]
    [InlineData(950, 550, 1.25, 760, 440)]
    [InlineData(1600, 1000, 1.25, 1280, 800)]
    [InlineData(760, 440, 1.0, 760, 440)]
    public void PhysicalWindowSizeConvertsToLogicalDips(int width, int height, double scale, int expectedWidth, int expectedHeight)
    {
        var logical = ShellCoordinateProjection.ToLogicalSize(width, height, scale);
        Assert.Equal(expectedWidth, logical.Width);
        Assert.Equal(expectedHeight, logical.Height);
    }

    [Fact]
    public void InvalidScaleFallsBackToOne()
    {
        var logical = ShellCoordinateProjection.ToLogicalSize(760, 440, 0);
        Assert.Equal(760, logical.Width);
        Assert.Equal(440, logical.Height);
    }
}
