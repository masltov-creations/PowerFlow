namespace PowerFlow.Core.Envelope;

public sealed record AdaptiveLearningModel(
    OperatingEnvelope Envelope,
    EnvelopeConfidence Confidence,
    bool IsFrozen);

public sealed record AdaptiveGovernorSettings(
    EnvelopeTuning? Tuning,
    bool LearningPaused,
    OperatingEnvelope? FrozenLearnedEnvelope,
    EnvelopeConfidence? FrozenConfidence)
{
    public static AdaptiveGovernorSettings Default { get; } = new(
        EnvelopeTuning.Learned,
        LearningPaused: false,
        FrozenLearnedEnvelope: null,
        FrozenConfidence: null);

    public EnvelopeTuning EffectiveTuning => Tuning ?? EnvelopeTuning.Learned;

    public AdaptiveLearningModel ResolveLearningModel(EnvelopeCalibrationResult liveCalibration)
    {
        ArgumentNullException.ThrowIfNull(liveCalibration);
        if (!LearningPaused)
            return new AdaptiveLearningModel(liveCalibration.Envelope, liveCalibration.Confidence, IsFrozen: false);

        if (FrozenLearnedEnvelope is null || FrozenConfidence is null)
            throw new InvalidOperationException("Paused learning requires a frozen learned envelope and confidence.");

        return new AdaptiveLearningModel(FrozenLearnedEnvelope, FrozenConfidence.Value, IsFrozen: true);
    }
}
