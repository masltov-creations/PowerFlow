using PowerFlow.Core.Envelope;
using PowerFlow.Core.Policy;

namespace PowerFlow.Core.Rules;

public sealed record PowerFlowConfig(
    int SchemaVersion,
    PowerState RestingState,
    double CpuPromotionThresholdPercent,
    TimeSpan CpuPromotionWindow,
    double QuietThresholdPercent,
    TimeSpan QuietWindow,
    TimeSpan PostGameCooldown,
    Guid? PowerSaverPlanId,
    Guid? BalancedPlanId,
    Guid? HighPerformancePlanId,
    IReadOnlyList<AppRule> AppRules,
    bool StartWithWindows,
    bool? ReducedMotionOverride,
    ThemePreference Theme = ThemePreference.System,
    bool AdaptiveActuationEnabled = false,
    bool GraduatedCoreActuationEnabled = false,
    AdaptiveGovernorSettings? AdaptiveGovernor = null)
{
    public AdaptiveGovernorSettings EffectiveAdaptiveGovernorSettings => AdaptiveGovernor ?? AdaptiveGovernorSettings.Default;

    public static PowerFlowConfig Default { get; } = new(
        SchemaVersion: 1,
        RestingState: PowerState.PowerSaver,
        CpuPromotionThresholdPercent: 35,
        CpuPromotionWindow: TimeSpan.FromSeconds(4),
        QuietThresholdPercent: 12,
        QuietWindow: TimeSpan.FromSeconds(25),
        PostGameCooldown: TimeSpan.FromSeconds(8),
        PowerSaverPlanId: null,
        BalancedPlanId: null,
        HighPerformancePlanId: null,
        AppRules: Array.Empty<AppRule>(),
        StartWithWindows: false,
        ReducedMotionOverride: null,
        Theme: ThemePreference.System);
}
