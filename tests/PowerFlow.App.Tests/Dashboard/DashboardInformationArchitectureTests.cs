using PowerFlow.App.Controller;
using PowerFlow.App.Dashboard;
using PowerFlow.Core.Policy;
using PowerFlow.Core.Rules;
using PowerFlow.Windows.Activity;
using Xunit;

namespace PowerFlow.App.Tests.Dashboard;

public sealed class DashboardInformationArchitectureTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 8, 20, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ViewModel_ExposesSixtySecondCpuPowerHistoryAndConfiguredThresholds()
    {
        var vm = new DashboardViewModel();
        vm.Configure(PowerFlowConfig.Default);

        for (var i = 0; i < 65; i++)
        {
            var at = T0.AddSeconds(i);
            var snapshot = Snapshot(PowerState.Balanced, 20 + (i % 15), at);
            vm.Update(snapshot, new DashboardTelemetry(45 + i * 0.25, 3900 + i, at));
        }

        Assert.Equal(60, vm.Samples.Count);
        Assert.Equal(T0.AddSeconds(5), vm.Samples[0].At);
        Assert.Equal(T0.AddSeconds(64), vm.Samples[^1].At);
        Assert.Equal(35, vm.PromotionThresholdPercent);
        Assert.Equal(12, vm.QuietThresholdPercent);
        Assert.Equal("CPU > 35% for 4s", vm.PromotionRuleLabel);
        Assert.Equal("CPU < 12% for 25s", vm.QuietRuleLabel);
    }

    [Fact]
    public void RuleProjection_MakesAutomaticRulesGraphicalAndLive()
    {
        var snapshot = Snapshot(PowerState.PowerSaver, 42, T0) with { ThresholdProgress = 1.2 };

        var rules = DashboardRuleProjection.Create(PowerFlowConfig.Default, snapshot);

        Assert.Collection(rules,
            promote =>
            {
                Assert.Equal("SUSTAINED CPU", promote.Title);
                Assert.Equal("CPU > 35% for 4s", promote.Condition);
                Assert.Equal("BALANCED", promote.Target);
                Assert.Equal("ACTIVE", promote.Status);
            },
            quiet =>
            {
                Assert.Equal("QUIET RETURN", quiet.Title);
                Assert.Equal("CPU < 12% for 25s", quiet.Condition);
                Assert.Equal("POWER SAVER", quiet.Target);
                Assert.Equal("STANDBY", quiet.Status);
            },
            game =>
            {
                Assert.Equal("GAME / APP", game.Title);
                Assert.Equal("Process lifecycle match", game.Condition);
                Assert.Equal("PERFORMANCE LOCK", game.Target);
                Assert.Equal("ARMED", game.Status);
            });
    }

    [Fact]
    public void DecisionMeter_LabelsMeaningAndThresholdMarkers()
    {
        var snapshot = Snapshot(PowerState.PowerSaver, 28, T0);

        var meter = DecisionMeterProjection.Create(PowerFlowConfig.Default, snapshot);

        Assert.Equal("CPU PRESSURE", meter.Title);
        Assert.Equal(28, meter.ValuePercent);
        Assert.Equal(12, meter.QuietMarkerPercent);
        Assert.Equal(35, meter.PromotionMarkerPercent);
        Assert.Contains("Balanced at 35%", meter.Explanation);
    }

    [Fact]
    public void TelemetryTimeAxis_AnchorsPartialHistoryAtTheRightEdge()
    {
        var first = T0;
        var last = T0.AddSeconds(9);

        Assert.InRange(TelemetryPlotProjection.NormalizedX(first, last), 0.84, 0.86);
        Assert.Equal(1, TelemetryPlotProjection.NormalizedX(last, last), 3);
    }
    [Fact]
    public void DecisionMeter_GameLatchExplainsThatUtilizationCannotDemote()
    {
        var snapshot = Snapshot(PowerState.HighPerformance, 3, T0) with { IsLatched = true, LatchType = "Game", TriggerApplication = "game.exe" };

        var meter = DecisionMeterProjection.Create(PowerFlowConfig.Default, snapshot);

        Assert.Equal("PERFORMANCE LATCH", meter.Title);
        Assert.Contains("utilization is ignored", meter.Explanation, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("game.exe", meter.Explanation, StringComparison.OrdinalIgnoreCase);
    }

    private static ControllerSnapshot Snapshot(PowerState state, double cpu, DateTimeOffset at) =>
        new(state, "test", false, null, cpu, 0, null, null, at, [], 0, 0);
}
