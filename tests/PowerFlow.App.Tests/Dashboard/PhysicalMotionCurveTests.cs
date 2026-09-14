using System.Reflection;
using Windows.Graphics;
using PowerFlow.App.Dashboard;
using Xunit;

namespace PowerFlow.App.Tests.Dashboard;

public sealed class PhysicalMotionCurveTests
{
    [Theory]
    [InlineData("Rigid")]
    [InlineData("Fluid")]
    public void NativeShellMaterials_AreMonotonic(string materialName)
    {
        var samples = Enumerable.Range(0, 101).Select(i => Sample(materialName, i / 100d)).ToArray();
        for (var i = 1; i < samples.Length; i++) Assert.True(samples[i].Progress + 1e-9 >= samples[i - 1].Progress, $"{materialName} reversed at {i}");
        Assert.Equal(0d, samples[0].Progress, 6);
        Assert.Equal(1d, samples[^1].Progress, 6);
    }

    [Fact]
    public void FluidMaterial_UsesMinimumJerkShapeInsteadOfFrontLoadedSnap()
    {
        var start = Sample("Fluid", .10d);
        var middle = Sample("Fluid", .50d);
        var finish = Sample("Fluid", .90d);

        Assert.InRange(start.Progress, .005d, .020d);
        Assert.InRange(middle.Progress, .49d, .51d);
        Assert.InRange(finish.Progress, .98d, .995d);
        Assert.True(start.Velocity < middle.Velocity);
        Assert.True(finish.Velocity < middle.Velocity);
    }

    [Fact]
    public void CompliantChildMotion_IsMonotonicAndDoesNotOvershootLayout()
    {
        var samples = Enumerable.Range(0, 201).Select(i => Sample("Compliant", i / 200d)).ToArray();
        for (var i = 1; i < samples.Length; i++)
            Assert.True(samples[i].Progress + 1e-9 >= samples[i - 1].Progress, $"Compliant reversed at {i}");
        Assert.All(samples, sample => Assert.InRange(sample.Progress, 0d, 1d));
        Assert.Equal(1d, samples[^1].Progress, 6);
    }
    [Fact]
    public void CompliantMaterial_TrailsFluidWithoutRubberBandOvershoot()
    {
        var fluid = Sample("Fluid", .50d);
        var compliant = Sample("Compliant", .50d);
        Assert.InRange(compliant.Progress, .35d, .49d);
        Assert.True(compliant.Progress < fluid.Progress);
        Assert.InRange(compliant.Progress, 0d, 1d);
    }
    [Fact]
    public void FluidMaterial_SquashStretchStaysWithinThreePercent()
    {
        var samples = Enumerable.Range(0, 101).Select(i => Sample("Fluid", i / 100d)).ToArray();
        Assert.All(samples, sample => Assert.InRange(sample.SecondaryScale, .97d, 1.03d));
        Assert.Equal(1d, samples[0].SecondaryScale, 6);
        Assert.Equal(1d, samples[^1].SecondaryScale, 6);
    }

    [Theory]
    [InlineData("Rigid")]
    [InlineData("Compliant")]
    [InlineData("Fluid")]
    public void EveryMaterial_LandsAtRest(string materialName)
    {
        var final = Sample(materialName, 1d);
        Assert.Equal(1d, final.Progress, 6);
        Assert.Equal(0d, final.Velocity, 6);
        Assert.Equal(1d, final.SecondaryScale, 6);
    }

    private static (double Progress, double Velocity, double SecondaryScale) Sample(string materialName, double t)
    {
        var assembly = typeof(PowerFlowShellLayout).Assembly;
        var curveType = assembly.GetType("PowerFlow.App.Dashboard.PhysicalMotionCurve");
        var materialType = assembly.GetType("PowerFlow.App.Dashboard.MotionMaterial");
        Assert.NotNull(curveType);
        Assert.NotNull(materialType);
        var material = Enum.Parse(materialType!, materialName);
        var method = curveType!.GetMethod("Sample", BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(method);
        var sample = method!.Invoke(null, [material, t, 0d]);
        Assert.NotNull(sample);
        var type = sample!.GetType();
        return (
            (double)type.GetProperty("Progress")!.GetValue(sample)!,
            (double)type.GetProperty("Velocity")!.GetValue(sample)!,
            (double)type.GetProperty("SecondaryScale")!.GetValue(sample)!);
    }
}

public sealed class ShellMotionCoordinatorTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 12, 17, 0, 0, TimeSpan.Zero);

    [Fact]
    public void RetargetingMidFlight_StartsFromCurrentOnScreenBounds()
    {
        var assembly = typeof(PowerFlowShellLayout).Assembly;
        var coordinatorType = assembly.GetType("PowerFlow.App.Dashboard.ShellMotionCoordinator");
        var materialType = assembly.GetType("PowerFlow.App.Dashboard.MotionMaterial");
        Assert.NotNull(coordinatorType);
        Assert.NotNull(materialType);
        var coordinator = Activator.CreateInstance(coordinatorType!)!;
        var rigid = Enum.Parse(materialType!, "Rigid");
        var begin = coordinatorType!.GetMethod("Begin")!;
        var sample = coordinatorType.GetMethod("Sample")!;
        var retarget = coordinatorType.GetMethod("Retarget")!;
        begin.Invoke(coordinator, [new RectInt32(100, 100, 320, 176), new RectInt32(100, 100, 760, 440), rigid, TimeSpan.FromMilliseconds(500), T0, 0d]);
        var at = T0.AddMilliseconds(220);
        var before = sample.Invoke(coordinator, [at])!;
        var beforeBounds = (RectInt32)before.GetType().GetProperty("Bounds")!.GetValue(before)!;
        retarget.Invoke(coordinator, [new RectInt32(100, 100, 1280, 800), rigid, TimeSpan.FromMilliseconds(600), at]);
        var after = sample.Invoke(coordinator, [at])!;
        var afterBounds = (RectInt32)after.GetType().GetProperty("Bounds")!.GetValue(after)!;
        Assert.Equal(beforeBounds, afterBounds);
    }

    [Fact]
    public void Coordinator_ReportsCompleteAtTargetEndpoint()
    {
        var assembly = typeof(PowerFlowShellLayout).Assembly;
        var coordinatorType = assembly.GetType("PowerFlow.App.Dashboard.ShellMotionCoordinator");
        var materialType = assembly.GetType("PowerFlow.App.Dashboard.MotionMaterial");
        Assert.NotNull(coordinatorType);
        Assert.NotNull(materialType);
        var coordinator = Activator.CreateInstance(coordinatorType!)!;
        var fluid = Enum.Parse(materialType!, "Fluid");
        var begin = coordinatorType!.GetMethod("Begin")!;
        var sample = coordinatorType.GetMethod("Sample")!;
        var target = new RectInt32(300, 200, 760, 440);
        begin.Invoke(coordinator, [new RectInt32(500, 700, 1, 1), target, fluid, TimeSpan.FromMilliseconds(400), T0, 0d]);
        var final = sample.Invoke(coordinator, [T0.AddMilliseconds(500)])!;
        Assert.Equal(target, (RectInt32)final.GetType().GetProperty("Bounds")!.GetValue(final)!);
        Assert.True((bool)final.GetType().GetProperty("IsComplete")!.GetValue(final)!);
    }
}