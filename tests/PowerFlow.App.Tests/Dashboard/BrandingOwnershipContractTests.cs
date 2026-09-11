using Xunit;

namespace PowerFlow.App.Tests.Dashboard;

public sealed class BrandingOwnershipContractTests
{
    [Fact]
    public void Branding_HasOneOwnerAndUsesPackagedLogoUri()
    {
        var root = FindRoot();
        var header = File.ReadAllText(Path.Combine(root, "src", "PowerFlow.App", "Dashboard", "ShellHeaderControl.xaml"));
        var headerCode = File.ReadAllText(Path.Combine(root, "src", "PowerFlow.App", "Dashboard", "ShellHeaderControl.xaml.cs"));
        var main = File.ReadAllText(Path.Combine(root, "src", "PowerFlow.App", "Dashboard", "MainWindow.xaml.cs"));
        Assert.Contains("ms-appx:///Assets/PowerFlow.png", header, StringComparison.Ordinal);
        Assert.DoesNotContain("Source=\"Assets/PowerFlow.png\"", header, StringComparison.Ordinal);
        Assert.Contains("ShowBrandProperty", headerCode, StringComparison.Ordinal);
        Assert.Contains("SystemHeaderHost.ShowBrand = profile.Navigation != NavigationPresentation.Rail", main, StringComparison.Ordinal);
    }

    private static string FindRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(dir, "PowerFlow.sln"))) dir = Directory.GetParent(dir)?.FullName ?? throw new DirectoryNotFoundException();
        return dir;
    }
}