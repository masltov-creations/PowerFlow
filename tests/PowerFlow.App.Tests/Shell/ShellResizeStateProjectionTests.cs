using PowerFlow.App.Dashboard;
using Xunit;

namespace PowerFlow.App.Tests.Shell;

public sealed class ShellResizeStateProjectionTests
{
    [Fact]
    public void ClampMinimum_StopsAtHoverTooltipSize()
        => Assert.Equal(new ShellLogicalSize(320, 219), ShellResizeStateProjection.ClampMinimum(new ShellLogicalSize(180, 90)));

    [Fact]
    public void Compact_ShrinksIntoHoverAndHoverRequiresEnoughRoomToReturnToCompact()
    {
        Assert.Equal(PowerFlowShellState.Glance, ShellResizeStateProjection.Resolve(new ShellLogicalSize(470, 440), PowerFlowShellState.Compact));
        Assert.Equal(PowerFlowShellState.Glance, ShellResizeStateProjection.Resolve(new ShellLogicalSize(519, 299), PowerFlowShellState.Glance));
        Assert.Equal(PowerFlowShellState.Compact, ShellResizeStateProjection.Resolve(new ShellLogicalSize(520, 300), PowerFlowShellState.Glance));
    }

    [Fact]
    public void CompactAndExpanded_UseExistingResponsiveHysteresis()
    {
        Assert.Equal(PowerFlowShellState.Expanded, ShellResizeStateProjection.Resolve(new ShellLogicalSize(900, 560), PowerFlowShellState.Compact));
        Assert.Equal(PowerFlowShellState.Compact, ShellResizeStateProjection.Resolve(new ShellLogicalSize(860, 520), PowerFlowShellState.Expanded));
    }
}