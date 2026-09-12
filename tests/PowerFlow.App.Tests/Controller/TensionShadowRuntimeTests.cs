using PowerFlow.App.Controller;
using PowerFlow.App.Telemetry;
using PowerFlow.Core.Envelope;
using PowerFlow.Core.Policy;
using PowerFlow.Core.Rules;
using Xunit;

namespace PowerFlow.App.Tests.Controller;

public sealed class TensionShadowRuntimeTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 11, 20, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Evaluate_ProcessesEachActivitySampleOnlyOnce()
    {
        var runtime = new TensionShadowRuntime();
        var snapshot = Snapshot(1, 70, null, false, null);
        var history = RichHistory(30);

        Assert.NotNull(runtime.Evaluate(snapshot, history, PowerFlowConfig.Default, 50));
        Assert.Null(runtime.Evaluate(snapshot, history, PowerFlowConfig.Default, 50));
    }

    [Fact]
    public void Evaluate_ContinuesObservingWhileManualAuthorityIsLatched()
    {
        var runtime = new TensionShadowRuntime();
        var result = runtime.Evaluate(Snapshot(1, 82, null, true, "Manual"), RichHistory(30), PowerFlowConfig.Default, 75);

        Assert.NotNull(result);
        Assert.Equal(75, result!.TensionPercent);
    }

    [Fact]
    public void Evaluate_AppEntitlementCeilingRemainsAuthoritative()
    {
        var entitlement = new PerformanceEntitlement(EnvelopeZone.Efficient, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(5), true);
        var config = PowerFlowConfig.Default with { AppRules = [new AppRule(@"C:\Apps\browser.exe", AppRuleMode.Performance, "Browser", true, entitlement)] };
        var runtime = new TensionShadowRuntime();

        var result = runtime.Evaluate(Snapshot(1, 96, @"C:\Apps\browser.exe", false, null), RichHistory(30), config, 100);

        Assert.NotNull(result);
        Assert.Equal(EnvelopeZone.Efficient, result!.Entitlement.MaximumZone);
        Assert.True(result.Decision.AllowedZone <= EnvelopeZone.Efficient);
        Assert.NotEqual(PowerFlowOperatingMode.Ultra, result.WouldUseMode);
    }

    [Fact]
    public void Evaluate_Tension50MatchesCurrentNeutralEnvelopeAndTiming()
    {
        var snapshot = Snapshot(1, 72, null, false, null);
        var history = RichHistory(30);
        var current = new AdaptiveGovernorRuntime().Evaluate(snapshot, history, PowerFlowConfig.Default);
        var shadow = new TensionShadowRuntime().Evaluate(snapshot, history, PowerFlowConfig.Default, 50);

        Assert.NotNull(current);
        Assert.NotNull(shadow);
        Assert.Equal(current!.EffectiveEnvelope, shadow!.EffectiveEnvelope);
        Assert.Equal(current.Entitlement, shadow.Entitlement);
        Assert.Equal(current.Decision.AllowedZone, shadow.Decision.AllowedZone);
    }

    [Fact]
    public void Evaluate_LowAndHighTensionProduceDifferentGovernanceFromSameTelemetry()
    {
        var snapshot = Snapshot(1, 74, null, false, null);
        var history = RichHistory(30);
        var low = new TensionShadowRuntime().Evaluate(snapshot, history, PowerFlowConfig.Default, 0);
        var high = new TensionShadowRuntime().Evaluate(snapshot, history, PowerFlowConfig.Default, 100);

        Assert.NotNull(low);
        Assert.NotNull(high);
        Assert.True(low!.EffectiveEnvelope.EfficientCeilingPressure > high!.EffectiveEnvelope.EfficientCeilingPressure);
        Assert.True(low.Entitlement.QualificationDuration > high.Entitlement.QualificationDuration);
        Assert.True(low.Entitlement.LeaseDuration < high.Entitlement.LeaseDuration);
        Assert.True(low.Entitlement.ReleaseHysteresis < high.Entitlement.ReleaseHysteresis);
    }

    [Fact]
    public void Source_HasNoActuationDependencies()
    {
        var source = Read("src", "PowerFlow.App", "Controller", "TensionShadowRuntime.cs");

        Assert.DoesNotContain("EnvelopeActuationPolicy", source, StringComparison.Ordinal);
        Assert.DoesNotContain("PowerModeProfileRuntime", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ApplyAdaptiveGovernorDecisionAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("IPowerPlanController", source, StringComparison.Ordinal);
    }

    private static ControllerSnapshot Snapshot(long count, double cpu, string? actor, bool latched, string? latchType) =>
        new(PowerState.PowerSaver, "sample", latched, latchType, cpu, 0, null, actor, T0.AddSeconds(count * 2), [], count, 0);

    private static IReadOnlyList<ContinuitySample> RichHistory(int count) => Enumerable.Range(0, count)
        .Select(i => new ContinuitySample(T0.AddSeconds(i * 5), 25 + (i % 60), 35 + i * 0.8, 1800 + i * 35,
            PowerState.Balanced, "sample", false, null, 0, null, ActiveCores: 8 + (i % 8), TotalCores: 16))
        .ToArray();

    private static string Read(params string[] parts)
    {
        var dir = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(dir, "PowerFlow.sln")))
            dir = Directory.GetParent(dir)?.FullName ?? throw new DirectoryNotFoundException();
        var path = Path.Combine(new[] { dir }.Concat(parts).ToArray());
        Assert.True(File.Exists(path), $"Expected source file {path}");
        return File.ReadAllText(path);
    }
}
