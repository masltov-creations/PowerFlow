using Xunit;

namespace PowerFlow.App.Tests.Shell;

public sealed class ReadabilityLayoutContractTests
{
    [Fact]
    public void MainWindow_DefaultsToReadableCompactSize()
    {
        var code = Read("src", "PowerFlow.App", "Dashboard", "MainWindow.xaml.cs");
        Assert.Contains("SizeInt32(CompressedWidth, CompressedHeight)", code);
        Assert.DoesNotContain("SizeInt32(900, 560)", code);
    }

    [Fact]
    public void Dashboard_DoesNotUseTinyExplicitFonts()
    {
        foreach (var file in new[]
        {
            RepoFile("src", "PowerFlow.App", "Dashboard", "MainWindow.xaml"),
            RepoFile("src", "PowerFlow.App", "Dashboard", "RuleFlowControl.xaml"),
            RepoFile("src", "PowerFlow.App", "Dashboard", "DecisionPressureControl.xaml"),
            RepoFile("src", "PowerFlow.App", "Dashboard", "TelemetryGraphControl.xaml")
        })
        {
            var xaml = File.ReadAllText(file);
            Assert.DoesNotContain("FontSize=\"8\"", xaml);
            Assert.DoesNotContain("FontSize=\"9\"", xaml);
            Assert.DoesNotContain("FontSize=\"10\"", xaml);
        }
    }

    [Fact]
    public void SettingsAndRules_ReflowOrScrollInsteadOfShrinkingTypography()
    {
        var rules = Read("src", "PowerFlow.App", "Settings", "RulesPage.xaml");
        var settings = Read("src", "PowerFlow.App", "Settings", "SettingsPage.xaml");
        Assert.Contains("ScrollViewer", rules);
        Assert.Contains("ScrollViewer", settings);
        Assert.DoesNotContain("FontSize=\"8\"", rules);
        Assert.DoesNotContain("FontSize=\"9\"", rules);
        Assert.DoesNotContain("FontSize=\"10\"", rules);
        Assert.DoesNotContain("FontSize=\"8\"", settings);
        Assert.DoesNotContain("FontSize=\"9\"", settings);
        Assert.DoesNotContain("FontSize=\"10\"", settings);
    }

    [Fact]
    public void TelemetryGraph_ProgrammaticLabelsAreReadable()
    {
        var code = Read("src", "PowerFlow.App", "Dashboard", "TelemetryGraphControl.xaml.cs");
        Assert.DoesNotContain("FontSize = 9", code);
        Assert.DoesNotContain(", 9, labelBrush", code);
    }
    private static string Read(params string[] parts) => File.ReadAllText(RepoFile(parts));
    private static string RepoFile(params string[] parts)
    {
        var dir = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(dir, "PowerFlow.sln")))
            dir = Directory.GetParent(dir)?.FullName ?? throw new DirectoryNotFoundException("PowerFlow repo root not found");
        return Path.Combine(new[] { dir }.Concat(parts).ToArray());
    }
}


