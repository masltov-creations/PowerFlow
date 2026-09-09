using Xunit;

namespace PowerFlow.App.Tests.Shell;

public sealed class SingleShellOwnershipContractTests
{
    [Fact]
    public void App_OwnsExactlyOneVisualShellPath()
    {
        var root = RepoRoot();
        var app = File.ReadAllText(Path.Combine(root, "src", "PowerFlow.App", "App.xaml.cs"));
        Assert.Contains("MainWindow? _shellWindow", app, StringComparison.Ordinal);
        Assert.DoesNotContain("TrayHoverWindow", app, StringComparison.Ordinal);
        Assert.DoesNotContain("_dashboardWindow", app, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(root, "src", "PowerFlow.App", "Tray", "TrayHoverWindow.xaml")));
        Assert.False(File.Exists(Path.Combine(root, "src", "PowerFlow.App", "Tray", "TrayHoverWindow.xaml.cs")));
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "PowerFlow.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("PowerFlow repo root not found");
    }
}
