using Xunit;

namespace PowerFlow.App.Tests.Shell;

public sealed class CompactShellContractTests
{
    [Fact]
    public void Rules_UseAppPickerRatherThanForegroundCaptureButtons()
    {
        var root = FindRepoRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "PowerFlow.App", "Settings", "RulesPage.xaml"));
        var code = File.ReadAllText(Path.Combine(root, "src", "PowerFlow.App", "Settings", "RulesPage.xaml.cs"));

        Assert.DoesNotContain("Capture foreground", xaml, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Content=\"Add app\"", xaml, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("RunningAppCatalog", code, StringComparison.Ordinal);
        Assert.Contains("Browse", code, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Source=\"{Binding Icon}\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("<FontIcon", xaml, StringComparison.Ordinal);
        Assert.Contains("LoadAppIconAsync", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Settings_ExposeOnlyStartupAndAppearancePreferences()
    {
        var root = FindRepoRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "PowerFlow.App", "Settings", "SettingsPage.xaml"));
        var code = File.ReadAllText(Path.Combine(root, "src", "PowerFlow.App", "Settings", "SettingsPage.xaml.cs"));

        Assert.Contains("x:Name=\"ThemeBox\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ReducedMotionBox\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"StartupToggle\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("SaverPlanBox", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("PromotionThresholdBox", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("TelemetryIntervalBox", xaml, StringComparison.Ordinal);
        Assert.Contains("ThemePreference", code, StringComparison.Ordinal);
    }

    [Fact]
    public void App_PackagesPowerFlowIconAndThemeResources()
    {
        var root = FindRepoRoot();
        var project = File.ReadAllText(Path.Combine(root, "src", "PowerFlow.App", "PowerFlow.App.csproj"));
        var appXaml = File.ReadAllText(Path.Combine(root, "src", "PowerFlow.App", "App.xaml"));
        Assert.Contains("<ApplicationIcon>Assets\\PowerFlow.ico</ApplicationIcon>", project, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(root, "src", "PowerFlow.App", "Assets", "PowerFlow.ico")));
        Assert.Contains("ThemeDictionaries", appXaml, StringComparison.Ordinal);
        Assert.Contains("PowerFlowWindowBackgroundBrush", appXaml, StringComparison.Ordinal);
        var tray = File.ReadAllText(Path.Combine(root, "src", "PowerFlow.App", "Tray", "TrayIconHost.cs"));
        Assert.Contains("PowerFlow.ico", tray, StringComparison.Ordinal);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "PowerFlow.sln"))) return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("PowerFlow.sln not found from test base directory.");
    }
}


