using PowerFlow.App.Telemetry;
using PowerFlow.Core.Envelope;
using PowerFlow.Core.Policy;
using PowerFlow.Core.Rules;

namespace PowerFlow.App.Controller;

public sealed record TensionShadowEvaluation(
    double TensionPercent,
    GovernorTensionReference Reference,
    GovernorDecision Decision,
    OperatingEnvelope LearnedEnvelope,
    OperatingEnvelope EffectiveEnvelope,
    PerformanceEntitlement Entitlement,
    EnvelopeConfidence Confidence,
    string? Actor,
    PowerFlowOperatingMode WouldUseMode);

public sealed class TensionShadowRuntime
{
    private readonly EnvelopeGovernor _governor = new();
    private long _lastActivitySampleCount;

    public TensionShadowEvaluation? Evaluate(
        ControllerSnapshot snapshot,
        IReadOnlyList<ContinuitySample> history,
        PowerFlowConfig config,
        double tensionPercent)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(config);

        if (snapshot.ActivitySampleCount <= 0 || snapshot.ActivitySampleCount <= _lastActivitySampleCount)
            return null;

        _lastActivitySampleCount = snapshot.ActivitySampleCount;

        var raw = history
            .Where(sample => sample.At <= snapshot.At && (sample.PackageWatts is not null || sample.AverageMhz is not null))
            .OrderBy(sample => sample.At)
            .Select(ToObservation)
            .ToArray();
        var calibration = EnvelopeCalibration.Calibrate(raw);
        var learningModel = config.EffectiveAdaptiveGovernorSettings.ResolveLearningModel(calibration);
        var actor = string.IsNullOrWhiteSpace(snapshot.TriggerApplication) ? null : snapshot.TriggerApplication;
        var baseEntitlement = ResolveEntitlement(config, actor);
        var tension = GovernorTensionPolicy.Resolve(tensionPercent, learningModel.Envelope, baseEntitlement);
        var latestRich = history
            .Where(sample => sample.At <= snapshot.At && (sample.PackageWatts is not null || sample.AverageMhz is not null))
            .OrderByDescending(sample => sample.At)
            .FirstOrDefault();
        var observation = new OperatingObservation(
            snapshot.At,
            GovernorPressureProjection.Current(snapshot, latestRich),
            packageWatts: null,
            effectiveClockMhz: null,
            activeCores: null,
            totalCores: latestRich?.TotalCores,
            ZoneForState(snapshot.State),
            actor,
            EnvelopeDecisionKind.None);
        var decision = _governor.Evaluate(
            observation,
            tension.EffectiveEnvelope,
            tension.EffectiveEntitlement,
            snapshot.At,
            learningModel.Confidence);
        var wouldUseMode = PowerFlowOperatingProfiles.ForAutoZone(decision.AllowedZone).Mode;

        return new TensionShadowEvaluation(
            tension.TensionPercent,
            tension.Reference,
            decision,
            learningModel.Envelope,
            tension.EffectiveEnvelope,
            tension.EffectiveEntitlement,
            learningModel.Confidence,
            actor,
            wouldUseMode);
    }

    private static OperatingObservation ToObservation(ContinuitySample sample) => new(
        sample.At,
        GovernorPressureProjection.FromSample(sample),
        sample.PackageWatts,
        sample.AverageMhz,
        sample.ActiveCores,
        sample.TotalCores,
        ZoneForState(sample.State),
        sample.TriggerApplication,
        EnvelopeDecisionKind.None);

    private static PerformanceEntitlement ResolveEntitlement(PowerFlowConfig config, string? actor)
    {
        if (string.IsNullOrWhiteSpace(actor)) return PerformanceEntitlement.LegacyPerformance;
        var rule = config.AppRules.FirstOrDefault(candidate => ActorMatches(candidate.ExecutablePath, actor));
        return rule?.EffectiveEntitlement ?? PerformanceEntitlement.LegacyPerformance;
    }

    private static bool ActorMatches(string executablePath, string actor)
    {
        if (string.Equals(executablePath, actor, StringComparison.OrdinalIgnoreCase)) return true;
        try { return string.Equals(Path.GetFileName(executablePath), Path.GetFileName(actor), StringComparison.OrdinalIgnoreCase); }
        catch { return false; }
    }

    private static EnvelopeZone ZoneForState(PowerState state) => state switch
    {
        PowerState.PowerSaver => EnvelopeZone.Eco,
        PowerState.Balanced => EnvelopeZone.Efficient,
        PowerState.HighPerformance => EnvelopeZone.Boost,
        _ => EnvelopeZone.Efficient
    };
}
