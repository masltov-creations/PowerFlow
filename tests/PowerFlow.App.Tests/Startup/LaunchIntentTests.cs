using PowerFlow.App.Startup;
using Xunit;

namespace PowerFlow.App.Tests.Startup;

public sealed class LaunchIntentTests
{
    [Theory]
    [InlineData(new string[] { "PowerFlow.exe", "--dashboard" }, true)]
    [InlineData(new string[] { "PowerFlow.exe", "--background" }, false)]
    [InlineData(new string[] { "PowerFlow.exe", "--preview" }, true)]
    [InlineData(new string[] { "PowerFlow.exe", "--fullscreen" }, true)]
    [InlineData(new string[] { "PowerFlow.exe" }, false)]
    public void DashboardOpensOnlyWhenExplicitlyRequested(string[] args, bool expected)
        => Assert.Equal(expected, LaunchIntent.ShouldOpenDashboard(args));

    [Theory]
    [InlineData(new string[] { "PowerFlow.exe", "--preview" }, true)]
    [InlineData(new string[] { "PowerFlow.exe", "--dashboard" }, false)]
    [InlineData(new string[] { "PowerFlow.exe", "--background" }, false)]
    public void PreviewMode_IsExplicit(string[] args, bool expected)
        => Assert.Equal(expected, LaunchIntent.IsPreview(args));

    [Theory]
    [InlineData(new string[] { "PowerFlow.exe", "--preview" }, false, true)]
    [InlineData(new string[] { "PowerFlow.exe", "--dashboard" }, true, false)]
    [InlineData(new string[] { "PowerFlow.exe", "--background" }, true, false)]
    public void PreviewLifecycle_IsWindowOnly(string[] args, bool shouldCreateTray, bool shouldExitWithDashboard)
    {
        Assert.Equal(shouldCreateTray, LaunchIntent.ShouldCreateTray(args));
        Assert.Equal(shouldExitWithDashboard, LaunchIntent.ShouldExitWithDashboard(args));
    }

    [Theory]
    [InlineData(new string[] { "PowerFlow.exe", "--fullscreen" }, true)]
    [InlineData(new string[] { "PowerFlow.exe", "--dashboard" }, false)]
    [InlineData(new string[] { "PowerFlow.exe", "--background" }, false)]
    public void FullScreenMode_IsExplicit(string[] args, bool expected)
        => Assert.Equal(expected, LaunchIntent.ShouldOpenFullScreen(args));

    [Theory]
    [InlineData(new string[] { "PowerFlow.exe", "--popup-preview" }, true)]
    [InlineData(new string[] { "PowerFlow.exe", "--dashboard" }, false)]
    public void PopupPreviewMode_IsExplicit(string[] args, bool expected)
        => Assert.Equal(expected, LaunchIntent.ShouldOpenPopupPreview(args));
}