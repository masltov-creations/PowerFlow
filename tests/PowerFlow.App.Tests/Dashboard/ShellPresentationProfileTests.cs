using PowerFlow.App.Dashboard;
using Xunit;

namespace PowerFlow.App.Tests.Dashboard;

public sealed class ShellPresentationProfileTests
{
    [Fact]
    public void SemanticProfile_DescribesAdaptivePresentationInsteadOfRetiredCockpitLayers()
    {
        var names = typeof(ShellPresentationProfile).GetProperties().Select(x => x.Name).ToArray();

        Assert.Contains(nameof(ShellPresentationProfile.State), names);
        Assert.Contains(nameof(ShellPresentationProfile.Navigation), names);
        Assert.Contains(nameof(ShellPresentationProfile.Header), names);
        Assert.Contains(nameof(ShellPresentationProfile.Timeline), names);
        Assert.Contains(nameof(ShellPresentationProfile.GovernorControls), names);
        Assert.Contains(nameof(ShellPresentationProfile.Geometry), names);

        foreach (var retired in new[] { "Modes", "Stats", "Trajectory", "ControlContext", "Secondary", "ShowLiveStatsPanel", "ShowLowerContextPanels", "ShowModeCards" })
            Assert.DoesNotContain(retired, names);
    }

    [Fact]
    public void Geometry_CarriesOnlyCurrentShellHierarchyScalars()
    {
        var geometry = new ShellGeometry(144, 16, 12, 44, 112);

        Assert.Equal(144, geometry.NavigationWidth);
        Assert.Equal(16, geometry.ContentPadding);
        Assert.Equal(12, geometry.Gap);
        Assert.Equal(44, geometry.HeaderHeight);
        Assert.Equal(112, geometry.ControlBandHeight);
    }
}