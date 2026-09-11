using PowerFlow.App.Telemetry;
using Xunit;

namespace PowerFlow.App.Tests.Telemetry;

public sealed class TelemetryCadencePolicyTests
{
    [Theory]
    [InlineData(false, false, null, TelemetryCadenceMode.HiddenAuto)]
    [InlineData(true, false, null, TelemetryCadenceMode.Visible)]
    [InlineData(true, true, "Game", TelemetryCadenceMode.Visible)]
    [InlineData(true, true, "Manual", TelemetryCadenceMode.Visible)]
    [InlineData(false, true, "Game", TelemetryCadenceMode.HiddenAuto)]
    [InlineData(false, true, "Manual", TelemetryCadenceMode.HiddenAuto)]
    public void SelectMode_UsesVisibilityAndLatchSemantics(bool anySurfaceVisible, bool isLatched, string? latchType, TelemetryCadenceMode expected)
    {
        Assert.Equal(expected, TelemetryCadencePolicy.SelectMode(anySurfaceVisible, isLatched, latchType));
    }

    [Fact]
    public void Interval_IsOneSecondVisibleAndFiveSecondsHiddenAuto()
    {
        Assert.Equal(TimeSpan.FromSeconds(1), TelemetryCadencePolicy.IntervalFor(TelemetryCadenceMode.Visible));
        Assert.Equal(TimeSpan.FromSeconds(5), TelemetryCadencePolicy.IntervalFor(TelemetryCadenceMode.HiddenAuto));
        Assert.Null(TelemetryCadencePolicy.IntervalFor(TelemetryCadenceMode.Off));
    }
    [Fact]
    public void Interval_AllowsGranularConfiguredRatesWithinSafeBounds()
    {
        Assert.Equal(TimeSpan.FromMilliseconds(375), TelemetryCadencePolicy.IntervalFor(TelemetryCadenceMode.Visible, TimeSpan.FromMilliseconds(375), TimeSpan.FromMilliseconds(1750)));
        Assert.Equal(TimeSpan.FromMilliseconds(1750), TelemetryCadencePolicy.IntervalFor(TelemetryCadenceMode.HiddenAuto, TimeSpan.FromMilliseconds(375), TimeSpan.FromMilliseconds(1750)));
        Assert.Equal(TimeSpan.FromMilliseconds(250), TelemetryCadencePolicy.NormalizeVisible(TimeSpan.FromMilliseconds(10)));
        Assert.Equal(TimeSpan.FromSeconds(10), TelemetryCadencePolicy.NormalizeHidden(TimeSpan.FromMinutes(1)));
    }
}
