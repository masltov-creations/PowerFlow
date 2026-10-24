using PowerFlow.App.Dashboard.HarmonicRing;
using Xunit;

namespace PowerFlow.App.Tests.Dashboard;

public sealed class HarmonicRingLayoutTests
{
    [Fact]
    public void ResolveDisclosure_RequiresBothDimensionsToEnterCard()
    {
        Assert.Equal(HarmonicRingDisclosure.Circle, HarmonicRingLayout.ResolveDisclosure(600, 350, HarmonicRingDisclosure.Circle));
        Assert.Equal(HarmonicRingDisclosure.Circle, HarmonicRingLayout.ResolveDisclosure(550, 380, HarmonicRingDisclosure.Circle));
        Assert.Equal(HarmonicRingDisclosure.Card, HarmonicRingLayout.ResolveDisclosure(560, 360, HarmonicRingDisclosure.Circle));
    }

    [Fact]
    public void ResolveDisclosure_KeepsCardWithinItsHysteresisBand()
    {
        Assert.Equal(HarmonicRingDisclosure.Card, HarmonicRingLayout.ResolveDisclosure(540, 340, HarmonicRingDisclosure.Card));
        Assert.Equal(HarmonicRingDisclosure.Circle, HarmonicRingLayout.ResolveDisclosure(519, 340, HarmonicRingDisclosure.Card));
        Assert.Equal(HarmonicRingDisclosure.Circle, HarmonicRingLayout.ResolveDisclosure(540, 319, HarmonicRingDisclosure.Card));
    }

    [Fact]
    public void ResolveDisclosure_RequiresBothDimensionsToEnterPanel()
    {
        Assert.Equal(HarmonicRingDisclosure.Card, HarmonicRingLayout.ResolveDisclosure(1000, 550, HarmonicRingDisclosure.Card));
        Assert.Equal(HarmonicRingDisclosure.Panel, HarmonicRingLayout.ResolveDisclosure(900, 560, HarmonicRingDisclosure.Card));
        Assert.Equal(HarmonicRingDisclosure.Panel, HarmonicRingLayout.ResolveDisclosure(1000, 600, HarmonicRingDisclosure.Circle));
    }

    [Fact]
    public void ResolveDisclosure_KeepsPanelWithinItsHysteresisBand()
    {
        Assert.Equal(HarmonicRingDisclosure.Panel, HarmonicRingLayout.ResolveDisclosure(880, 540, HarmonicRingDisclosure.Panel));
        Assert.Equal(HarmonicRingDisclosure.Card, HarmonicRingLayout.ResolveDisclosure(859, 540, HarmonicRingDisclosure.Panel));
        Assert.Equal(HarmonicRingDisclosure.Card, HarmonicRingLayout.ResolveDisclosure(880, 519, HarmonicRingDisclosure.Panel));
    }

    [Fact]
    public void ResolveDisclosure_InvalidSizesPreserveCurrentDisclosure()
    {
        Assert.Equal(HarmonicRingDisclosure.Card, HarmonicRingLayout.ResolveDisclosure(double.NaN, 800, HarmonicRingDisclosure.Card));
        Assert.Equal(HarmonicRingDisclosure.Panel, HarmonicRingLayout.ResolveDisclosure(-1, 800, HarmonicRingDisclosure.Panel));
    }
}
