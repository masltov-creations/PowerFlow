using System.Reflection;
using PowerFlow.App.Dashboard;
using Windows.Graphics;
using Xunit;

namespace PowerFlow.App.Tests.Dashboard;

public sealed class ShellMotionIntegrationTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 12, 17, 30, 0, TimeSpan.Zero);

    [Fact]
    public void CoordinatorFrame_CarriesShellAndCompliantChildSamplesFromOneClock()
    {
        var coordinator = new ShellMotionCoordinator();
        coordinator.Begin(
            new RectInt32(100, 100, 320, 176),
            new RectInt32(300, 200, 760, 440),
            MotionMaterial.Fluid,
            TimeSpan.FromMilliseconds(500),
            T0);

        var frame = coordinator.Sample(T0.AddMilliseconds(250));
        var childProperty = typeof(ShellMotionFrame).GetProperty("ChildSample", BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(childProperty);
        var child = childProperty!.GetValue(frame);
        Assert.NotNull(child);
        var childProgress = (double)child!.GetType().GetProperty("Progress")!.GetValue(child)!;

        Assert.InRange(frame.Sample.Progress, 0d, 1d);
        Assert.InRange(childProgress, 0d, 1.025d);
        Assert.False(frame.IsComplete);
    }

    [Fact]
    public void Coordinator_RejectsCompliantMaterialForNativeWindowBounds()
    {
        var coordinator = new ShellMotionCoordinator();
        Assert.Throws<ArgumentOutOfRangeException>(() => coordinator.Begin(
            new RectInt32(0, 0, 320, 176),
            new RectInt32(0, 0, 760, 440),
            MotionMaterial.Compliant,
            TimeSpan.FromMilliseconds(300),
            T0));
    }

    [Fact]
    public void ZeroDurationMotion_LandsBothMaterialChannelsAtRest()
    {
        var coordinator = new ShellMotionCoordinator();
        var target = new RectInt32(40, 50, 760, 440);
        coordinator.Begin(new RectInt32(10, 20, 320, 176), target, MotionMaterial.Fluid, TimeSpan.Zero, T0);
        var frame = coordinator.Sample(T0);
        var childProperty = typeof(ShellMotionFrame).GetProperty("ChildSample", BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(childProperty);
        var child = childProperty!.GetValue(frame)!;

        Assert.Equal(target, frame.Bounds);
        Assert.True(frame.IsComplete);
        Assert.Equal(1d, frame.Sample.Progress, 6);
        Assert.Equal(0d, frame.Sample.Velocity, 6);
        Assert.Equal(1d, (double)child.GetType().GetProperty("Progress")!.GetValue(child)!, 6);
        Assert.Equal(0d, (double)child.GetType().GetProperty("Velocity")!.GetValue(child)!, 6);
    }

    [Fact]
    public void MainWindow_UsesOneCoordinatorAndRenderFrameClockForAutomaticMotion()
    {
        var code = Read("src", "PowerFlow.App", "Dashboard", "MainWindow.xaml.cs");
        Assert.Contains("private readonly ShellMotionCoordinator _motionCoordinator", code, StringComparison.Ordinal);
        Assert.Contains("CompositionTarget.Rendering += OnShellMotionRendering", code, StringComparison.Ordinal);
        Assert.Contains("CompositionTarget.Rendering -= OnShellMotionRendering", code, StringComparison.Ordinal);
        Assert.DoesNotContain("_presentationTimer", code, StringComparison.Ordinal);

        var animate = MethodBody(code, "private Task AnimateShellBoundsAsync");
        Assert.Contains("_motionCoordinator", animate, StringComparison.Ordinal);
        Assert.DoesNotContain("_dispatcher.CreateTimer", animate, StringComparison.Ordinal);
        Assert.DoesNotContain("ShellMotionPolicy.Ease", animate, StringComparison.Ordinal);
    }

    [Fact]
    public void OneMotionFrame_DrivesBoundsSemanticMorphAndMaterialResponse()
    {
        var code = Read("src", "PowerFlow.App", "Dashboard", "MainWindow.xaml.cs");
        var render = MethodBody(code, "private void OnShellMotionRendering");
        var apply = MethodBody(code, "private void ApplyMotionFrame");

        Assert.Contains("_motionCoordinator.Sample", render, StringComparison.Ordinal);
        Assert.Contains("ApplyMotionFrame(frame)", render, StringComparison.Ordinal);
        Assert.Contains("frame.Bounds", apply, StringComparison.Ordinal);
        Assert.Contains("frame.Sample.Progress", apply, StringComparison.Ordinal);
        Assert.Contains("frame.ChildSample.Progress", apply, StringComparison.Ordinal);
        Assert.Contains("ApplyMaterialResponse(frame.Sample, frame.ChildSample)", apply, StringComparison.Ordinal);
    }

    [Fact]
    public void InterruptedTransitions_AreVersionedSoSupersededAwaitersCannotFinalizeOldState()
    {
        var code = Read("src", "PowerFlow.App", "Dashboard", "MainWindow.xaml.cs");
        Assert.Contains("_transitionVersion", code, StringComparison.Ordinal);
        var transition = MethodBody(code, "public async Task TransitionToAsync");
        Assert.Contains("transitionVersion", transition, StringComparison.Ordinal);
        Assert.Contains("if (transitionVersion != _transitionVersion) return", transition, StringComparison.Ordinal);
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
        throw new Xunit.Sdk.XunitException($"Unclosed method {signature}");
    }

    private static string Read(params string[] parts)
    {
        var dir = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(dir, "PowerFlow.sln")))
            dir = Directory.GetParent(dir)?.FullName ?? throw new DirectoryNotFoundException();
        return File.ReadAllText(Path.Combine(new[] { dir }.Concat(parts).ToArray()));
    }
}