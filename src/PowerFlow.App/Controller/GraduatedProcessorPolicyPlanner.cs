using PowerFlow.Windows.Power;

namespace PowerFlow.App.Controller;

public sealed record GraduatedProcessorPolicyPlan(
    double RequestedCapacityPercent,
    double DeliveredCapacityPercent,
    double EstimatedRequiredCoreFloorPercent,
    uint CurrentCoreFloorPercent,
    uint NextCoreFloorPercent,
    uint CurrentEnergyPerformancePreferencePercent,
    bool CoreFloorQualified,
    bool EnergyPerformancePreferenceQualified,
    string Reason);

public static class GraduatedProcessorPolicyPlanner
{
    public const double DeliveryDeadbandPercent = 3d;
    public const double ReleaseDeadbandPercent = 8d;
    public const uint MinimumQualifiedCoreFloorPercent = 10;
    public const uint MaximumCoreFloorRisePerStep = 15;
    public const uint MaximumCoreFloorFallPerStep = 10;

    public static GraduatedProcessorPolicyPlan Plan(
        double requestedCapacityPercent,
        double deliveredCapacityPercent,
        double? averageAwakePercentOfMaximumFrequency,
        ProcessorPolicySnapshot policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        var requested = Math.Clamp(requestedCapacityPercent, 0d, 100d);
        var delivered = Math.Clamp(deliveredCapacityPercent, 0d, 100d);
        var performance = averageAwakePercentOfMaximumFrequency is double value && double.IsFinite(value) && value > 0
            ? Math.Clamp(value, 10d, 200d)
            : 100d;
        var requiredFloor = Math.Clamp(Math.Ceiling(requested / (performance / 100d)), MinimumQualifiedCoreFloorPercent, 100d);
        var current = Math.Clamp(policy.CoreParkingMinCoresPercent, 0u, 100u);
        uint next = current;
        string reason;

        if (delivered + DeliveryDeadbandPercent < requested)
        {
            var desired = (uint)Math.Ceiling(requiredFloor);
            if (desired > current)
                next = Math.Min(desired, current + MaximumCoreFloorRisePerStep);
            reason = next > current
                ? $"Delivered capacity trails request; raise the qualified core floor toward {requiredFloor:0}%."
                : "Delivered capacity trails request, but the current core floor is already at or above the estimated requirement.";
        }
        else if (delivered > requested + ReleaseDeadbandPercent && current > MinimumQualifiedCoreFloorPercent)
        {
            var desired = Math.Max(MinimumQualifiedCoreFloorPercent, (uint)Math.Ceiling(requiredFloor));
            if (desired < current)
                next = Math.Max(desired, current - MaximumCoreFloorFallPerStep);
            reason = next < current
                ? $"Delivered capacity materially exceeds request; release the core floor gradually toward {requiredFloor:0}%."
                : "Delivered capacity exceeds request, but the estimated floor still requires the current setting.";
        }
        else
        {
            reason = "Delivered capacity is within the actuator deadband; hold the current core floor.";
        }

        return new GraduatedProcessorPolicyPlan(
            requested,
            delivered,
            requiredFloor,
            current,
            next,
            policy.EnergyPerformancePreferencePercent,
            CoreFloorQualified: true,
            EnergyPerformancePreferenceQualified: false,
            $"{reason} EPP remains HOLD because the isolated EPP sweep showed no material response on the current non-autonomous Saver path.");
    }
}