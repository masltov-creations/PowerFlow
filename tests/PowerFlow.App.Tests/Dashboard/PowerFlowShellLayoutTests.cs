using PowerFlow.App.Dashboard;
using Xunit;

namespace PowerFlow.App.Tests.Dashboard;

public sealed class PowerFlowShellLayoutTests
{
    [Fact]
    public void Resolve_Glance_IsMinimalAndNavigationFree()
    {
        var p = PowerFlowShellLayout.Resolve(320, 176, PowerFlowShellState.Glance, "flow");

        Assert.Equal(PowerFlowShellState.Glance, p.State);
        Assert.False(p.ShowNavigationRail);
        Assert.False(p.ShowModeCards);
        Assert.True(p.ShowGlanceTelemetry);
        Assert.False(p.ShowLiveStatsPanel);
        Assert.False(p.ShowLowerContextPanels);
        Assert.True(p.GraphHeight <= 70);
    }

    [Fact]
    public void Resolve_Compact_UsesWorkingDensityWithoutPermanentNavigationRail()
    {
        var p = PowerFlowShellLayout.Resolve(760, 440, PowerFlowShellState.Compact, "flow");

        Assert.Equal(PowerFlowShellState.Compact, p.State);
        Assert.False(p.ShowNavigationRail);
        Assert.True(p.ShowModeCards);
        Assert.False(p.ShowGlanceTelemetry);
        Assert.True(p.ShowCompactTelemetry);
        Assert.False(p.ShowLowerContextPanels);
    }

    [Fact]
    public void Resolve_Expanded_UsesReferenceCockpitDensity()
    {
        var p = PowerFlowShellLayout.Resolve(1280, 800, PowerFlowShellState.Expanded, "flow");

        Assert.True(p.ShowNavigationRail);
        Assert.True(p.ShowModeCards);
        Assert.True(p.ShowLiveStatsPanel);
        Assert.True(p.ShowLowerContextPanels);
        Assert.True(p.GraphHeight >= 360);
        Assert.True(p.NavigationWidth >= 120);
    }

    [Fact]
    public void Resolve_Expanded_RemainsFluidInsideSameState()
    {
        var small = PowerFlowShellLayout.Resolve(980, 620, PowerFlowShellState.Expanded, "flow");
        var large = PowerFlowShellLayout.Resolve(1320, 820, PowerFlowShellState.Expanded, "flow");

        Assert.Equal(PowerFlowShellState.Expanded, small.State);
        Assert.Equal(PowerFlowShellState.Expanded, large.State);
        Assert.True(large.GraphHeight > small.GraphHeight);
        Assert.True(large.PanelGap > small.PanelGap);
        Assert.True(large.ContentPadding > small.ContentPadding);
    }

    [Fact]
    public void Resolve_RulesAndSettingsAtCompactRequestExpandedDensity()
    {
        var rules = PowerFlowShellLayout.Resolve(760, 440, PowerFlowShellState.Compact, "rules");
        var settings = PowerFlowShellLayout.Resolve(760, 440, PowerFlowShellState.Compact, "settings");

        Assert.Equal(PowerFlowShellState.Expanded, rules.State);
        Assert.Equal(PowerFlowShellState.Expanded, settings.State);
        Assert.True(rules.ShowNavigationRail);
        Assert.True(settings.ShowNavigationRail);
    }
}