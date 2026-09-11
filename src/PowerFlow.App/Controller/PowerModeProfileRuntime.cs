using PowerFlow.Windows.Power;

namespace PowerFlow.App.Controller;

public sealed record PowerModeProfileStatus(
    PowerFlowOperatingMode? Mode,
    bool LiveWritesEnabled,
    bool Applied,
    string Message,
    ProcessorPolicySnapshot? Baseline,
    ProcessorPolicySnapshot? Current);

public sealed class PowerModeProfileRuntime
{
    private readonly IProcessorPolicyController _policy;
    private ProcessorPolicySnapshot? _baseline;

    public PowerModeProfileRuntime(IProcessorPolicyController? policy = null)
        => _policy = policy ?? new WindowsProcessorPolicyController();

    public PowerFlowOperatingProfile? CurrentProfile { get; private set; }
    public PowerModeProfileStatus Status { get; private set; } = new(null, false, false, "No manual PowerFlow mode profile is active.", null, null);

    public PowerModeProfileStatus Apply(PowerFlowOperatingProfile profile, bool liveWritesEnabled)
    {
        ArgumentNullException.ThrowIfNull(profile);
        Restore("Previous PowerFlow mode profile restored before applying the next mode.");
        CurrentProfile = profile;
        if (!liveWritesEnabled)
        {
            Status = new(profile.Mode, false, false, $"Preview: would apply core floor {profile.CoreFloorPercent}%, EPP {profile.EnergyPerformancePreferencePercent}%, boost mode {profile.BoostMode}.", null, null);
            return Status;
        }

        _baseline = _policy.CaptureActive();
        var result = _policy.Apply(new ProcessorPolicyPatch(
            CoreParkingMinCoresPercent: profile.CoreFloorPercent,
            EnergyPerformancePreferencePercent: profile.EnergyPerformancePreferencePercent,
            ProcessorPerformanceBoostMode: profile.BoostMode));
        if (!result.Success)
        {
            var message = $"Failed to apply {profile.Mode} profile: {result.Error}";
            _baseline = null;
            CurrentProfile = null;
            Status = new(profile.Mode, true, false, message, result.Before, result.After);
            return Status;
        }

        Status = new(profile.Mode, true, true,
            $"{profile.Mode}: floor {result.After.CoreParkingMinCoresPercent}%, EPP {result.After.EnergyPerformancePreferencePercent}%, boost {result.After.ProcessorPerformanceBoostMode}.",
            _baseline, result.After);
        return Status;
    }

    public PowerModeProfileStatus Restore(string reason = "PowerFlow mode profile baseline restored.")
    {
        var profile = CurrentProfile;
        if (_baseline is null)
        {
            CurrentProfile = null;
            Status = new(null, false, false, reason, null, null);
            return Status;
        }

        var baseline = _baseline;
        _baseline = null;
        CurrentProfile = null;
        var result = _policy.Restore(baseline);
        Status = new(profile?.Mode, true, false,
            result.Success ? reason : $"Mode profile restore failed: {result.Error}", baseline, result.After);
        return Status;
    }
}