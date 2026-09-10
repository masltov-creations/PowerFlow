using PowerFlow.App.Controller;
using PowerFlow.App.Telemetry;
using PowerFlow.Core.Envelope;
using PowerFlow.Core.Policy;
using PowerFlow.Core.Rules;
using Xunit;

namespace PowerFlow.App.Tests.Controller;

public sealed class AdaptiveGovernorRuntimeTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 10, 4, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Evaluate_ProcessesEachActivitySampleCountOnlyOnce()
    {
        var runtime = new AdaptiveGovernorRuntime();
        var snapshot = Snapshot(1, 70, null);
        var history = RichHistory(30);
        Assert.NotNull(runtime.Evaluate(snapshot, history, PowerFlowConfig.Default));
        Assert.Null(runtime.Evaluate(snapshot, history, PowerFlowConfig.Default));
    }

    [Fact]
    public void Evaluate_UnknownActorUsesSystemDefaultWithoutInventingAttribution()
    {
        var runtime = new AdaptiveGovernorRuntime();
        var result = runtime.Evaluate(Snapshot(1, 62, null), RichHistory(30), PowerFlowConfig.Default);
        Assert.NotNull(result);
        Assert.Null(result!.Actor);
        Assert.Equal(PerformanceEntitlement.LegacyPerformance.MaximumZone, result.Entitlement.MaximumZone);
    }

    [Fact]
    public void Evaluate_UsesTruthfulActorRuleWhenSnapshotCarriesActor()
    {
        var entitlement = new PerformanceEntitlement(EnvelopeZone.Efficient, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(5), true);
        var config = PowerFlowConfig.Default with { AppRules = [new AppRule(@"C:\Apps\browser.exe", AppRuleMode.Performance, "Browser", true, entitlement)] };
        var runtime = new AdaptiveGovernorRuntime();
        var result = runtime.Evaluate(Snapshot(1, 92, @"C:\Apps\browser.exe"), RichHistory(30), config);
        Assert.NotNull(result);
        Assert.Equal(@"C:\Apps\browser.exe", result!.Actor);
        Assert.Equal(EnvelopeZone.Efficient, result.Entitlement.MaximumZone);
        Assert.Equal(EnvelopeDecisionKind.Brake, result.Decision.Kind);
        Assert.Equal(EnvelopeZone.Efficient, result.Decision.AllowedZone);
    }

    [Fact]
    public void Evaluate_AppliesMachineTuningAndManualOverrideToSameGovernorDecision()
    {
        var tuning = new EnvelopeTuning(EnvelopeTuningLayer.Override, 15, 35, 55, manualOverrideZone: EnvelopeZone.Efficient);
        var runtime = new AdaptiveGovernorRuntime();
        var result = runtime.Evaluate(Snapshot(1, 95, null), RichHistory(30), PowerFlowConfig.Default, tuning);
        Assert.NotNull(result);
        Assert.Equal(15, result!.EffectiveEnvelope.EcoCeilingPressure);
        Assert.Equal(35, result.EffectiveEnvelope.EfficientCeilingPressure);
        Assert.Equal(55, result.EffectiveEnvelope.ResponsiveCeilingPressure);
        Assert.Equal(EnvelopeZone.Efficient, result.Decision.AllowedZone);
        Assert.Contains("override", result.Decision.Explanation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AppSource_WiresRuntimeDecisionIntoExistingGuardedControllerBridge()
    {
        var code = Read("src", "PowerFlow.App", "App.xaml.cs");
        Assert.Contains("AdaptiveGovernorRuntime", code, StringComparison.Ordinal);
        Assert.Contains("ApplyAdaptiveGovernorDecisionAsync", code, StringComparison.Ordinal);
    }

    private static ControllerSnapshot Snapshot(long count, double cpu, string? actor) =>
        new(PowerState.PowerSaver, "sample", false, null, cpu, 0, null, actor, T0.AddSeconds(count * 2), [], count, 0);

    private static IReadOnlyList<ContinuitySample> RichHistory(int count) => Enumerable.Range(0, count)
        .Select(i => new ContinuitySample(T0.AddSeconds(i * 5), 25 + (i % 60), 35 + i * 0.8, 1800 + i * 35,
            PowerState.Balanced, "sample", false, null, 0, null, ActiveCores: null, TotalCores: 32))
        .ToArray();

    private static string Read(params string[] parts)
    {
        var dir = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(dir, "PowerFlow.sln")))
            dir = Directory.GetParent(dir)?.FullName ?? throw new DirectoryNotFoundException();
        return File.ReadAllText(Path.Combine(new[] { dir }.Concat(parts).ToArray()));
    }
}
