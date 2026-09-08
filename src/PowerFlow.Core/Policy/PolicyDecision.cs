namespace PowerFlow.Core.Policy;

public sealed record PolicyDecision(
    PowerState Target,
    bool Changed,
    string Reason,
    bool IsLatched,
    DateTimeOffset DecidedAt);
