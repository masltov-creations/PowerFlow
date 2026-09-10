using PowerFlow.App.Telemetry;
using PowerFlow.Core.Envelope;
using PowerFlow.Core.Policy;
using PowerFlow.Core.Rules;

namespace PowerFlow.App.Controller;

public sealed record AdaptiveGovernorRuntimeEvaluation(
    GovernorDecision Decision,
    OperatingEnvelope LearnedEnvelope,
    OperatingEnvelope EffectiveEnvelope,
    PerformanceEntitlement Entitlement,
    EnvelopeConfidence Confidence,
    string? Actor);

public sealed class AdaptiveGovernorRuntime
{
    private readonly EnvelopeGovernor _governor = new();
    private long _lastActivitySampleCount;

    public AdaptiveGovernorRuntimeEvaluation? Evaluate(
        ControllerSnapshot snapshot,
        IReadOnlyList<ContinuitySample> history,
        PowerFlowConfig config,
        EnvelopeTuning? tuning = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(config);

        if (snapshot.ActivitySampleCount <= 0 || snapshot.ActivitySampleCount <= _lastActivitySampleCount)
            return null;

        _lastActivitySampleCount = snapshot.ActivitySampleCount;
        if (snapshot.IsLatched)
            return null;

        var raw = history
            .Where(sample => sample.At <= snapshot.At && (sample.PackageWatts is not null || sample.AverageMhz is not null))
            .OrderBy(sample => sample.At)
            .Select(ToObservation)
            .ToArray();
        var calibration = EnvelopeCalibration.Calibrate(raw);
        var settings = config.EffectiveAdaptiveGovernorSettings;
        var learningModel = settings.ResolveLearningModel(calibration);
        var candidate = tuning ?? settings.EffectiveTuning;
        var effectiveEnvelope = candidate.ApplyTo(learningModel.Envelope);
        var actor = string.IsNullOrWhiteSpace(snapshot.TriggerApplication) ? null : snapshot.TriggerApplication;
        var entitlement = candidate.ApplyTo(ResolveEntitlement(config, actor));
        var latestRich = history
            .Where(sample => sample.At <= snapshot.At && (sample.PackageWatts is not null || sample.AverageMhz is not null))
            .OrderByDescending(sample => sample.At)
            .FirstOrDefault();
        var observation = new OperatingObservation(
            snapshot.At,
            snapshot.CpuPercent,
            packageWatts: null,
            effectiveClockMhz: null,
            activeCores: null,
            totalCores: latestRich?.TotalCores,
            ZoneForState(snapshot.State),
            actor,
            EnvelopeDecisionKind.None);
        var decision = _governor.Evaluate(
            observation,
            effectiveEnvelope,
            entitlement,
            snapshot.At,
            learningModel.Confidence,
            candidate.ManualOverrideZone);

        return new AdaptiveGovernorRuntimeEvaluation(
            decision,
            learningModel.Envelope,
            effectiveEnvelope,
            entitlement,
            learningModel.Confidence,
            actor);
    }

    private static OperatingObservation ToObservation(ContinuitySample sample) => new(
        sample.At,
        sample.CpuPercent,
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
