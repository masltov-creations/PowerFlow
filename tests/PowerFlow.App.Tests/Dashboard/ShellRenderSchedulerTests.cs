using System.Reflection;
using PowerFlow.App.Dashboard;
using Xunit;

namespace PowerFlow.App.Tests.Dashboard;

public sealed class ShellRenderSchedulerTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 12, 17, 0, 0, TimeSpan.Zero);

    [Fact]
    public void BurstOfNativeResizeSamples_CoalescesToLatestFrame()
    {
        var scheduler = NewScheduler();
        Invoke(scheduler, "SubmitSize", new ShellLogicalSize(800, 500), T0);
        Invoke(scheduler, "SubmitSize", new ShellLogicalSize(820, 520), T0.AddMilliseconds(3));
        Invoke(scheduler, "SubmitSize", new ShellLogicalSize(840, 540), T0.AddMilliseconds(6));
        var latest = (ShellLogicalSize?)Invoke(scheduler, "ConsumePendingFrame");
        Assert.Equal(new ShellLogicalSize(840, 540), latest);
        Assert.Null((ShellLogicalSize?)Invoke(scheduler, "ConsumePendingFrame"));
    }

    [Fact]
    public void SemanticCommit_RequiresResizeToSettle()
    {
        var scheduler = NewScheduler();
        Invoke(scheduler, "SubmitSize", new ShellLogicalSize(900, 560), T0);
        Assert.False((bool)Invoke(scheduler, "ShouldCommit", T0.AddMilliseconds(80))!);
        Assert.True((bool)Invoke(scheduler, "ShouldCommit", T0.AddMilliseconds(111))!);
    }

    [Fact]
    public void NewSample_AfterSettleResetsCommitClock()
    {
        var scheduler = NewScheduler();
        Invoke(scheduler, "SubmitSize", new ShellLogicalSize(900, 560), T0);
        Assert.True((bool)Invoke(scheduler, "ShouldCommit", T0.AddMilliseconds(111))!);
        Invoke(scheduler, "SubmitSize", new ShellLogicalSize(910, 570), T0.AddMilliseconds(120));
        Assert.False((bool)Invoke(scheduler, "ShouldCommit", T0.AddMilliseconds(200))!);
        Assert.True((bool)Invoke(scheduler, "ShouldCommit", T0.AddMilliseconds(231))!);
    }

    private static object NewScheduler()
    {
        var type = typeof(PowerFlowShellLayout).Assembly.GetType("PowerFlow.App.Dashboard.ShellRenderScheduler");
        Assert.NotNull(type);
        return Activator.CreateInstance(type!, [TimeSpan.FromMilliseconds(110)])!;
    }

    private static object? Invoke(object target, string name, params object[] args)
    {
        var method = target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.Public);
        Assert.NotNull(method);
        return method!.Invoke(target, args);
    }
}

public sealed class ShellResizeContractTests
{
    [Fact]
    public void NativeResizeEvent_CoalescesFramesAndUsesSameProjectionAsSettledCommit()
    {
        var code = Read("src", "PowerFlow.App", "Dashboard", "MainWindow.xaml.cs");
        var body = MethodBody(code, "private void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)");
        Assert.Contains("_resizeRenderScheduler.SubmitSize", body, StringComparison.Ordinal);
        Assert.Contains("EnsureResizeRenderLoop", body, StringComparison.Ordinal);
        Assert.DoesNotContain("ApplyResponsiveResizeMorph", body, StringComparison.Ordinal);
        Assert.DoesNotContain("ApplyShellLayout", body, StringComparison.Ordinal);
    }

    [Fact]
    public void ResizeLoop_UsesOneSemanticLayerAndSharedProjectionDuringDragAndSettle()
    {
        var code = Read("src", "PowerFlow.App", "Dashboard", "MainWindow.xaml.cs");
        Assert.Contains("_resizeFrameTimer", code, StringComparison.Ordinal);
        Assert.Contains("OnResizeFrameTick", code, StringComparison.Ordinal);
        var interactive = MethodBody(code, "private void ApplyInteractiveResizeFrame");
        var commit = MethodBody(code, "private void CommitResizePresentation");
        Assert.Contains("ShellManualResizePolicy.Resolve", interactive, StringComparison.Ordinal);
        Assert.Contains("ShellManualResizePolicy.Resolve", commit, StringComparison.Ordinal);
        Assert.Contains("ApplyDisclosureProgress", interactive, StringComparison.Ordinal);
        Assert.Contains("ApplyCockpitGeometry", interactive, StringComparison.Ordinal);
        Assert.DoesNotContain("ApplyShellGeometryMorph", interactive, StringComparison.Ordinal);
        Assert.DoesNotContain("ApplyShellTransitionFrame", interactive, StringComparison.Ordinal);
        Assert.DoesNotContain("ApplyMorph(", interactive, StringComparison.Ordinal);
    }

    [Fact]
    public void ManualResize_UsesEndpointVisibilityWhileAutomaticMotionKeepsTransientLayersAlive()
    {
        var code = Read("src", "PowerFlow.App", "Dashboard", "MainWindow.xaml.cs");
        var interactive = MethodBody(code, "private void ApplyInteractiveResizeFrame");
        var automatic = MethodBody(code, "private void ApplyMotionFrame");
        Assert.Contains("ApplyDisclosureProgress(projection.Disclosure, settled: true)", interactive, StringComparison.Ordinal);
        Assert.Contains("settled: false", automatic, StringComparison.Ordinal);
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