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
    private readonly Dictionary<Guid, ProcessorPolicySnapshot> _baselines = new();

    public PowerModeProfileRuntime(IProcessorPolicyController? policy = null)
        => _policy = policy ?? new WindowsProcessorPolicyController();

    public PowerFlowOperatingProfile? CurrentProfile { get; private set; }
    public PowerModeProfileStatus Status { get; private set; } = new(null, false, false, "No PowerFlow mode profile is active.", null, null);
    public int CapturedSchemeCount => _baselines.Count;

    public PowerModeProfileStatus Apply(PowerFlowOperatingProfile profile, bool liveWritesEnabled)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var previousProfile = CurrentProfile;
        if (!liveWritesEnabled)
        {
            CurrentProfile = profile;
            Status = new(profile.Mode, false, false, $"Preview: would apply core floor {profile.CoreFloorPercent}%, EPP {profile.EnergyPerformancePreferencePercent}%, boost mode {profile.BoostMode}.", null, null);
            return Status;
        }

        var newlyCaptured = new HashSet<Guid>();
        var activeBefore = _policy.CaptureActive();
        CaptureBaseline(activeBefore, newlyCaptured);

        var result = _policy.Apply(new ProcessorPolicyPatch(
            CoreParkingMinCoresPercent: profile.CoreFloorPercent,
            EnergyPerformancePreferencePercent: profile.EnergyPerformancePreferencePercent,
            ProcessorPerformanceBoostMode: profile.BoostMode));
        CaptureBaseline(result.Before, newlyCaptured);

        if (!result.Success)
        {
            foreach (var scheme in newlyCaptured) _baselines.Remove(scheme);
            CurrentProfile = previousProfile;
            Status = new(profile.Mode, true, false,
                $"Failed to apply {profile.Mode} profile: {result.Error}",
                BaselineFor(result.Before.SchemeId), result.After);
            return Status;
        }

        CurrentProfile = profile;
        Status = new(profile.Mode, true, true,
            $"{profile.Mode}: floor {result.After.CoreParkingMinCoresPercent}%, EPP {result.After.EnergyPerformancePreferencePercent}%, boost {result.After.ProcessorPerformanceBoostMode}; {_baselines.Count} scheme baseline(s) protected.",
            BaselineFor(result.After.SchemeId), result.After);
        return Status;
    }

    public PowerModeProfileStatus Restore(string reason = "PowerFlow mode profile baselines restored.")
    {
        var profile = CurrentProfile;
        if (_baselines.Count == 0)
        {
            CurrentProfile = null;
            Status = new(null, false, false, reason, null, null);
            return Status;
        }

        var failures = new List<string>();
        ProcessorPolicySnapshot? lastBaseline = null;
        ProcessorPolicySnapshot? lastCurrent = null;
        foreach (var baseline in _baselines.Values.ToArray())
        {
            lastBaseline = baseline;
            var result = _policy.Restore(baseline);
            lastCurrent = result.After;
            if (result.Success)
                _baselines.Remove(baseline.SchemeId);
            else
                failures.Add($"{baseline.SchemeId}: {result.Error ?? "readback mismatch"}");
        }

        if (failures.Count == 0)
        {
            CurrentProfile = null;
            Status = new(profile?.Mode, true, false, $"{reason} Restored all touched schemes.", lastBaseline, lastCurrent);
            return Status;
        }

        Status = new(profile?.Mode, true, false,
            $"Processor policy restore incomplete; {_baselines.Count} scheme(s) still require restore: {string.Join("; ", failures)}",
            lastBaseline, lastCurrent);
        return Status;
    }

    private void CaptureBaseline(ProcessorPolicySnapshot snapshot, ISet<Guid> newlyCaptured)
    {
        if (_baselines.ContainsKey(snapshot.SchemeId)) return;
        _baselines[snapshot.SchemeId] = snapshot;
        newlyCaptured.Add(snapshot.SchemeId);
    }

    private ProcessorPolicySnapshot? BaselineFor(Guid schemeId) =>
        _baselines.TryGetValue(schemeId, out var baseline) ? baseline : null;
}