using PowerFlow.App.Dashboard;
using PowerFlow.App.Tray;
using Windows.Graphics;
using Xunit;

namespace PowerFlow.App.Tests.Tray;

public sealed class TrayMotionIntegrationTests
{
    [Fact]
    public void TrayHost_UsesFiniteWin32TimerAndCachedFramesRatherThanBackgroundAnimationLoop()
    {
        var code = Read("src", "PowerFlow.App", "Tray", "TrayIconHost.cs");
        Assert.Contains("TrayIconFrameCache", code, StringComparison.Ordinal);
        Assert.Contains("TrayIconMotionPolicy.Decide", code, StringComparison.Ordinal);
        Assert.Contains("SetTimer", code, StringComparison.Ordinal);
        Assert.Contains("KillTimer", code, StringComparison.Ordinal);
        Assert.Contains("WmTimer", code, StringComparison.Ordinal);
        Assert.Contains("_motionFrameIndex", code, StringComparison.Ordinal);
        Assert.DoesNotContain("System.Threading.Timer", code, StringComparison.Ordinal);
        Assert.DoesNotContain("PeriodicTimer", code, StringComparison.Ordinal);
    }

    [Fact]
    public void TrayFrameAssets_AreFiveCachedPosturesAndCopiedWithTheApp()
    {
        var root = RepoRoot();
        for (var i = 0; i < 5; i++)
            Assert.True(File.Exists(Path.Combine(root, "src", "PowerFlow.App", "Assets", "Tray", $"PowerFlow.Tray.{i}.ico")), $"Missing tray posture {i}");
        var project = Read("src", "PowerFlow.App", "PowerFlow.App.csproj");
        Assert.Contains("Assets\\Tray\\*.ico", project, StringComparison.Ordinal);
    }

    [Fact]
    public void HiddenMotionOrigin_UsesActualTrayRectangleRatherThanSyntheticPoint()
    {
        var tray = new TrayRect(1800, 1030, 1840, 1070);
        var work = new TrayRect(0, 0, 1920, 1080);
        var bounds = ShellTransitionGeometry.TargetBounds(tray, work, new RectInt32(0, 0, 1, 1), PowerFlowShellState.Hidden);
        Assert.Equal(new RectInt32(1800, 1030, 40, 40), bounds);
    }

    [Fact]
    public void AppPrefersShellNotifyIconRectangleBeforeObservedHoverFallback()
    {
        var code = Read("src", "PowerFlow.App", "App.xaml.cs");
        var body = MethodBody(code, "private bool TryResolveTrayGeometry");
        var shell = body.IndexOf("TryGetIconRect", StringComparison.Ordinal);
        var fallback = body.IndexOf("TryGetHoverAnchorRect", StringComparison.Ordinal);
        Assert.True(shell >= 0 && fallback > shell);
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
        throw new Xunit.Sdk.XunitException($"Unclosed method {signature}");
    }

    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine(new[] { RepoRoot() }.Concat(parts).ToArray()));
    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(dir, "PowerFlow.sln")))
            dir = Directory.GetParent(dir)?.FullName ?? throw new DirectoryNotFoundException();
        return dir;
    }
}