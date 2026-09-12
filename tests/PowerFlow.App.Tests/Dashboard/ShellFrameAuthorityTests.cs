using System.Reflection;
using PowerFlow.App.Dashboard;
using Xunit;

namespace PowerFlow.App.Tests.Dashboard;

public sealed class ShellFrameAuthorityTests
{
    [Fact]
    public void GeometryAuthority_NavigationNeverOwnsNativeBounds()
    {
        var type = typeof(PowerFlowShellLayout).Assembly.GetType("PowerFlow.App.Dashboard.ShellGeometryAuthority");
        Assert.NotNull(type);
        var intentType = typeof(PowerFlowShellLayout).Assembly.GetType("PowerFlow.App.Dashboard.ShellGeometryIntent");
        Assert.NotNull(intentType);
        var method = type!.GetMethod("ShouldChangeBounds", BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(method);
        var navigation = Enum.Parse(intentType!, "Navigation");
        var expand = Enum.Parse(intentType!, "Expand");
        Assert.False((bool)method!.Invoke(null, [navigation])!);
        Assert.True((bool)method.Invoke(null, [expand])!);
    }

    [Fact]
    public void Navigation_ChangesContentWithoutChangingShellGeometry()
    {
        var code = Read("src", "PowerFlow.App", "Dashboard", "MainWindow.xaml.cs");
        var body = MethodBody(code, "private Task NavigateToSectionAsync(string tag)");
        Assert.DoesNotContain("TransitionToAsync", body, StringComparison.Ordinal);
        Assert.DoesNotContain("MinimumState", body, StringComparison.Ordinal);
        Assert.DoesNotContain("MoveAndResize", body, StringComparison.Ordinal);
    }

    [Fact]
    public void PinnedShell_RegistersAnExplicitVisibleDragSurface()
    {
        var xaml = Read("src", "PowerFlow.App", "Dashboard", "MainWindow.xaml");
        var code = Read("src", "PowerFlow.App", "Dashboard", "MainWindow.xaml.cs");
        Assert.Contains("x:Name=\"DragSurface\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"DragHandle\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name=\"Move PowerFlow window\"", xaml, StringComparison.Ordinal);
        Assert.Contains("DragSurface.Height = compactMoveStrip ? 10d : 16d", code, StringComparison.Ordinal);
        Assert.Contains("SystemHeaderHost.Margin = new Thickness(0, headerTopInset, 0, 0)", code, StringComparison.Ordinal);
        Assert.Contains("ExtendsContentIntoTitleBar = true", code, StringComparison.Ordinal);
        Assert.Contains("SetTitleBar(DragSurface)", code, StringComparison.Ordinal);
        Assert.Contains("ConfigureCustomTitleBar", code, StringComparison.Ordinal);
    }

    private static string MethodBody(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Missing method {signature}");
        var open = source.IndexOf('{', start);
        Assert.True(open >= 0);
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