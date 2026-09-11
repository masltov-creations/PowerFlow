using System.Text.Json.Serialization;
using PowerFlow.Core.Envelope;

namespace PowerFlow.Core.Rules;

public enum AppRuleMode { Balanced, Performance }
public enum AppImportance { Low, Normal, High }

public sealed record AppRule(
    string ExecutablePath,
    AppRuleMode Mode,
    string? DisplayName = null,
    bool FollowChildren = true,
    PerformanceEntitlement? Entitlement = null,
    AppImportance? Importance = null)
{
    [JsonIgnore]
    public AppImportance EffectiveImportance => Entitlement is not null
        ? InferImportance(Entitlement)
        : Importance ?? (Mode == AppRuleMode.Balanced ? AppImportance.Low : AppImportance.Normal);

    [JsonIgnore]
    public PerformanceEntitlement EffectiveEntitlement => Entitlement
        ?? (Importance is AppImportance importance
            ? EntitlementFor(importance, FollowChildren)
            : Mode == AppRuleMode.Performance
                ? PerformanceEntitlement.LegacyPerformance with { FollowChildren = FollowChildren }
                : PerformanceEntitlement.LegacyBalanced with { FollowChildren = FollowChildren });

    public static PerformanceEntitlement EntitlementFor(AppImportance importance, bool followChildren = true) => importance switch
    {
        AppImportance.Low => PerformanceEntitlement.LegacyBalanced with { FollowChildren = followChildren },
        AppImportance.Normal => PerformanceEntitlement.LegacyPerformance with { FollowChildren = followChildren },
        AppImportance.High => new PerformanceEntitlement(
            EnvelopeZone.Boost,
            TimeSpan.FromSeconds(.5),
            TimeSpan.FromSeconds(15),
            TimeSpan.FromSeconds(8),
            followChildren),
        _ => PerformanceEntitlement.LegacyPerformance with { FollowChildren = followChildren }
    };

    private static AppImportance InferImportance(PerformanceEntitlement entitlement)
    {
        if (entitlement.MaximumZone <= EnvelopeZone.Efficient) return AppImportance.Low;
        if (entitlement.MaximumZone == EnvelopeZone.Boost && entitlement.QualificationDuration <= TimeSpan.FromSeconds(1)) return AppImportance.High;
        return AppImportance.Normal;
    }
}