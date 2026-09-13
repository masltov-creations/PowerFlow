using System.Globalization;
using System.Text.RegularExpressions;
using PowerFlow.App.Dashboard;
using Xunit;

namespace PowerFlow.App.Tests.Dashboard;

public sealed class VisualCoherenceContractTests
{
    [Theory]
    [InlineData("flow", PowerFlowShellState.Compact)]
    [InlineData("rules", PowerFlowShellState.Expanded)]
    [InlineData("profile", PowerFlowShellState.Expanded)]
    [InlineData("settings", PowerFlowShellState.Expanded)]
    public void SectionPolicy_DefinesMinimumUsableShell(string section, PowerFlowShellState expected)
    {
        var type = typeof(PowerFlowShellLayout).Assembly.GetType("PowerFlow.App.Dashboard.ShellSectionPolicy");
        Assert.NotNull(type);
        var method = type!.GetMethod("MinimumState", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
        Assert.NotNull(method);
        Assert.Equal(expected, method!.Invoke(null, [section]));
    }

    [Fact]
    public void SectionNavigation_ChangesContentWithoutChangingShellPresentation()
    {
        var code = Read("src", "PowerFlow.App", "Dashboard", "MainWindow.xaml.cs");
        var start = code.IndexOf("private Task NavigateToSectionAsync", StringComparison.Ordinal);
        var end = code.IndexOf("private void ApplySectionVisibility", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        var navigation = code[start..end];
        Assert.DoesNotContain("TransitionToAsync", navigation, StringComparison.Ordinal);
        Assert.DoesNotContain("ShellSectionPolicy.MinimumState", navigation, StringComparison.Ordinal);
        Assert.Contains("ApplyShellLayout(_shellState", navigation, StringComparison.Ordinal);
        Assert.Contains("Task.CompletedTask", navigation, StringComparison.Ordinal);
    }
    [Fact]
    public void GrowthMotion_HasSoftStartAndSoftLanding()
    {
        var early = ShellMotionPolicy.Ease(PowerFlowShellState.Compact, PowerFlowShellState.Expanded, .01);
        var late = ShellMotionPolicy.Ease(PowerFlowShellState.Compact, PowerFlowShellState.Expanded, .99);
        Assert.InRange(early, 0, .005);
        Assert.InRange(1 - late, 0, .005);
        Assert.InRange(ShellMotionPolicy.Duration(PowerFlowShellState.Compact, PowerFlowShellState.Expanded, false).TotalMilliseconds, 280, 420);
    }

    [Fact]
    public void Timeline_ProvidesContinuousPresentationMorph()
    {
        var method = typeof(PerformanceTimelineControl).GetMethod("ApplyMorph");
        Assert.NotNull(method);
    }

    [Fact]
    public void ShellGeometry_ProvidesContinuousInterpolation()
    {
        var method = typeof(ShellTransitionGeometry).GetMethod("InterpolateGeometry");
        Assert.NotNull(method);
    }

    [Fact]
    public void FullScreenExit_WaitsForPresenterLayoutBeforeBoundsAnimation()
    {
        var code = Read("src", "PowerFlow.App", "Dashboard", "MainWindow.xaml.cs");
        Assert.Contains("WaitForPresenterLayoutAsync", code, StringComparison.Ordinal);
        Assert.Contains("ShellPresenterTransitionPolicy", code, StringComparison.Ordinal);
    }

    [Fact]
    public void CurrentSurfaces_UseSemanticMutedTextBrush()
    {
        foreach (var parts in new[]
        {
            new[] { "src", "PowerFlow.App", "Dashboard", "MainWindow.xaml" },
            new[] { "src", "PowerFlow.App", "Dashboard", "PerformanceTimelineControl.xaml" },
            new[] { "src", "PowerFlow.App", "Dashboard", "CpuCapabilityProfileControl.xaml" },
            new[] { "src", "PowerFlow.App", "Settings", "RulesPage.xaml" }
        })
            Assert.Contains("PowerFlowTextMutedBrush", Read(parts), StringComparison.Ordinal);
    }

    [Fact]
    public void LightAndDarkSemanticTextBrushesMeetNormalTextContrast()
    {
        var xaml = Read("src", "PowerFlow.App", "App.xaml");
        foreach (var theme in new[] { "Dark", "Light" })
        {
            var block = ThemeBlock(xaml, theme);
            var background = OpaqueComposite(Color(block, "PowerFlowSurfaceBrush"), "FFFFFF");
            foreach (var key in new[] { "PowerFlowTextSecondaryBrush", "PowerFlowTextMutedBrush" })
            {
                var foreground = OpaqueComposite(Color(block, key), background);
                Assert.True(Contrast(foreground, background) >= 4.5,
                    $"{theme} {key} contrast was {Contrast(foreground, background):0.00}:1");
            }
        }
    }


    [Fact]
    public void BaselineSurface_DescribesExactlySevenFixedLegsAndThirtyFiveMinutes()
    {
        var xaml = Read("src", "PowerFlow.App", "Dashboard", "CpuCapabilityProfileControl.xaml");
        var code = Read("src", "PowerFlow.App", "Dashboard", "CpuCapabilityProfileControl.xaml.cs");
        Assert.DoesNotContain("ULTRA and Auto", xaml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("/ ULTRA / AUTO", xaml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("/ ULTRA / AUTO", code, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("35 minutes", xaml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("40 minutes", xaml, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CurrentSurfaces_DoNotDimSmallTextWithRawOpacity()
    {
        foreach (var parts in new[]
        {
            new[] { "src", "PowerFlow.App", "Dashboard", "MainWindow.xaml" },
            new[] { "src", "PowerFlow.App", "Dashboard", "PerformanceTimelineControl.xaml" },
            new[] { "src", "PowerFlow.App", "Dashboard", "CpuCapabilityProfileControl.xaml" },
            new[] { "src", "PowerFlow.App", "Dashboard", "MachineBaselineComparisonChartControl.xaml" },
            new[] { "src", "PowerFlow.App", "Settings", "RulesPage.xaml" },
            new[] { "src", "PowerFlow.App", "Settings", "SettingsPage.xaml" }
        })
        {
            var xaml = Read(parts);
            Assert.DoesNotMatch(new Regex("<TextBlock[^>]*Opacity=\\\"0\\.[0-6][0-9]*\\\"", RegexOptions.IgnoreCase), xaml);
        }
    }

    [Fact]
    public void ShellActionControls_UseThirtyFourPixelLogicalFloorToSurviveResizeRounding()
    {
        var main = Read("src", "PowerFlow.App", "Dashboard", "MainWindow.xaml");
        var header = Read("src", "PowerFlow.App", "Dashboard", "ShellHeaderControl.xaml");
        Assert.Contains("x:Name=\"CompactButton\"", main, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"PresentationToggleButton\"", main, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"CompactNavigationButton\"", header, StringComparison.Ordinal);
        Assert.Matches(new Regex("x:Name=\\\"CompactButton\\\"[^>]*MinHeight=\\\"34\\\""), main);
        Assert.Matches(new Regex("x:Name=\\\"PresentationToggleButton\\\"[^>]*MinHeight=\\\"34\\\""), main);
        Assert.Matches(new Regex("x:Name=\\\"CompactNavigationButton\\\"[^>]*MinHeight=\\\"34\\\""), header);
    }
    private static string ThemeBlock(string xaml, string theme)
    {
        var start = xaml.IndexOf($"<ResourceDictionary x:Key=\"{theme}\">", StringComparison.Ordinal);
        Assert.True(start >= 0, $"Missing {theme} theme dictionary");
        var end = xaml.IndexOf("</ResourceDictionary>", start, StringComparison.Ordinal);
        Assert.True(end > start);
        return xaml[start..end];
    }

    private static string Color(string block, string key)
    {
        var match = Regex.Match(block, $"x:Key=\"{Regex.Escape(key)}\" Color=\"#(?<hex>[0-9A-Fa-f]{{8}})\"");
        Assert.True(match.Success, $"Missing {key}");
        return match.Groups["hex"].Value.ToUpperInvariant();
    }

    private static string OpaqueComposite(string argb, string backgroundRgb)
    {
        var a = int.Parse(argb[..2], NumberStyles.HexNumber) / 255d;
        var fg = new[] { argb[2..4], argb[4..6], argb[6..8] }.Select(v => int.Parse(v, NumberStyles.HexNumber)).ToArray();
        var bg = new[] { backgroundRgb[..2], backgroundRgb[2..4], backgroundRgb[4..6] }.Select(v => int.Parse(v, NumberStyles.HexNumber)).ToArray();
        return string.Concat(fg.Zip(bg, (f, b) => (int)Math.Round(a * f + (1 - a) * b)).Select(v => v.ToString("X2")));
    }

    private static double Contrast(string a, string b)
    {
        var l1 = Luminance(a); var l2 = Luminance(b);
        return (Math.Max(l1, l2) + .05) / (Math.Min(l1, l2) + .05);
    }

    private static double Luminance(string rgb)
    {
        var v = new[] { rgb[..2], rgb[2..4], rgb[4..6] }
            .Select(x => int.Parse(x, NumberStyles.HexNumber) / 255d)
            .Select(x => x <= .04045 ? x / 12.92 : Math.Pow((x + .055) / 1.055, 2.4)).ToArray();
        return .2126 * v[0] + .7152 * v[1] + .0722 * v[2];
    }

    [Fact]
    public void WorkloadsPrimaryAction_HasConsistentThirtyTwoPixelMinimumHeight()
    {
        var xaml = Read("src", "PowerFlow.App", "Settings", "RulesPage.xaml");
        Assert.Contains("Content=\"Add app\"", xaml, StringComparison.Ordinal);
        Assert.Matches(new System.Text.RegularExpressions.Regex("Content=\\\"Add app\\\"[^>]*MinHeight=\\\"32\\\""), xaml);
    }
    private static string Read(params string[] parts)
    {
        var dir = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(dir, "PowerFlow.sln")))
            dir = Directory.GetParent(dir)?.FullName ?? throw new DirectoryNotFoundException();
        return File.ReadAllText(Path.Combine(new[] { dir }.Concat(parts).ToArray()));
    }
    [Fact]
    public void CompactHeader_SeparatesIdentityAndModeStripAcrossRows()
    {
        var xaml = Read("src", "PowerFlow.App", "Dashboard", "ShellHeaderControl.xaml");
        var layout = Read("src", "PowerFlow.App", "Dashboard", "PowerFlowShellLayout.cs");
        var timeline = Read("src", "PowerFlow.App", "Dashboard", "PerformanceTimelineControl.xaml.cs");

        Assert.Contains("x:Name=\"CompactHeader\"", xaml, StringComparison.Ordinal);
        Assert.Contains("<RowDefinition Height=\"Auto\"/>", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"CompactModeStrip\" Grid.Row=\"1\"", xaml, StringComparison.Ordinal);
        Assert.Contains("new ShellGeometry(0, 10, 8, 74, 82)", layout, StringComparison.Ordinal);
        Assert.Contains("_resizeRedrawTimer.Stop()", timeline, StringComparison.Ordinal);
        Assert.Contains("TimeSpan.FromMilliseconds(16)", timeline, StringComparison.Ordinal);
    }
}
