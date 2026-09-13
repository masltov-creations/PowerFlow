using Xunit;

namespace PowerFlow.App.Tests.Shell;

public sealed class ShellLifecycleContractTests
{
    [Fact]
    public void App_RoutesAllVisualRequestsThroughOneShellWindow()
    {
        var app = Read("src", "PowerFlow.App", "App.xaml.cs");
        Assert.Contains("MainWindow? _shellWindow", app, StringComparison.Ordinal);
        Assert.DoesNotContain("_dashboardWindow", app, StringComparison.Ordinal);
        Assert.DoesNotContain("new TrayHoverWindow", app, StringComparison.Ordinal);
        Assert.Contains("InteractionRequested += OnTrayInteractionRequested", app, StringComparison.Ordinal);
        Assert.Contains("ShowShellAsync", app, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindow_IsTheMorphingShellAndMovesPositionAndSizeTogether()
    {
        var code = Read("src", "PowerFlow.App", "Dashboard", "MainWindow.xaml.cs");
        var xaml = Read("src", "PowerFlow.App", "Dashboard", "MainWindow.xaml");

        Assert.Contains("ShowShellAsync", code, StringComparison.Ordinal);
        Assert.Contains("TransitionToAsync", code, StringComparison.Ordinal);
        Assert.Contains("ShellActivationMode", code, StringComparison.Ordinal);
        Assert.Contains("AppWindow.MoveAndResize", code, StringComparison.Ordinal);
        Assert.Contains("WS_EX_NOACTIVATE", code, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ShellRoot\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"GlanceTapTarget\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindow_CoalescesSupersededWindowTransitionsAndScopesResizeSyncSuppression()
    {
        var code = Read("src", "PowerFlow.App", "Dashboard", "MainWindow.xaml.cs");
        Assert.Contains("BeginShellTransition", code, StringComparison.Ordinal);
        Assert.Contains("CancelPresentationAnimation", code, StringComparison.Ordinal);
        Assert.Contains("_motionCompletion", code, StringComparison.Ordinal);
        Assert.Contains("_transitionGeneration", code, StringComparison.Ordinal);
        Assert.Contains("IsCurrentTransition", code, StringComparison.Ordinal);
        Assert.Contains("_resizeModeSyncSuppressionDepth", code, StringComparison.Ordinal);
        Assert.DoesNotContain("_suppressResizeModeSync", code, StringComparison.Ordinal);
    }
    [Fact]
    public void MainWindow_HeaderBrandSurfacesMoveWindowDirectlyWithoutChangingPresentation()
    {
        var window = Read("src", "PowerFlow.App", "Dashboard", "MainWindow.xaml.cs");
        var header = Read("src", "PowerFlow.App", "Dashboard", "ShellHeaderControl.xaml.cs");
        var xaml = Read("src", "PowerFlow.App", "Dashboard", "ShellHeaderControl.xaml");
        Assert.Contains("DragStarted", header, StringComparison.Ordinal);
        Assert.Contains("surface.CapturePointer(e.Pointer)", header, StringComparison.Ordinal);
        Assert.Contains("_dragCaptureElement?.ReleasePointerCapture(e.Pointer)", header, StringComparison.Ordinal);
        Assert.Contains("SystemHeaderHost.DragStarted += OnHeaderDragStarted", window, StringComparison.Ordinal);
        Assert.Contains("AppWindow.Move(new PointInt32", window, StringComparison.Ordinal);
        Assert.Contains("_dispatcher.CreateTimer()", window, StringComparison.Ordinal);
        Assert.Contains("TimeSpan.FromMilliseconds(16)", window, StringComparison.Ordinal);
        Assert.Contains("OnHeaderDragTimerTick", window, StringComparison.Ordinal);
        Assert.Contains("Math.Abs(deltaX) < 3", window, StringComparison.Ordinal);
        Assert.Contains("_headerDragWindowOrigin", window, StringComparison.Ordinal);
        Assert.DoesNotContain("WmNcButtonDown", window, StringComparison.Ordinal);
        Assert.DoesNotContain("ReleaseCapture();", window, StringComparison.Ordinal);
        Assert.Contains("PowerFlowShellState.Glance or PowerFlowShellState.FullScreen", window, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"CompactBrandText\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SystemStateDragSurface\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotMatch("CompactNavigationButton[^>]*PointerPressed", xaml);
        Assert.DoesNotMatch("CompactModeStrip[^>]*PointerPressed", xaml);
        Assert.DoesNotMatch("SystemModeStrip[^>]*PointerPressed", xaml);
    }
    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine(new[] { RepoRoot() }.Concat(parts).ToArray()));

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "PowerFlow.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException();
    }
}