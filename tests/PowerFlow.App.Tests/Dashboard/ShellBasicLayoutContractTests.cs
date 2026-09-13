using PowerFlow.App.Dashboard;
using Xunit;

namespace PowerFlow.App.Tests.Dashboard;

public sealed class ShellBasicLayoutContractTests
{
    [Fact]
    public void Header_MoveStripOccupiesDedicatedRowAboveInteractiveControls()
    {
        var xaml = Read("src", "PowerFlow.App", "Dashboard", "MainWindow.xaml");
        Assert.Contains("x:Name=\"DragStripRowDefinition\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"DragSurface\" Grid.Row=\"0\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"HeaderContentRow\" Grid.Row=\"1\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SystemHeaderHost\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"PresentationActions\" Grid.Column=\"1\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("SystemHeaderHost\" Margin=\"0,10,0,0\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("PresentationActions\" Grid.Column=\"1\" Margin=\"0,10,0,0\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void TimelineRow_HasMinimumHeightSoGovernorCannotCollapseItDuringResize()
    {
        var xaml = Read("src", "PowerFlow.App", "Dashboard", "MainWindow.xaml");
        Assert.Contains("x:Name=\"TimelineRowDefinition\" Height=\"*\" MinHeight=\"156\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void RestingGovernorBand_UsesOneContentSafeHeightPolicy()
    {
        var code = Read("src", "PowerFlow.App", "Dashboard", "MainWindow.xaml.cs");
        var body = MethodBody(code, "private void ApplyCockpitGeometry");
        Assert.Contains("new GridLength(ControlBandHeight(profile))", body, StringComparison.Ordinal);
        Assert.Contains("GovernorControlPresentation.Bias => 96", code, StringComparison.Ordinal);
        Assert.Contains("GovernorControlPresentation.Contextual => Math.Max(144", code, StringComparison.Ordinal);
        Assert.Contains("_ => Math.Max(156", code, StringComparison.Ordinal);
        Assert.DoesNotContain("GridLength.Auto", body, StringComparison.Ordinal);
    }

    [Fact]
    public void SettledDisclosure_CollapsesSubReadableTextFragments()
    {
        Assert.Equal(0d, ShellDisclosurePolicy.SettledContextProgress(.30d), 6);
        Assert.Equal(0d, ShellDisclosurePolicy.SettledFooterProgress(.62d), 6);
        Assert.True(ShellDisclosurePolicy.SettledContextProgress(.60d) > 0d);
        Assert.True(ShellDisclosurePolicy.SettledFooterProgress(.88d) > 0d);

        var disclosure = Read("src", "PowerFlow.App", "Dashboard", "MainWindow.Disclosure.cs");
        Assert.Contains("SettledContextProgress", disclosure, StringComparison.Ordinal);
        Assert.Contains("SettledFooterProgress", disclosure, StringComparison.Ordinal);
    }

    [Fact]
    public void LayoutMotion_NeverOverwritesXamlOwnedCompositionOffset()
    {
        foreach (var parts in new[]
        {
            new[] { "src", "PowerFlow.App", "Dashboard", "MainWindow.xaml.cs" },
            new[] { "src", "PowerFlow.App", "Dashboard", "MainWindow.Disclosure.cs" },
            new[] { "src", "PowerFlow.App", "Dashboard", "ShellHeaderControl.xaml.cs" }
        })
        {
            var code = Read(parts);
            Assert.DoesNotContain(".Offset =", code, StringComparison.Ordinal);
        }

        Assert.Contains("SetLayoutTranslation", Read("src", "PowerFlow.App", "Dashboard", "MainWindow.xaml.cs"), StringComparison.Ordinal);
        Assert.Contains("SetLayoutTranslation", Read("src", "PowerFlow.App", "Dashboard", "MainWindow.Disclosure.cs"), StringComparison.Ordinal);
        Assert.Contains("SetLayoutTranslation", Read("src", "PowerFlow.App", "Dashboard", "ShellHeaderControl.xaml.cs"), StringComparison.Ordinal);
    }
    private static string MethodBody(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Missing method {signature}");
        var open = source.IndexOf('{', start);
        var depth = 0;
        for (var i = open; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}' && --depth == 0) return source[open..(i + 1)];
        }
        throw new InvalidOperationException($"Could not parse method body for {signature}");
    }

    private static string Read(params string[] parts)
    {
        var dir = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(dir, "PowerFlow.sln")))
            dir = Directory.GetParent(dir)?.FullName ?? throw new DirectoryNotFoundException();
        return File.ReadAllText(Path.Combine(new[] { dir }.Concat(parts).ToArray()));
    }
}




