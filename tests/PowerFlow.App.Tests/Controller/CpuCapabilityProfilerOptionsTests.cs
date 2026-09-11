using PowerFlow.App.Controller;
using PowerFlow.Core.Profiling;
using Xunit;

namespace PowerFlow.App.Tests.Controller;

public sealed class CpuCapabilityProfilerOptionsTests
{
    [Fact]
    public void Quick_profile_preserves_existing_short_probe_behavior()
    {
        var options = CpuCapabilityProfilerOptions.Quick;
        Assert.Equal(new[] { 1, 2, 4, 8, 16 }, options.WorkerCounts);
        Assert.Equal(TimeSpan.FromMilliseconds(300), options.WarmupDuration);
        Assert.Equal(TimeSpan.FromSeconds(2), options.PointDuration);
        Assert.Equal(TimeSpan.FromMilliseconds(250), options.InterPointDelay);
    }

    [Fact]
    public void Standard_baseline_profile_consumes_exact_benchmark_budget()
    {
        var schedule = MachineBaselineSchedule.Standard;
        var options = CpuCapabilityProfilerOptions.ForBaseline(schedule);

        Assert.Equal(new[] { 1, 2, 4, 8, 16 }, options.WorkerCounts);
        Assert.Equal(TimeSpan.Zero, options.WarmupDuration);
        Assert.Equal(schedule.BenchmarkPointDuration, options.PointDuration);
        Assert.Equal(TimeSpan.Zero, options.InterPointDelay);
        Assert.Equal(TimeSpan.FromSeconds(195), options.TotalDuration);
        Assert.Equal(schedule.ModeDuration, schedule.SettleDuration + schedule.IdleDuration + options.TotalDuration);
    }

    [Fact]
    public void Profiler_loop_honors_configured_point_duration_and_interpoint_delay()
    {
        var root = FindRoot();
        var source = File.ReadAllText(Path.Combine(root, "src", "PowerFlow.App", "Controller", "CpuCapabilityProfiler.cs"));
        Assert.Contains("MeasureAsync(telemetry, workers, options.PointDuration, cancellationToken)", source, StringComparison.Ordinal);
        Assert.Contains("options.InterPointDelay", source, StringComparison.Ordinal);
        Assert.DoesNotContain("MeasureAsync(telemetry, workers, TimeSpan.FromSeconds(2), cancellationToken)", source, StringComparison.Ordinal);
    }

    private static string FindRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(dir, "PowerFlow.sln")))
            dir = Directory.GetParent(dir)?.FullName ?? throw new DirectoryNotFoundException();
        return dir;
    }}