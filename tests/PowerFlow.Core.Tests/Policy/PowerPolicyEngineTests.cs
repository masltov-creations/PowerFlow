using Xunit;
using PowerFlow.Core.Policy;

namespace PowerFlow.Core.Tests.Policy;

public sealed class PowerPolicyEngineTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);

    private static PowerPolicyEngine NewEngine(PowerState resting = PowerState.PowerSaver) =>
        new(new PolicyConfig(
            RestingState: resting,
            CpuPromotionThresholdPercent: 35,
            CpuPromotionWindow: TimeSpan.FromSeconds(4),
            QuietThresholdPercent: 12,
            QuietWindow: TimeSpan.FromSeconds(25),
            PostGameCooldown: TimeSpan.FromSeconds(8)));

    [Fact]
    public void ShortCpuSpike_DoesNotLeaveRestingState()
    {
        var engine = NewEngine();
        engine.Evaluate(new CpuSample(T0, 80));
        var result = engine.Evaluate(new CpuSample(T0.AddSeconds(2), 5));
        Assert.Equal(PowerState.PowerSaver, result.Target);
        Assert.False(result.Changed);
    }

    [Fact]
    public void SustainedCpuDemand_PromotesToBalancedAfterThresholdWindow()
    {
        var engine = NewEngine();
        engine.Evaluate(new CpuSample(T0, 60));
        engine.Evaluate(new CpuSample(T0.AddSeconds(2), 60));
        var result = engine.Evaluate(new CpuSample(T0.AddSeconds(4), 60));
        Assert.Equal(PowerState.Balanced, result.Target);
        Assert.True(result.Changed);
        Assert.Contains("sustained CPU", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BalancedQuietPeriod_DemotesOnlyAfterHysteresis()
    {
        var engine = NewEngine();
        engine.Evaluate(new CpuSample(T0, 60));
        engine.Evaluate(new CpuSample(T0.AddSeconds(4), 60));
        var before = engine.Evaluate(new CpuSample(T0.AddSeconds(20), 2));
        var still = engine.Evaluate(new CpuSample(T0.AddSeconds(44), 2));
        var after = engine.Evaluate(new CpuSample(T0.AddSeconds(45), 2));
        Assert.Equal(PowerState.Balanced, before.Target);
        Assert.Equal(PowerState.Balanced, still.Target);
        Assert.Equal(PowerState.PowerSaver, after.Target);
    }

    [Fact]
    public void GameStart_JumpsDirectlyToPerformanceAndLatches()
    {
        var engine = NewEngine();
        var result = engine.Evaluate(new GameStarted(T0, "game:42"));
        Assert.Equal(PowerState.HighPerformance, result.Target);
        Assert.True(result.IsLatched);
        Assert.Contains("game", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GameTelemetryDroppingToZero_DoesNotReleaseLatch()
    {
        var engine = NewEngine();
        engine.Evaluate(new GameStarted(T0, "game:42"));
        var result = engine.Evaluate(new CpuSample(T0.AddMinutes(5), 0));
        Assert.Equal(PowerState.HighPerformance, result.Target);
        Assert.True(result.IsLatched);
    }

    [Fact]
    public void GameExit_EntersCooldownThenBalanced()
    {
        var engine = NewEngine();
        engine.Evaluate(new GameStarted(T0, "game:42"));
        var cooling = engine.Evaluate(new GameExited(T0.AddMinutes(1), "game:42", AllTrackedGameProcessesExited: true));
        Assert.Equal(PowerState.HighPerformance, cooling.Target);
        Assert.False(cooling.IsLatched);
        Assert.Contains("cooldown", cooling.Reason, StringComparison.OrdinalIgnoreCase);

        var result = engine.Evaluate(new CooldownExpired(T0.AddMinutes(1).AddSeconds(8)));
        Assert.Equal(PowerState.Balanced, result.Target);
        Assert.True(result.Changed);
    }

    [Fact]
    public void ManualPerformance_RemainsLatchedUntilExplicitRelease()
    {
        var engine = NewEngine();
        engine.Evaluate(new ManualPerformanceRequested(T0));
        var idle = engine.Evaluate(new CpuSample(T0.AddHours(1), 0));
        Assert.Equal(PowerState.HighPerformance, idle.Target);
        Assert.True(idle.IsLatched);

        var released = engine.Evaluate(new ManualPerformanceReleased(T0.AddHours(1).AddSeconds(1)));
        Assert.Equal(PowerState.Balanced, released.Target);
        Assert.False(released.IsLatched);
    }

    [Fact]
    public void ManualLatch_OutranksGameAndCpuRules()
    {
        var engine = NewEngine();
        engine.Evaluate(new GameStarted(T0, "game:42"));
        engine.Evaluate(new ManualPerformanceRequested(T0.AddSeconds(1)));
        engine.Evaluate(new GameExited(T0.AddSeconds(2), "game:42", AllTrackedGameProcessesExited: true));
        var result = engine.Evaluate(new CpuSample(T0.AddMinutes(10), 0));
        Assert.Equal(PowerState.HighPerformance, result.Target);
        Assert.True(result.IsLatched);
        Assert.Contains("manual", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ExplicitBalancedRule_OutranksCpuQuietDemotion()
    {
        var engine = NewEngine();
        engine.Evaluate(new ExplicitBalancedActivated(T0, "render.exe"));
        engine.Evaluate(new CpuSample(T0.AddSeconds(1), 0));
        var result = engine.Evaluate(new CpuSample(T0.AddMinutes(2), 0));
        Assert.Equal(PowerState.Balanced, result.Target);
        Assert.Contains("render.exe", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BalancedRestingStateFlag_PreventsPowerSaverDemotion()
    {
        var engine = NewEngine(PowerState.Balanced);
        engine.Evaluate(new CpuSample(T0, 0));
        var result = engine.Evaluate(new CpuSample(T0.AddMinutes(2), 0));
        Assert.Equal(PowerState.Balanced, result.Target);
    }
}


