using PowerFlow.App.Dashboard;
using Xunit;

namespace PowerFlow.App.Tests.Dashboard;

public sealed class DashboardClosePolicyTests
{
    [Fact]
    public void RealUserClose_HidesToTray()
    {
        Assert.Equal(DashboardCloseDisposition.HideToTray, DashboardClosePolicy.Decide(previewMode: false, explicitShutdown: false));
    }

    [Fact]
    public void PreviewClose_ActuallyCloses()
    {
        Assert.Equal(DashboardCloseDisposition.Close, DashboardClosePolicy.Decide(previewMode: true, explicitShutdown: false));
    }

    [Fact]
    public void ExplicitShutdown_ActuallyCloses()
    {
        Assert.Equal(DashboardCloseDisposition.Close, DashboardClosePolicy.Decide(previewMode: false, explicitShutdown: true));
    }

    [Fact]
    public void MainWindow_InterceptsRealCloseAndReleasesVisibleTelemetryLease()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot(), "src", "PowerFlow.App", "Dashboard", "MainWindow.xaml.cs"));
        Assert.Contains("AppWindow.Closing += OnAppWindowClosing", source);
        Assert.Contains("args.Cancel = true", source);
        Assert.Contains("AppWindow.Hide()", source);
        Assert.Contains("ReleaseDashboardVisibility", source);
        Assert.Contains("AcquireVisibility", source);
        Assert.Contains("CloseForShutdown", source);
        Assert.DoesNotContain("new DashboardTelemetrySession", source);
    }

    [Fact]
    public void App_UsesExplicitShutdownClosePath()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot(), "src", "PowerFlow.App", "App.xaml.cs"));
        Assert.Contains("CloseForShutdown", source);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "PowerFlow.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repo root not found");
    }
}