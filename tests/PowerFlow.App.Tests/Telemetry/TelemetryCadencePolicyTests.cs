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
    [InlineData(false, true, "Game", TelemetryCadenceMode.Off)]
    [InlineData(false, true, "Manual", TelemetryCadenceMode.Off)]
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
}
