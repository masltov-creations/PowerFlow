namespace PowerFlow.Core.Envelope;

public sealed record BoostLease(DateTimeOffset GrantedAt, DateTimeOffset ExpiresAt, string? Actor);
