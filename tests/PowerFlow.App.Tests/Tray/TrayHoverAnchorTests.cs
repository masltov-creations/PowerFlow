using PowerFlow.App.Tray;
using Xunit;

namespace PowerFlow.App.Tests.Tray;

public sealed class TrayHoverAnchorTests
{
    [Fact]
    public void Resolve_UsesShellRectWhenPointerIsInsideActualVisibleIcon()
    {
        var shell = new TrayRect(100, 100, 140, 140);
        var observed = new TrayRect(300, 300, 344, 344);

        var result = TrayHoverAnchorProjection.Resolve(shell, observed, 120, 120);

        Assert.Equal(shell, result);
    }

    [Fact]
    public void Resolve_FallsBackToObservedCallbackRectWhenShellReturnsOverflowChevron()
    {
        var chevron = new TrayRect(3124, 1380, 3164, 1440);
        var observedHiddenIcon = new TrayRect(2920, 1280, 2964, 1324);

        var result = TrayHoverAnchorProjection.Resolve(chevron, observedHiddenIcon, 2942, 1302);

        Assert.Equal(observedHiddenIcon, result);
    }

    [Fact]
    public void Resolve_ReturnsNullAfterPointerLeavesBothRects()
    {
        var shell = new TrayRect(3124, 1380, 3164, 1440);
        var observed = new TrayRect(2920, 1280, 2964, 1324);

        Assert.Null(TrayHoverAnchorProjection.Resolve(shell, observed, 2800, 1200));
    }

    [Fact]
    public void AroundPoint_CreatesStableOverflowIconHitTarget()
    {
        Assert.Equal(new TrayRect(2980, 1180, 3020, 1220), TrayHoverAnchorProjection.AroundPoint(3000, 1200, 40));
    }
}