namespace PowerFlow.Core.Envelope;

public sealed record PerformanceEntitlement
{
    public PerformanceEntitlement(
        EnvelopeZone maximumZone,
        TimeSpan qualificationDuration,
        TimeSpan leaseDuration,
        TimeSpan releaseHysteresis,
        bool followChildren)
    {
        if (qualificationDuration < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(qualificationDuration));
        if (leaseDuration <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(leaseDuration));
        if (releaseHysteresis < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(releaseHysteresis));
        MaximumZone = maximumZone;
        QualificationDuration = qualificationDuration;
        LeaseDuration = leaseDuration;
        ReleaseHysteresis = releaseHysteresis;
        FollowChildren = followChildren;
    }

    public EnvelopeZone MaximumZone { get; init; }
    public TimeSpan QualificationDuration { get; init; }
    public TimeSpan LeaseDuration { get; init; }
    public TimeSpan ReleaseHysteresis { get; init; }
    public bool FollowChildren { get; init; }

    public static PerformanceEntitlement LegacyBalanced { get; } = new(
        EnvelopeZone.Efficient,
        TimeSpan.FromSeconds(4),
        TimeSpan.FromSeconds(12),
        TimeSpan.FromSeconds(10),
        true);

    public static PerformanceEntitlement LegacyPerformance { get; } = new(
        EnvelopeZone.Boost,
        TimeSpan.FromSeconds(4),
        TimeSpan.FromSeconds(12),
        TimeSpan.FromSeconds(10),
        true);
}
