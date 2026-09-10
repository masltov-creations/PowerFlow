namespace PowerFlow.Core.Envelope;

public sealed record EnvelopeCalibrationResult(
    OperatingEnvelope Envelope,
    EnvelopeConfidence Confidence,
    int SampleCount,
    int UsablePowerSampleCount,
    double? SustainedEfficiencyFrontierWatts);
