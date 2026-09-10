using System.Text.Json.Serialization;
using PowerFlow.Core.Envelope;

namespace PowerFlow.Core.Rules;

public enum AppRuleMode { Balanced, Performance }

public sealed record AppRule(
    string ExecutablePath,
    AppRuleMode Mode,
    string? DisplayName = null,
    bool FollowChildren = true,
    PerformanceEntitlement? Entitlement = null)
{
    [JsonIgnore]
    public PerformanceEntitlement EffectiveEntitlement => Entitlement ?? (Mode == AppRuleMode.Performance
        ? PerformanceEntitlement.LegacyPerformance with { FollowChildren = FollowChildren }
        : PerformanceEntitlement.LegacyBalanced with { FollowChildren = FollowChildren });
}
