using Xunit;
using PowerFlow.App.Tray;

namespace PowerFlow.App.Tests.Tray;

public sealed class TrayHoverPolicyTests
{
    [Fact]
    public void HoverRequiresDelayAndHidesAfterLeavingIconAndPopup()
    {
        var t0 = new DateTimeOffset(2026, 9, 8, 18, 30, 0, TimeSpan.Zero);
        var sut = new TrayHoverPolicy(TimeSpan.FromMilliseconds(350));

        sut.BeginHover(t0);
        Assert.Equal(TrayHoverAction.None, sut.Evaluate(t0.AddMilliseconds(349), pointerOverIcon: true, pointerOverPopup: false));
        Assert.Equal(TrayHoverAction.Show, sut.Evaluate(t0.AddMilliseconds(350), pointerOverIcon: true, pointerOverPopup: false));
        Assert.Equal(TrayHoverAction.None, sut.Evaluate(t0.AddMilliseconds(500), pointerOverIcon: false, pointerOverPopup: true));
        Assert.Equal(TrayHoverAction.Hide, sut.Evaluate(t0.AddMilliseconds(650), pointerOverIcon: false, pointerOverPopup: false));
    }

    [Fact]
    public void LeavingBeforeDelayCancelsHoverWithoutShowing()
    {
        var t0 = DateTimeOffset.UtcNow;
        var sut = new TrayHoverPolicy(TimeSpan.FromMilliseconds(350));
        sut.BeginHover(t0);

        Assert.Equal(TrayHoverAction.Cancel, sut.Evaluate(t0.AddMilliseconds(100), pointerOverIcon: false, pointerOverPopup: false));
        Assert.False(sut.IsVisible);
    }

    [Fact]
    public void PlacementCentersAboveTrayIconAndClampsToWorkArea()
    {
        var result = TrayPopupPlacement.AboveIcon(
            new TrayRect(1880, 1040, 1904, 1064),
            new TrayRect(0, 0, 1920, 1080),
            width: 380,
            height: 236,
            gap: 10);

        Assert.InRange(result.Left, 0, 1540);
        Assert.Equal(794, result.Top);
    }

    [Theory]
    [InlineData(true, true, false)]
    [InlineData(true, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, true)]
    [InlineData(null, true, true)]
    [InlineData(null, false, false)]
    public void MotionPreference_RespectsOverrideAndWindowsSetting(bool? reducedMotionOverride, bool windowsAnimationsEnabled, bool expected)
    {
        Assert.Equal(expected, TrayMotionPreference.ShouldAnimate(reducedMotionOverride, windowsAnimationsEnabled));
    }}
