using PowerFlow.App.Dashboard;
using Xunit;

namespace PowerFlow.App.Tests.Dashboard;

public sealed class DashboardResponsiveLayoutTests
{
    [Fact]
    public void Resolve_ProgressivelyDisclosesWithoutShrinkingTextIntoDust()
    {
        var compressed = DashboardResponsiveLayout.Resolve(760, 440, false);
        var expanded = DashboardResponsiveLayout.Resolve(1120, 720, false);
        var full = DashboardResponsiveLayout.Resolve(1920, 1080, true);

        Assert.Equal(DashboardPresentationMode.Compressed, compressed.Mode);
        Assert.True(compressed.ShowCompactTelemetry);
        Assert.False(compressed.ShowTelemetryCard);
        Assert.False(compressed.ShowContextRail);
        Assert.True(compressed.StateFontSize >= 18);

        Assert.Equal(DashboardPresentationMode.Expanded, expanded.Mode);
        Assert.False(expanded.ShowCompactTelemetry);
        Assert.True(expanded.ShowTelemetryCard);
        Assert.True(expanded.ShowContextRail);
        Assert.False(expanded.ShowFullContext);

        Assert.Equal(DashboardPresentationMode.FullScreen, full.Mode);
        Assert.True(full.ShowTelemetryCard);
        Assert.True(full.ShowContextRail);
        Assert.True(full.ShowFullContext);
        Assert.True(full.StateFontSize > expanded.StateFontSize);
        Assert.True(full.GraphMinHeight > expanded.GraphMinHeight);
    }

    [Fact]
    public void Resolve_IsFluidWithinExpandedMode_NotJustBreakpointSwitching()
    {
        var small = DashboardResponsiveLayout.Resolve(980, 620, false);
        var large = DashboardResponsiveLayout.Resolve(1320, 820, false);

        Assert.Equal(DashboardPresentationMode.Expanded, small.Mode);
        Assert.Equal(DashboardPresentationMode.Expanded, large.Mode);
        Assert.True(large.GraphMinHeight > small.GraphMinHeight);
        Assert.True(large.ReasonMaxWidth > small.ReasonMaxWidth);
        Assert.True(large.PanelHorizontalPadding > small.PanelHorizontalPadding);
    }

    [Fact]
    public void Resolve_LargeManualWindowUsesFullDensityWithoutForcingFullscreenPresenter()
    {
        var large = DashboardResponsiveLayout.Resolve(1600, 900, false);
        Assert.Equal(DashboardPresentationMode.FullScreen, large.Mode);
        Assert.True(large.ShowFullContext);
    }
}