namespace PowerFlow.Core.Envelope;

public sealed record GovernorDecision(
    EnvelopeZone RequestedZone,
    EnvelopeZone AllowedZone,
    EnvelopeDecisionKind Kind,
    double QualificationProgress,
    DateTimeOffset? LeaseExpiresAt,
    string Explanation,
    EnvelopeConfidence Confidence);
