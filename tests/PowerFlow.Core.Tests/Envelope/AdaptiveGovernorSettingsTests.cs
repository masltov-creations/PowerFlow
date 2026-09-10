using PowerFlow.Core.Envelope;
using Xunit;

namespace PowerFlow.Core.Tests.Envelope;

public sealed class AdaptiveGovernorSettingsTests
{
    [Fact]
    public void DefaultSettings_AreLearnedAndLearningIsActive()
    {
        var settings = AdaptiveGovernorSettings.Default;
        Assert.False(settings.LearningPaused);
        Assert.Equal(EnvelopeTuningLayer.Learned, settings.EffectiveTuning.Layer);
        Assert.Null(settings.FrozenLearnedEnvelope);
        Assert.Null(settings.FrozenConfidence);
    }

    [Fact]
    public void ActiveLearning_UsesLiveCalibration()
    {
        var live = Calibration(new OperatingEnvelope(20, 50, 80, 30, 55, 90), EnvelopeConfidence.Medium);
        var resolved = AdaptiveGovernorSettings.Default.ResolveLearningModel(live);
        Assert.Same(live.Envelope, resolved.Envelope);
        Assert.Equal(EnvelopeConfidence.Medium, resolved.Confidence);
        Assert.False(resolved.IsFrozen);
    }

    [Fact]
    public void PausedLearning_UsesExactFrozenEnvelopeAndConfidence()
    {
        var frozen = new OperatingEnvelope(15, 42, 67, 25, 48, 78);
        var settings = new AdaptiveGovernorSettings(EnvelopeTuning.Learned, true, frozen, EnvelopeConfidence.High);
        var live = Calibration(new OperatingEnvelope(25, 60, 90, 40, 70, 110), EnvelopeConfidence.Low);
        var resolved = settings.ResolveLearningModel(live);
        Assert.Same(frozen, resolved.Envelope);
        Assert.Equal(EnvelopeConfidence.High, resolved.Confidence);
        Assert.True(resolved.IsFrozen);
    }

    private static EnvelopeCalibrationResult Calibration(OperatingEnvelope envelope, EnvelopeConfidence confidence) =>
        new(envelope, confidence, 100, 80, envelope.EfficientPowerFrontierWatts);
}
