using Xunit;

namespace PowerFlow.App.Tests.Packaging;

public sealed class WinUiResourceSafetyTests
{
    [Fact]
    public void AppXaml_LoadsWinUiControlThemeResources()
    {
        var root = FindRepoRoot();
        var text = File.ReadAllText(Path.Combine(root, "src", "PowerFlow.App", "App.xaml"));
        Assert.Contains("XamlControlsResources", text, StringComparison.Ordinal);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "PowerFlow.sln"))) return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("PowerFlow repo root not found.");
    }
}