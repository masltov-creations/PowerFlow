using PowerFlow.App.Controller;
using PowerFlow.App.Dashboard;
using PowerFlow.App.Telemetry;
using PowerFlow.Core.Policy;
using Xunit;

namespace PowerFlow.App.Tests.Dashboard;

public sealed class CpuPerformanceHistoryPropagationTests
{
    [Fact]
    public void UpdateContinuity_PreservesProcessorPerformanceIntoTimelinePoints()
    {
        var vm = new DashboardViewModel();
        var at = new DateTimeOffset(2026, 9, 10, 12, 0, 0, TimeSpan.Zero);
        var snapshot = new ControllerSnapshot(
            PowerState.Balanced, "test", false, null, 20, 0, null, null,
            at, Array.Empty<TransitionRecord>(), 0, 0);
        var continuity = new[]
        {
            new ContinuitySample(at, 20, 60, 4200, PowerState.Balanced, "test", false, null, 0, null,
                ActiveCores: 8, TotalCores: 16, ProcessorPerformancePercent: 123),
            new ContinuitySample(at.AddSeconds(1), 25, 62, 4300, PowerState.Balanced, "test", false, null, 0, null,
                ActiveCores: 8, TotalCores: 16, ProcessorPerformancePercent: 126),
        };

        vm.UpdateContinuity(snapshot, continuity, telemetry: null);

        Assert.Equal(new double?[] { 123, 126 }, vm.OperatingHistory.Select(x => x.ProcessorPerformancePercent).ToArray());
        var timeline = PerformanceTimelineProjection.Build(vm.OperatingHistory, 60, PerformanceTimelineMode.Stacked);
        var lane = Assert.Single(timeline.Lanes, x => x.Metric == PerformanceTimelineMetric.EffectiveClock);
        Assert.Equal(2, lane.Points.Count);
        Assert.Equal(123, lane.Points[0].Value);
        Assert.Equal(126, lane.Points[1].Value);
    }

    [Fact]
    public void FromDashboardSample_PreservesProcessorPerformance()
    {
        var sample = new DashboardSample(DateTimeOffset.UnixEpoch, 10, 40, 4000, PowerState.Balanced,
            ActiveCores: 4, TotalCores: 8, PressurePercent: 25, ProcessorPerformancePercent: 121.5);

        var observation = OperatingObservationProjection.FromDashboardSample(sample);

        Assert.Equal(121.5, observation.ProcessorPerformancePercent);
    }
}