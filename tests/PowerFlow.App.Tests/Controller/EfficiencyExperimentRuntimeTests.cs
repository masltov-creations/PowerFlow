using PowerFlow.App.Controller;
using PowerFlow.App.Telemetry;
using PowerFlow.Core.Policy;
using Xunit;

namespace PowerFlow.App.Tests.Controller;

public sealed class EfficiencyExperimentRuntimeTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 10, 20, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Summarize_IntegratesNominalCpuWorkAndPackageEnergy()
    {
        var samples = Enumerable.Range(0, 7)
            .Select(i => Sample(i * 10, demand: 50, watts: 100, pressure: 60, queue: 1))
            .ToArray();

        var result = EfficiencyComparisonProjection.Summarize(samples);

        Assert.Equal(60, result.ObservedDuration.TotalSeconds, 3);
        Assert.Equal(100, result.CoveragePercent, 3);
        Assert.Equal(.5, result.CpuWorkMinutes, 3);
        Assert.Equal(1.667, result.EnergyWh, 3);
        Assert.Equal(.3, result.WorkPerWh, 3);
        Assert.Equal(100, result.AveragePackageWatts, 3);
        Assert.Equal(60, result.AveragePressurePercent, 3);
        Assert.Equal(1, result.AverageQueueLength, 3);
    }

    [Fact]
    public void Compare_NormalizesEnergyToEquivalentAfterWork()
    {
        var baseline = EfficiencyComparisonProjection.Summarize(Enumerable.Range(0, 7)
            .Select(i => Sample(i * 10, 50, 100, 50, 0)).ToArray());
        var after = EfficiencyComparisonProjection.Summarize(Enumerable.Range(0, 7)
            .Select(i => Sample(i * 10, 50, 60, 45, 0)).ToArray());

        var comparison = EfficiencyComparisonProjection.Compare(baseline, after);

        Assert.Equal(1.667, comparison.EquivalentBaselineEnergyWh!.Value, 3);
        Assert.Equal(.667, comparison.AvoidedEnergyWh!.Value, 3);
        Assert.Equal(40, comparison.AvoidedEnergyPercent!.Value, 2);
        Assert.Equal(66.67, comparison.WorkEfficiencyImprovementPercent!.Value, 2);
        Assert.Equal(0, comparison.WorkRateChangePercent!.Value, 3);
        Assert.Equal(-5, comparison.PressureChangePoints!.Value, 3);
    }

    [Fact]
    public void Summarize_DoesNotIntegrateAcrossLargeTelemetryGap()
    {
        var samples = new[]
        {
            Sample(0, 50, 100, 50, 0),
            Sample(60, 50, 100, 50, 0)
        };

        var result = EfficiencyComparisonProjection.Summarize(samples);

        Assert.Equal(0, result.ObservedDuration.TotalSeconds);
        Assert.Equal(0, result.CpuWorkMinutes);
        Assert.Equal(0, result.EnergyWh);
        Assert.Equal(0, result.CoveragePercent);
    }

    [Fact]
    public void Runtime_CollectsIndependentBaselineAndAfterPhasesAndAutoStops()
    {
        var runtime = new EfficiencyExperimentRuntime();
        runtime.StartBaseline(TimeSpan.FromSeconds(20));
        runtime.Observe(new[] { Sample(0, 40, 90, 45, 0) });
        runtime.Observe(new[] { Sample(0, 40, 90, 45, 0), Sample(10, 40, 90, 45, 0) });
        var baselineDone = runtime.Observe(new[] { Sample(20, 40, 90, 45, 0) });

        Assert.Equal(EfficiencyExperimentPhase.None, baselineDone.ActivePhase);
        Assert.True(baselineDone.BaselineComplete);
        Assert.False(baselineDone.AfterComplete);

        runtime.StartAfter(TimeSpan.FromSeconds(20));
        runtime.Observe(new[] { Sample(30, 40, 70, 42, 0) });
        runtime.Observe(new[] { Sample(40, 40, 70, 42, 0) });
        var afterDone = runtime.Observe(new[] { Sample(50, 40, 70, 42, 0) });

        Assert.Equal(EfficiencyExperimentPhase.None, afterDone.ActivePhase);
        Assert.True(afterDone.BaselineComplete);
        Assert.True(afterDone.AfterComplete);
        Assert.NotNull(afterDone.Comparison.AvoidedEnergyWh);
        Assert.True(afterDone.Comparison.AvoidedEnergyWh > 0);
    }

    [Fact]
    public void Runtime_RejectsDurationsBeyondFiveHours()
    {
        var runtime = new EfficiencyExperimentRuntime();
        Assert.Throws<ArgumentOutOfRangeException>(() => runtime.StartBaseline(TimeSpan.FromHours(5.1)));
    }

    private static ContinuitySample Sample(int seconds, double demand, double watts, double pressure, double queue)
    {
        var telemetry = new DemandPressureTelemetry(
            pressure,
            demand,
            AvailableCapacityPercent: 100,
            CapacitySaturationPercent: pressure,
            QueuePressurePercent: 0,
            QueueLength: queue,
            AwakeCores: 8,
            TotalCores: 8,
            Driver: "DEMAND");
        return new ContinuitySample(
            T0.AddSeconds(seconds),
            CpuPercent: demand,
            PackageWatts: watts,
            AverageMhz: 3400,
            State: PowerState.Balanced,
            Reason: "test",
            IsLatched: false,
            LatchType: null,
            ThresholdProgress: 0,
            TriggerApplication: null,
            ActiveCores: 8,
            TotalCores: 8,
            LogicalProcessors: null,
            ProcessorQueueLength: queue,
            DemandPressure: telemetry,
            ProcessorPerformancePercent: 100);
    }
}