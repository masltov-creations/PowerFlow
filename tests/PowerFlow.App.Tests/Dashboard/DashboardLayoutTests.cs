using Xunit;

namespace PowerFlow.App.Tests.Dashboard;

public sealed class DashboardLayoutTests
{
    [Fact]
    public void DedicatedRulesView_UsesResponsiveImportanceCardsInsteadOfAFlatTable()
    {
        var xaml = File.ReadAllText(RepoFile("src", "PowerFlow.App", "Settings", "RulesPage.xaml"));
        Assert.Contains("x:Name=\"RulesRepeater\"", xaml);
        Assert.Contains("UniformGridLayout", xaml);
        Assert.Contains("LOW · EFFICIENCY ONLY", xaml);
        Assert.Contains("NORMAL · QUALIFIED BOOST", xaml);
        Assert.Contains("HIGH · FAST BOOST", xaml);
        Assert.Contains("Change importance", xaml);
        Assert.DoesNotContain("ServiceRepeater", xaml);
    }

    private static string RepoFile(params string[] parts)
    {
        var dir = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(dir, "PowerFlow.sln")))
            dir = Directory.GetParent(dir)?.FullName ?? throw new DirectoryNotFoundException("PowerFlow repo root not found");
        return Path.Combine(new[] { dir }.Concat(parts).ToArray());
    }
}