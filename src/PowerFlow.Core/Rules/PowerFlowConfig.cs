using PowerFlow.Core.Envelope;
using PowerFlow.Core.Policy;
using PowerFlow.Core.Profiling;

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
    bool AdaptiveActuationEnabled = true,
    bool GraduatedCoreActuationEnabled = false,
    AdaptiveGovernorSettings? AdaptiveGovernor = null,
    int TelemetryVisibleIntervalMs = 1000,
    int TelemetryBackgroundIntervalMs = 5000,
    IReadOnlyList<ServicePolicyRule>? ServiceRules = null,
    IReadOnlyList<CpuCapabilityProfile>? CpuCapabilityProfiles = null,
    IReadOnlyList<MachineBaselineComparisonRun>? MachineBaselineRuns = null,
    double? GovernorTensionPercent = null)
{
    public AdaptiveGovernorSettings EffectiveAdaptiveGovernorSettings => AdaptiveGovernor ?? AdaptiveGovernorSettings.Default;
    public TimeSpan EffectiveTelemetryVisibleInterval => TimeSpan.FromMilliseconds(Math.Clamp(TelemetryVisibleIntervalMs, 250, 5000));
    public TimeSpan EffectiveTelemetryBackgroundInterval => TimeSpan.FromMilliseconds(Math.Clamp(TelemetryBackgroundIntervalMs, 500, 10000));
    public IReadOnlyList<ServicePolicyRule> EffectiveServiceRules => ServiceRules ?? Array.Empty<ServicePolicyRule>();
    public IReadOnlyList<CpuCapabilityProfile> EffectiveCpuCapabilityProfiles => CpuCapabilityProfiles ?? Array.Empty<CpuCapabilityProfile>();
    public IReadOnlyList<MachineBaselineComparisonRun> EffectiveMachineBaselineRuns => MachineBaselineRuns ?? Array.Empty<MachineBaselineComparisonRun>();
    public double EffectiveGovernorTensionPercent => GovernorTensionPercent is double value && double.IsFinite(value) ? Math.Clamp(value, 0d, 100d) : 50d;

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
