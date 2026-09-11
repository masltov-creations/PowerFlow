using PowerFlow.Core.Policy;

namespace PowerFlow.App.Controller;

public enum PowerFlowOperatingMode
{
    Saver,
    Balanced,
    Performance,
    Ultra
}

public sealed record PowerFlowOperatingProfile(
    PowerFlowOperatingMode Mode,
    PowerState WindowsState,
    uint CoreFloorPercent,
    uint EnergyPerformancePreferencePercent,
    uint BoostMode,
    double ReadinessFloorPercent,
    double PromotionQualificationSeconds,
    string Description);

public static class PowerFlowOperatingProfiles
{
    public static PowerFlowOperatingProfile Saver { get; } = new(
        PowerFlowOperatingMode.Saver, PowerState.PowerSaver,
        CoreFloorPercent: 10, EnergyPerformancePreferencePercent: 60, BoostMode: 0,
        ReadinessFloorPercent: 12, PromotionQualificationSeconds: 4.0,
        Description: "Low readiness floor; boost disabled; sustained demand must prove itself before capacity rises.");

    public static PowerFlowOperatingProfile Balanced { get; } = new(
        PowerFlowOperatingMode.Balanced, PowerState.Balanced,
        CoreFloorPercent: 25, EnergyPerformancePreferencePercent: 35, BoostMode: 3,
        ReadinessFloorPercent: 25, PromotionQualificationSeconds: 0.75,
        Description: "Adaptive equilibrium: modest core floor, moderate energy preference, efficient boost.");

    public static PowerFlowOperatingProfile Performance { get; } = new(
        PowerFlowOperatingMode.Performance, PowerState.Balanced,
        CoreFloorPercent: 75, EnergyPerformancePreferencePercent: 10, BoostMode: 2,
        ReadinessFloorPercent: 75, PromotionQualificationSeconds: 0.15,
        Description: "High readiness: most cores kept available with aggressive boost, while idle frequency can still fall.");

    public static PowerFlowOperatingProfile Ultra { get; } = new(
        PowerFlowOperatingMode.Ultra, PowerState.HighPerformance,
        CoreFloorPercent: 100, EnergyPerformancePreferencePercent: 10, BoostMode: 2,
        ReadinessFloorPercent: 100, PromotionQualificationSeconds: 0,
        Description: "Fully pre-armed: High Performance plan, all cores available, no capacity qualification delay.");

    public static PowerFlowOperatingProfile For(PowerFlowOperatingMode mode) => mode switch
    {
        PowerFlowOperatingMode.Saver => Saver,
        PowerFlowOperatingMode.Balanced => Balanced,
        PowerFlowOperatingMode.Performance => Performance,
        PowerFlowOperatingMode.Ultra => Ultra,
        _ => Balanced
    };
}