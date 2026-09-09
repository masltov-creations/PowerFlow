using Xunit;

namespace PowerFlow.App.Tests.Shell;

public sealed class ShellLifecycleContractTests
{
    [Fact]
    public void App_RoutesAllVisualRequestsThroughOneShellWindow()
    {
        var app = Read("src", "PowerFlow.App", "App.xaml.cs");
        Assert.Contains("MainWindow? _shellWindow", app, StringComparison.Ordinal);
        Assert.DoesNotContain("_dashboardWindow", app, StringComparison.Ordinal);
        Assert.DoesNotContain("new TrayHoverWindow", app, StringComparison.Ordinal);
        Assert.Contains("InteractionRequested += OnTrayInteractionRequested", app, StringComparison.Ordinal);
        Assert.Contains("ShowShellAsync", app, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindow_IsTheMorphingShellAndMovesPositionAndSizeTogether()
    {
        var code = Read("src", "PowerFlow.App", "Dashboard", "MainWindow.xaml.cs");
        var xaml = Read("src", "PowerFlow.App", "Dashboard", "MainWindow.xaml");

        Assert.Contains("ShowShellAsync", code, StringComparison.Ordinal);
        Assert.Contains("TransitionToAsync", code, StringComparison.Ordinal);
        Assert.Contains("ShellActivationMode", code, StringComparison.Ordinal);
        Assert.Contains("AppWindow.MoveAndResize", code, StringComparison.Ordinal);
        Assert.Contains("WS_EX_NOACTIVATE", code, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ShellRoot\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"GlanceTapTarget\"", xaml, StringComparison.Ordinal);
    }

    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine(new[] { RepoRoot() }.Concat(parts).ToArray()));

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "PowerFlow.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException();
    }
}