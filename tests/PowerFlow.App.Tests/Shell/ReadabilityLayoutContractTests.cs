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
    public void AdaptiveDashboard_DoesNotUseTinyExplicitFonts()
    {
        foreach (var file in new[]
        {
            RepoFile("src", "PowerFlow.App", "Dashboard", "MainWindow.xaml"),
            RepoFile("src", "PowerFlow.App", "Dashboard", "PerformanceTimelineControl.xaml"),
            RepoFile("src", "PowerFlow.App", "Dashboard", "ShellHeaderControl.xaml")
        })
        {
            var xaml = File.ReadAllText(file);
            foreach (var size in new[] { "8", "9", "10" })
                Assert.DoesNotContain($"FontSize=\"{size}\"", xaml, StringComparison.Ordinal);
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
    public void Timeline_ProgrammaticLabelsAreReadable()
    {
        var code = Read("src", "PowerFlow.App", "Dashboard", "PerformanceTimelineControl.xaml.cs");
        foreach (var size in new[] { "8", "9", "10" })
            Assert.DoesNotContain($"FontSize = {size}", code, StringComparison.Ordinal);
        foreach (var token in new[] { ", 8,", ", 9,", ", 10," })
            Assert.DoesNotContain(token, code, StringComparison.Ordinal);
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