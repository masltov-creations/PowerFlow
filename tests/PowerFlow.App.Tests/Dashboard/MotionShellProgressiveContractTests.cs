using System.Xml.Linq;
using Xunit;

namespace PowerFlow.App.Tests.Dashboard;

public sealed class MotionShellProgressiveContractTests
{
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void ShellChromePersistsOutsidePageSpecificLiveSurface()
    {
        var doc = XDocument.Load(RepoFile("src", "PowerFlow.App", "Dashboard", "MainWindow.xaml"));
        var header = Named(doc, "SystemHeaderRow");
        var drag = Named(doc, "DragSurface");
        var actions = Named(doc, "PresentationActions");
        var cockpit = Named(doc, "CockpitSurface");
        var sectionHost = Named(doc, "SectionHost");
        Assert.DoesNotContain(cockpit.AncestorsAndSelf(), e => ReferenceEquals(e, header));
        Assert.Contains(sectionHost, header.Ancestors());
        Assert.Contains(header, drag.Ancestors());
        Assert.Contains(header, actions.Ancestors());
        Assert.DoesNotContain(cockpit, header.AncestorsAndSelf());
    }

    [Fact]
    public void AllSectionsShareOneContentViewportBelowPersistentChrome()
    {
        var doc = XDocument.Load(RepoFile("src", "PowerFlow.App", "Dashboard", "MainWindow.xaml"));
        var viewport = Named(doc, "SectionContentHost");
        foreach (var name in new[] { "CockpitSurface", "CpuProfilePanel", "RulesPanel", "SettingsPanel" })
            Assert.Contains(viewport, Named(doc, name).Ancestors());
    }

    [Fact]
    public void LiveContextAndFooterAreProgressiveRatherThanCompactAlwaysOn()
    {
        var xaml = Read("src", "PowerFlow.App", "Dashboard", "MainWindow.xaml");
        var code = Read("src", "PowerFlow.App", "Dashboard", "MainWindow.xaml.cs");
        foreach (var name in new[] { "ActorContextColumn", "SelectedActorPanel", "GovernorDetailPanel", "StatusFooter" })
            Assert.Contains($"x:Name=\"{name}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ApplyDisclosureProgress", code, StringComparison.Ordinal);
        Assert.Contains("ShellDisclosurePolicy.Progress", code, StringComparison.Ordinal);
        var disclosure = Read("src", "PowerFlow.App", "Dashboard", "MainWindow.Disclosure.cs");
        Assert.Contains("ShellDisclosurePolicy.ContextProgress", disclosure, StringComparison.Ordinal);
        Assert.Contains("ShellDisclosurePolicy.FooterProgress", disclosure, StringComparison.Ordinal);
    }

    [Fact]
    public void AutomaticAndManualResizePathsBothDriveDisclosureContinuously()
    {
        var code = Read("src", "PowerFlow.App", "Dashboard", "MainWindow.xaml.cs");
        Assert.Contains("ApplyDisclosureProgress", MethodBody(code, "private void ApplyMotionFrame"), StringComparison.Ordinal);
        Assert.Contains("ApplyDisclosureProgress", MethodBody(code, "private void ApplyInteractiveResizeFrame"), StringComparison.Ordinal);
        Assert.Contains("ApplyDisclosureProgress", MethodBody(code, "private void ApplyResponsiveResizeMorph"), StringComparison.Ordinal);
    }

    [Fact]
    public void SectionNavigationChangesVisibilityOnlyNeverNativeGeometry()
    {
        var code = Read("src", "PowerFlow.App", "Dashboard", "MainWindow.xaml.cs");
        var body = MethodBody(code, "private Task NavigateToSectionAsync");
        Assert.Contains("ApplySectionVisibility", body, StringComparison.Ordinal);
        Assert.DoesNotContain("MoveAndResize", body, StringComparison.Ordinal);
        Assert.DoesNotContain("TransitionToAsync", body, StringComparison.Ordinal);
        Assert.DoesNotContain("ResolveTargetBounds", body, StringComparison.Ordinal);
    }

    private static XElement Named(XDocument doc, string name)
        => doc.Descendants().Single(e => (string?)e.Attribute(X + "Name") == name);

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
        throw new Xunit.Sdk.XunitException($"Unclosed method {signature}");
    }

    private static string Read(params string[] parts) => File.ReadAllText(RepoFile(parts));
    private static string RepoFile(params string[] parts)
    {
        var dir = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(dir, "PowerFlow.sln"))) dir = Directory.GetParent(dir)?.FullName ?? throw new DirectoryNotFoundException();
        return Path.Combine(new[] { dir }.Concat(parts).ToArray());
    }
}
