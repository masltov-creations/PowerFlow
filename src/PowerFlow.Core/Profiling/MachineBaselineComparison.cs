namespace PowerFlow.Core.Profiling;

public enum MachineBaselineMode
{
    WindowsSaver,
    WindowsBalanced,
    BalancedEfficient,
    BalancedPerformance,
    Performance,
    Auto
}

public sealed record MachineBaselineSchedule(
    TimeSpan ModeDuration,
    TimeSpan SettleDuration,
    TimeSpan IdleDuration,
    TimeSpan BenchmarkPointDuration,
    IReadOnlyList<MachineBaselineMode> Modes)
{
    public static MachineBaselineSchedule Standard { get; } = new(
        TimeSpan.FromMinutes(5),
        TimeSpan.FromSeconds(15),
        TimeSpan.FromSeconds(90),
        TimeSpan.FromSeconds(39),
        new[]
        {
            MachineBaselineMode.WindowsSaver,
            MachineBaselineMode.WindowsBalanced,
            MachineBaselineMode.BalancedEfficient,
            MachineBaselineMode.BalancedPerformance,
            MachineBaselineMode.Performance,
            MachineBaselineMode.Auto
        });

    public TimeSpan TotalDuration => TimeSpan.FromTicks(ModeDuration.Ticks * Modes.Count);
}

public sealed record MachineIdleSummary(
    TimeSpan ObservedDuration,
    double? AveragePackageWatts,
    double? P95PackageWatts,
    double? AverageProcessorPerformancePercent,
    double? AverageSpeedGhz,
    double? AveragePressurePercent,
    double? AverageQueueLength,
    double? EnergyWh,
    int SampleCount);

public sealed record MachineBaselineModeResult(
    MachineBaselineMode Mode,
    string Label,
    string PolicySignature,
    MachineIdleSummary Idle,
    CpuCapabilityProfile Benchmark)
{
    public double MaxThroughputMops => Benchmark.Points.Count == 0 ? 0d : Benchmark.Points.Max(point => point.ThroughputMops);
    public double? BestThroughputPerWatt => Benchmark.Points.Where(point => point.ThroughputPerWatt is not null).Select(point => point.ThroughputPerWatt).Max();
    public double? SingleThreadThroughputMops => Benchmark.Points.FirstOrDefault(point => point.WorkerCount == 1)?.ThroughputMops;
}

public sealed record MachineBaselineRecommendation(
    MachineBaselineMode RecommendedMode,
    MachineBaselineMode? LowestIdlePowerMode,
    MachineBaselineMode BestSingleThreadMode,
    MachineBaselineMode BestEfficiencyMode,
    MachineBaselineMode MaxThroughputMode,
    double? IdleWattsAvoidedVsWindowsBalanced,
    string Reason);

public sealed record MachineBaselineComparisonRun(
    Guid Id,
    DateTimeOffset CapturedAt,
    MachineBaselineSchedule Schedule,
    IReadOnlyList<MachineBaselineModeResult> Results,
    MachineBaselineRecommendation Recommendation);

public static class MachineBaselineAnalysis
{
    public static MachineBaselineRecommendation Recommend(IReadOnlyList<MachineBaselineModeResult> results)
    {
        if (results is null || results.Count == 0)
            throw new ArgumentException("At least one baseline mode result is required.", nameof(results));

        var maxThroughputResult = results.MaxBy(result => result.MaxThroughputMops)!;
        var singleThreadResult = results
            .Where(result => result.SingleThreadThroughputMops is not null)
            .MaxBy(result => result.SingleThreadThroughputMops) ?? maxThroughputResult;
        var efficiencyResult = results
            .Where(result => result.BestThroughputPerWatt is not null)
            .MaxBy(result => result.BestThroughputPerWatt) ?? maxThroughputResult;
        var lowestIdle = results
            .Where(result => result.Idle.AveragePackageWatts is not null)
            .MinBy(result => result.Idle.AveragePackageWatts);

        var threshold = maxThroughputResult.MaxThroughputMops * .95d;
        var nearMax = results.Where(result => result.MaxThroughputMops >= threshold).ToArray();
        var recommended = nearMax
            .Where(result => result.Idle.AveragePackageWatts is not null)
            .MinBy(result => result.Idle.AveragePackageWatts)
            ?? efficiencyResult;

        var windowsBalanced = results.FirstOrDefault(result => result.Mode == MachineBaselineMode.WindowsBalanced);
        double? avoidedVsBalanced = windowsBalanced?.Idle.AveragePackageWatts is double baselineWatts
            && recommended.Idle.AveragePackageWatts is double recommendedWatts
            ? baselineWatts - recommendedWatts
            : null;

        var reason = recommended.Idle.AveragePackageWatts is double watts
            ? $"{recommended.Label} is the lowest-idle-power mode among profiles delivering at least 95% of the measured maximum throughput ({watts:0.0} W idle average)."
            : $"{recommended.Label} has the best measured throughput per watt among profiles when idle-power evidence is unavailable; the 95% throughput gate could not be resolved by idle cost.";

        return new MachineBaselineRecommendation(
            recommended.Mode,
            lowestIdle?.Mode,
            singleThreadResult.Mode,
            efficiencyResult.Mode,
            maxThroughputResult.Mode,
            avoidedVsBalanced,
            reason);
    }
}
public static class MachineBaselineHistory
{
    public const int MaximumRuns = 5;

    public static IReadOnlyList<MachineBaselineComparisonRun> Upsert(
        IReadOnlyList<MachineBaselineComparisonRun>? existing,
        MachineBaselineComparisonRun run,
        int maximumRuns = MaximumRuns)
    {
        ArgumentNullException.ThrowIfNull(run);
        if (maximumRuns < 1) throw new ArgumentOutOfRangeException(nameof(maximumRuns));
        return (existing ?? Array.Empty<MachineBaselineComparisonRun>())
            .Where(candidate => candidate.Id != run.Id)
            .Append(run)
            .OrderByDescending(candidate => candidate.CapturedAt)
            .Take(maximumRuns)
            .ToArray();
    }
}