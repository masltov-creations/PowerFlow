using Xunit;
using PowerFlow.Core.Profiling;

namespace PowerFlow.Core.Tests.Profiling;

public sealed class MachineBaselineComparisonTests
{
    [Fact]
    public void Standard_schedule_is_seven_fixed_modes_at_five_minutes_each()
    {
        var schedule = MachineBaselineSchedule.Standard;

        Assert.Equal(TimeSpan.FromMinutes(5), schedule.ModeDuration);
        Assert.Equal(TimeSpan.FromSeconds(15), schedule.SettleDuration);
        Assert.Equal(TimeSpan.FromSeconds(90), schedule.IdleDuration);
        Assert.Equal(TimeSpan.FromSeconds(39), schedule.BenchmarkPointDuration);
        Assert.Equal(new[]
        {
            MachineBaselineMode.WindowsSaver,
            MachineBaselineMode.WindowsBalanced,
            MachineBaselineMode.PowerFlowSaver,
            MachineBaselineMode.BalancedEfficient,
            MachineBaselineMode.BalancedPerformance,
            MachineBaselineMode.Performance,
            MachineBaselineMode.Ultra
        }, schedule.Modes);
        Assert.Equal(TimeSpan.FromMinutes(35), schedule.TotalDuration);
    }

    [Fact]
    public void Recommendation_prefers_lowest_idle_power_among_modes_within_95_percent_of_global_max()
    {
        var run = BuildRun(
            Result(MachineBaselineMode.WindowsBalanced, "WIN BAL", idleWatts: 90, maxThroughput: 100, efficiency: 1.0, singleThread: 20),
            Result(MachineBaselineMode.BalancedEfficient, "BAL-E", idleWatts: 55, maxThroughput: 96, efficiency: 1.35, singleThread: 22),
            Result(MachineBaselineMode.BalancedPerformance, "BAL-P", idleWatts: 70, maxThroughput: 99, efficiency: 1.25, singleThread: 23),
            Result(MachineBaselineMode.Performance, "PERF", idleWatts: 105, maxThroughput: 101, efficiency: 1.05, singleThread: 25));

        var recommendation = MachineBaselineAnalysis.Recommend(run.Results);

        Assert.Equal(MachineBaselineMode.BalancedEfficient, recommendation.RecommendedMode);
        Assert.Equal(MachineBaselineMode.BalancedEfficient, recommendation.LowestIdlePowerMode);
        Assert.Equal(MachineBaselineMode.Performance, recommendation.BestSingleThreadMode);
        Assert.Equal(MachineBaselineMode.Performance, recommendation.MaxThroughputMode);
        Assert.Equal(MachineBaselineMode.BalancedEfficient, recommendation.BestEfficiencyMode);
        Assert.NotNull(recommendation.IdleWattsAvoidedVsWindowsBalanced);
        Assert.Equal(35d, recommendation.IdleWattsAvoidedVsWindowsBalanced!.Value, 3);
        Assert.Contains("95%", recommendation.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Recommendation_falls_back_to_efficiency_when_idle_power_is_unavailable()
    {
        var results = new[]
        {
            Result(MachineBaselineMode.BalancedEfficient, "BAL-E", idleWatts: null, maxThroughput: 98, efficiency: 1.5, singleThread: 20),
            Result(MachineBaselineMode.Performance, "PERF", idleWatts: null, maxThroughput: 100, efficiency: 1.1, singleThread: 24)
        };

        var recommendation = MachineBaselineAnalysis.Recommend(results);

        Assert.Equal(MachineBaselineMode.BalancedEfficient, recommendation.RecommendedMode);
        Assert.Null(recommendation.IdleWattsAvoidedVsWindowsBalanced);
    }

    private static MachineBaselineComparisonRun BuildRun(params MachineBaselineModeResult[] results)
        => new(Guid.NewGuid(), DateTimeOffset.UtcNow, MachineBaselineSchedule.Standard, results, MachineBaselineAnalysis.Recommend(results));

    private static MachineBaselineModeResult Result(MachineBaselineMode mode, string label, double? idleWatts, double maxThroughput, double efficiency, double singleThread)
    {
        var profile = new CpuCapabilityProfile(
            DateTimeOffset.UtcNow,
            label,
            label,
            new[]
            {
                new CpuCapabilityPoint(1, singleThread, 50, 100, 4.0),
                new CpuCapabilityPoint(4, maxThroughput * .8, 70, 110, 4.2),
                new CpuCapabilityPoint(16, maxThroughput, maxThroughput / efficiency, 120, 4.3)
            },
            BestEfficiencyWorkers: 16,
            KneeWorkers: 16,
            MaxThroughputWorkers: 16);
        var idle = new MachineIdleSummary(TimeSpan.FromSeconds(90), idleWatts, idleWatts, 100, 4.0, 10, 0, idleWatts is double w ? w * 90 / 3600d : null, 90);
        return new MachineBaselineModeResult(mode, label, label, idle, profile);
    }

    [Fact]
    public void Default_config_has_empty_machine_baseline_history()
    {
        Assert.Empty(PowerFlow.Core.Rules.PowerFlowConfig.Default.EffectiveMachineBaselineRuns);
    }

    [Fact]
    public void Baseline_history_keeps_five_newest_runs()
    {
        var runs = Enumerable.Range(0, 7)
            .Select(index => new MachineBaselineComparisonRun(Guid.NewGuid(), DateTimeOffset.UtcNow.AddMinutes(index), MachineBaselineSchedule.Standard,
                new[] { Result(MachineBaselineMode.BalancedEfficient, $"BAL-E-{index}", 60, 100, 1.2, 20) },
                MachineBaselineAnalysis.Recommend(new[] { Result(MachineBaselineMode.BalancedEfficient, $"BAL-E-{index}", 60, 100, 1.2, 20) })))
            .ToArray();

        IReadOnlyList<MachineBaselineComparisonRun> history = Array.Empty<MachineBaselineComparisonRun>();
        foreach (var run in runs) history = MachineBaselineHistory.Upsert(history, run);

        Assert.Equal(5, history.Count);
        Assert.Equal(runs[^1].Id, history[0].Id);
        Assert.Equal(runs[^5].Id, history[^1].Id);
    }
}
