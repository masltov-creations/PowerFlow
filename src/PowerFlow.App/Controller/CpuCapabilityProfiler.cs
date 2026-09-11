using System.Diagnostics;
using PowerFlow.Core.Profiling;
using PowerFlow.Windows.Activity;
using PowerFlow.Windows.Power;

namespace PowerFlow.App.Controller;

public sealed record CpuCapabilityProgress(int Step, int TotalSteps, int WorkerCount, string Message);

public sealed record CpuCapabilityProfilerOptions(
    IReadOnlyList<int> WorkerCounts,
    TimeSpan WarmupDuration,
    TimeSpan PointDuration,
    TimeSpan InterPointDelay)
{
    public static CpuCapabilityProfilerOptions Quick { get; } = new(
        new[] { 1, 2, 4, 8, 16 },
        TimeSpan.FromMilliseconds(300),
        TimeSpan.FromSeconds(2),
        TimeSpan.FromMilliseconds(250));

    public static CpuCapabilityProfilerOptions ForBaseline(MachineBaselineSchedule schedule)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        return new(new[] { 1, 2, 4, 8, 16 }, TimeSpan.Zero, schedule.BenchmarkPointDuration, TimeSpan.Zero);
    }

    public TimeSpan TotalDuration => TimeSpan.FromTicks(PointDuration.Ticks * WorkerCounts.Count)
        + WarmupDuration
        + TimeSpan.FromTicks(InterPointDelay.Ticks * Math.Max(0, WorkerCounts.Count - 1));
}

public interface ICpuCapabilityProfiler
{
    Task<CpuCapabilityProfile> RunAsync(CpuCapabilityProfilerOptions options, IProgress<CpuCapabilityProgress>? progress = null, CancellationToken cancellationToken = default);
}

public sealed class CpuCapabilityProfiler : ICpuCapabilityProfiler
{
    private static long _sink;
    private readonly IProcessorPolicyController _policy;
    private readonly Func<DashboardTelemetrySource> _telemetryFactory;

    public CpuCapabilityProfiler(IProcessorPolicyController? policy = null, Func<DashboardTelemetrySource>? telemetryFactory = null)
    {
        _policy = policy ?? new WindowsProcessorPolicyController();
        _telemetryFactory = telemetryFactory ?? (() => new DashboardTelemetrySource());
    }

    public Task<CpuCapabilityProfile> RunAsync(IProgress<CpuCapabilityProgress>? progress = null, CancellationToken cancellationToken = default)
        => RunAsync(CpuCapabilityProfilerOptions.Quick, progress, cancellationToken);

    public async Task<CpuCapabilityProfile> RunAsync(CpuCapabilityProfilerOptions options, IProgress<CpuCapabilityProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.PointDuration <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(options), "Point duration must be positive.");
        var counts = options.WorkerCounts.Where(value => value > 0 && value <= Math.Max(1, Environment.ProcessorCount)).Distinct().OrderBy(value => value).ToArray();
        if (counts.Length == 0) counts = [1];
        var policy = _policy.CaptureActive();
        var label = PolicyLabel(policy);
        var signature = $"{policy.SchemeId:D}|floor={policy.CoreParkingMinCoresPercent}|epp={policy.EnergyPerformancePreferencePercent}|boost={policy.ProcessorPerformanceBoostMode}";

        using var telemetry = _telemetryFactory();
        try { _ = telemetry.Read(DateTimeOffset.UtcNow); } catch { }
        if (options.WarmupDuration > TimeSpan.Zero) await RunKernelAsync(1, options.WarmupDuration, cancellationToken);

        var points = new List<CpuCapabilityPoint>(counts.Length);
        for (var i = 0; i < counts.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var workers = counts[i];
            progress?.Report(new CpuCapabilityProgress(i + 1, counts.Length, workers, $"Profiling {workers} worker{(workers == 1 ? string.Empty : "s")}"));
            points.Add(await MeasureAsync(telemetry, workers, options.PointDuration, cancellationToken));
            if (i < counts.Length - 1 && options.InterPointDelay > TimeSpan.Zero)
                await Task.Delay(options.InterPointDelay, cancellationToken);
        }
        return CpuCapabilityAnalysis.Build(DateTimeOffset.UtcNow, label, signature, points);
    }

    private static async Task<CpuCapabilityPoint> MeasureAsync(DashboardTelemetrySource telemetry, int workers, TimeSpan duration, CancellationToken cancellationToken)
    {
        using var runCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var workerTasks = Enumerable.Range(0, workers)
            .Select(index => Task.Factory.StartNew(() => Kernel(runCts.Token, index + 1), CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default))
            .ToArray();
        var watts = new List<double>();
        var performance = new List<double>();
        var speeds = new List<double>();
        var sw = Stopwatch.StartNew();
        try
        {
            while (sw.Elapsed < duration)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var sample = telemetry.Read(DateTimeOffset.UtcNow);
                    if (sample.PackageWatts is double w && double.IsFinite(w) && w >= 0) watts.Add(w);
                    if (sample.ProcessorPerformancePercent is double p && double.IsFinite(p) && p >= 0) performance.Add(p);
                    if (sample.AverageMhz is double mhz && double.IsFinite(mhz) && mhz > 0) speeds.Add(mhz / 1000d);
                }
                catch { }
                await Task.Delay(200, cancellationToken);
            }
        }
        finally
        {
            runCts.Cancel();
        }
        var operations = (await Task.WhenAll(workerTasks)).Sum();
        var seconds = Math.Max(.001, sw.Elapsed.TotalSeconds);
        return new CpuCapabilityPoint(
            workers,
            operations / seconds / 1_000_000d,
            AverageOrNull(watts),
            AverageOrNull(performance),
            AverageOrNull(speeds));
    }

    private static async Task RunKernelAsync(int workers, TimeSpan duration, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var tasks = Enumerable.Range(0, workers)
            .Select(index => Task.Factory.StartNew(() => Kernel(cts.Token, index + 17), CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default))
            .ToArray();
        await Task.Delay(duration, cancellationToken);
        cts.Cancel();
        await Task.WhenAll(tasks);
    }

    private static long Kernel(CancellationToken token, int seed)
    {
        ulong x = unchecked((ulong)(0x9E3779B97F4A7C15UL + (uint)seed));
        long operations = 0;
        while (!token.IsCancellationRequested)
        {
            for (var i = 0; i < 2048; i++)
            {
                x ^= x << 13;
                x ^= x >> 7;
                x ^= x << 17;
                x *= 0xD1342543DE82EF95UL;
            }
            operations += 2048;
        }
        Interlocked.Exchange(ref _sink, unchecked((long)x));
        return operations;
    }

    private static double? AverageOrNull(IReadOnlyList<double> values) => values.Count == 0 ? null : values.Average();

    internal static string PolicyLabel(ProcessorPolicySnapshot policy)
    {
        if (policy.CoreParkingMinCoresPercent == 10 && policy.EnergyPerformancePreferencePercent == 60 && policy.ProcessorPerformanceBoostMode == 0) return "SAVER";
        if (policy.CoreParkingMinCoresPercent == 25 && policy.EnergyPerformancePreferencePercent == 35 && policy.ProcessorPerformanceBoostMode == 3) return "BAL-E";
        if (policy.CoreParkingMinCoresPercent == 50 && policy.EnergyPerformancePreferencePercent == 20 && policy.ProcessorPerformanceBoostMode == 3) return "BAL-P";
        if (policy.CoreParkingMinCoresPercent == 75 && policy.EnergyPerformancePreferencePercent == 10 && policy.ProcessorPerformanceBoostMode == 2) return "PERFORMANCE";
        if (policy.CoreParkingMinCoresPercent == 100 && policy.EnergyPerformancePreferencePercent == 10 && policy.ProcessorPerformanceBoostMode == 2) return "ULTRA";
        return "CUSTOM";
    }
}