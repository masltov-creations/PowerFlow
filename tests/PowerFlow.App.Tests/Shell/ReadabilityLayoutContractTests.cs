using Xunit;

namespace PowerFlow.App.Tests.Shell;

public sealed class ReadabilityLayoutContractTests
{
    [Fact]
    public void Shell_PreservesReadableCanonicalCompactSize()
    {
        var geometry = Read("src", "PowerFlow.App", "Dashboard", "ShellTransitionGeometry.cs");
        var code = Read("src", "PowerFlow.App", "Dashboard", "MainWindow.xaml.cs");
        Assert.Contains("PowerFlowShellState.Compact", geometry, StringComparison.Ordinal);
        Assert.Contains("(760, 440)", geometry, StringComparison.Ordinal);
        Assert.Contains("PowerFlowShellState.Glance", code, StringComparison.Ordinal);
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
        foreach (var size in new[] { "8", "9", "10" })
        {
            Assert.DoesNotContain($"FontSize=\"{size}\"", rules);
            Assert.DoesNotContain($"FontSize=\"{size}\"", settings);
        }
    }

    [Fact]
    public void TelemetryGraph_ProgrammaticLabelsAreReadable()
    {
        var code = Read("src", "PowerFlow.App", "Dashboard", "TelemetryGraphControl.xaml.cs");
        Assert.DoesNotContain("FontSize = 8", code);
        Assert.DoesNotContain("FontSize = 9", code);
        Assert.DoesNotContain("FontSize = 10", code);
        Assert.DoesNotContain(", 8,", code);
        Assert.DoesNotContain(", 9,", code);
        Assert.DoesNotContain(", 10,", code);
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