using PowerFlow.App.Controller;
using PowerFlow.Core.Envelope;
using PowerFlow.Core.Profiling;

namespace PowerFlow.App.Dashboard;

public sealed record GovernorComparisonView(
    string CurrentStateText,
    string CurrentDetailText,
    string ShadowStateText,
    string ShadowDetailText,
    string ReferenceText,
    string ScaleBehaviorText,
    string SustainBehaviorText,
    string SettleBehaviorText,
    string CurrentEvidenceText,
    string ShadowEvidenceText);

public static class GovernorComparisonProjection
{
    public static GovernorComparisonView Create(
        GovernorDecision? currentDecision,
        PowerFlowOperatingMode? appliedProfile,
        bool manualAuthority,
        TensionShadowEvaluation? shadow,
        MachineBaselineComparisonRun? baseline)
    {
        var authority = manualAuthority ? "MANUAL" : "AUTO";
        var currentState = appliedProfile is PowerFlowOperatingMode profile
            ? $"APPLIED · {authority} / {ProfileLabel(profile)}"
            : $"APPLIED · {authority} / OBSERVING";
        var currentDetail = currentDecision is null
            ? "MODEL OBSERVING"
            : $"MODEL {ZoneLabel(currentDecision.AllowedZone)}";
        var currentEvidence = appliedProfile is PowerFlowOperatingMode currentMode
            ? EvidenceFor(currentMode, baseline)
            : "ESTIMATE · no applied-profile baseline evidence";

        if (shadow is null)
        {
            return new GovernorComparisonView(
                currentState,
                currentDetail,
                "SHADOW · OBSERVING",
                "WOULD USE · waiting for model evidence",
                "BAL-E ↔ BAL-P",
                "Waiting for shadow telemetry",
                "Waiting for shadow telemetry",
                "Waiting for shadow telemetry",
                currentEvidence,
                "ESTIMATE · shadow profile not yet resolved");
        }

        var shadowState = $"SHADOW · T{shadow.TensionPercent:0.#}";
        var shadowDetail = $"WOULD USE {ProfileLabel(shadow.WouldUseMode)} · {ZoneLabel(shadow.Decision.AllowedZone)}";
        var behavior = Behavior(shadow.TensionPercent);
        return new GovernorComparisonView(
            currentState,
            currentDetail,
            shadowState,
            shadowDetail,
            ReferenceLabel(shadow.Reference),
            behavior.Scale,
            behavior.Sustain,
            behavior.Settle,
            currentEvidence,
            EvidenceFor(shadow.WouldUseMode, baseline));
    }

    private static (string Scale, string Sustain, string Settle) Behavior(double tension)
    {
        if (tension < 35d)
            return ("Scales later and qualifies longer", "shorter sustain at elevated performance", "settles sooner toward efficiency");
        if (tension > 65d)
            return ("Scales sooner and qualifies faster", "longer sustain at elevated performance", "settles later toward efficiency");
        return ("Near current governor scale timing", "current sustain behavior", "current settle behavior");
    }

    private static string EvidenceFor(PowerFlowOperatingMode mode, MachineBaselineComparisonRun? baseline)
    {
        if (baseline is null) return "ESTIMATE · no measured profile evidence";
        var baselineMode = mode switch
        {
            PowerFlowOperatingMode.Saver => MachineBaselineMode.PowerFlowSaver,
            PowerFlowOperatingMode.Balanced => MachineBaselineMode.BalancedEfficient,
            PowerFlowOperatingMode.BalancedPerformance => MachineBaselineMode.BalancedPerformance,
            PowerFlowOperatingMode.Performance => MachineBaselineMode.Performance,
            PowerFlowOperatingMode.Ultra => MachineBaselineMode.Ultra,
            _ => MachineBaselineMode.BalancedEfficient
        };
        var result = baseline.Results.FirstOrDefault(candidate => candidate.Mode == baselineMode);
        if (result is null) return "ESTIMATE · no measured profile evidence";
        var idle = result.Idle.AveragePackageWatts;
        var throughput = result.MaxThroughputMops;
        if (idle is double watts && throughput > 0)
            return $"MEASURED · {watts:0.0} W idle · {throughput:0} Mops max";
        if (idle is double idleOnly)
            return $"MEASURED · {idleOnly:0.0} W idle";
        if (throughput > 0)
            return $"MEASURED · {throughput:0} Mops max";
        return "MEASURED · profile captured; power/throughput unavailable";
    }

    public static string ProfileLabel(PowerFlowOperatingMode mode) => mode switch
    {
        PowerFlowOperatingMode.Saver => "SAVER",
        PowerFlowOperatingMode.Balanced => "BAL-E",
        PowerFlowOperatingMode.BalancedPerformance => "BAL-P",
        PowerFlowOperatingMode.Performance => "PERF",
        PowerFlowOperatingMode.Ultra => "ULTRA",
        _ => "BAL-E"
    };

    private static string ZoneLabel(EnvelopeZone zone) => zone.ToString().ToUpperInvariant();

    private static string ReferenceLabel(GovernorTensionReference reference) => reference switch
    {
        GovernorTensionReference.SaverBias => "SAVER BIAS",
        GovernorTensionReference.BalancedEfficient => "BAL-E",
        GovernorTensionReference.BalancedPerformance => "BAL-P",
        GovernorTensionReference.PerformanceBias => "PERF BIAS",
        _ => "BAL-E ↔ BAL-P"
    };
}
