using PowerFlow.App.Dashboard;
using Xunit;

namespace PowerFlow.App.Tests.Dashboard;

public sealed class ShellPresentationProfileTests
{
    [Fact]
    public void SemanticProfile_DescribesPresentationInsteadOfVisibilityFlags()
    {
        var type = typeof(ShellPresentationProfile);
        var names = type.GetProperties().Select(x => x.Name).ToArray();

        Assert.Contains(nameof(ShellPresentationProfile.Navigation), names);
        Assert.Contains(nameof(ShellPresentationProfile.Header), names);
        Assert.Contains(nameof(ShellPresentationProfile.Modes), names);
        Assert.Contains(nameof(ShellPresentationProfile.Stats), names);
        Assert.Contains(nameof(ShellPresentationProfile.Trajectory), names);
        Assert.Contains(nameof(ShellPresentationProfile.ControlContext), names);
        Assert.Contains(nameof(ShellPresentationProfile.Secondary), names);
        Assert.Contains(nameof(ShellPresentationProfile.Geometry), names);

        Assert.DoesNotContain("ShowLiveStatsPanel", names);
        Assert.DoesNotContain("ShowLowerContextPanels", names);
        Assert.DoesNotContain("ShowModeCards", names);
    }

    [Fact]
    public void Geometry_CarriesReferenceHierarchyScalars()
    {
        var geometry = new ShellGeometry(144, 16, 12, 44, 68, .70, 112, 126);

        Assert.Equal(.70, geometry.PrimaryGraphFraction, 2);
        Assert.Equal(144, geometry.NavigationWidth);
        Assert.Equal(112, geometry.ControlBandHeight);
        Assert.Equal(126, geometry.SecondaryBandHeight);
    }
}
