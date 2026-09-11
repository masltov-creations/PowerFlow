using System.ComponentModel;
using System.Runtime.InteropServices;

namespace PowerFlow.Windows.Power;

public static class ProcessorPolicySettingIds
{
    public static readonly Guid ProcessorSubgroup = Guid.Parse("54533251-82be-4824-96c1-47b60b740d00");
    public static readonly Guid CoreParkingMinCores = Guid.Parse("0cc5b647-c1df-4637-891a-dec35c318583");
    public static readonly Guid EnergyPerformancePreference = Guid.Parse("36687f9e-e3a5-4dbf-b1dc-15eb381c6863");
    public static readonly Guid ProcessorPerformanceBoostMode = Guid.Parse("be337238-0d82-4146-a960-4f3749d470c7");
}

public sealed class WindowsProcessorPolicyController : IProcessorPolicyController
{
    private readonly IProcessorPolicyNative _native;

    public WindowsProcessorPolicyController() : this(new ProcessorPolicyNative()) { }
    public WindowsProcessorPolicyController(IProcessorPolicyNative native) => _native = native;

    public ProcessorPolicySnapshot CaptureActive()
    {
        var scheme = _native.GetActiveScheme();
        return Capture(scheme);
    }

    public ProcessorPolicyApplyResult Apply(ProcessorPolicyPatch patch)
    {
        ArgumentNullException.ThrowIfNull(patch);
        ValidatePercent(patch.CoreParkingMinCoresPercent, nameof(patch.CoreParkingMinCoresPercent));
        ValidatePercent(patch.EnergyPerformancePreferencePercent, nameof(patch.EnergyPerformancePreferencePercent));
        ValidateBoostMode(patch.ProcessorPerformanceBoostMode);

        var scheme = _native.GetActiveScheme();
        var before = Capture(scheme);
        try
        {
            if (patch.CoreParkingMinCoresPercent is uint cores)
                WriteVerified(scheme, ProcessorPolicySettingIds.CoreParkingMinCores, cores, "core parking minimum");
            if (patch.EnergyPerformancePreferencePercent is uint epp)
                WriteVerified(scheme, ProcessorPolicySettingIds.EnergyPerformancePreference, epp, "energy performance preference");
            if (patch.ProcessorPerformanceBoostMode is uint boostMode)
                WriteVerified(scheme, ProcessorPolicySettingIds.ProcessorPerformanceBoostMode, boostMode, "processor performance boost mode");

            ReactivateIfCurrent(scheme);
            var after = Capture(scheme);
            return new ProcessorPolicyApplyResult(true, before, after);
        }
        catch (Exception ex)
        {
            TryRestoreValues(before);
            return new ProcessorPolicyApplyResult(false, before, CaptureBestEffort(before), ex.Message);
        }
    }

    public ProcessorPolicyApplyResult Restore(ProcessorPolicySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        try
        {
            WriteVerified(snapshot.SchemeId, ProcessorPolicySettingIds.CoreParkingMinCores, snapshot.CoreParkingMinCoresPercent, "core parking minimum");
            WriteVerified(snapshot.SchemeId, ProcessorPolicySettingIds.EnergyPerformancePreference, snapshot.EnergyPerformancePreferencePercent, "energy performance preference");
            WriteVerified(snapshot.SchemeId, ProcessorPolicySettingIds.ProcessorPerformanceBoostMode, snapshot.ProcessorPerformanceBoostMode, "processor performance boost mode");
            ReactivateIfCurrent(snapshot.SchemeId);
            var after = Capture(snapshot.SchemeId);
            var success = after.CoreParkingMinCoresPercent == snapshot.CoreParkingMinCoresPercent
                && after.EnergyPerformancePreferencePercent == snapshot.EnergyPerformancePreferencePercent
                && after.ProcessorPerformanceBoostMode == snapshot.ProcessorPerformanceBoostMode;
            return new ProcessorPolicyApplyResult(success, snapshot, after, success ? null : "Processor policy restore readback did not match the snapshot.");
        }
        catch (Exception ex)
        {
            return new ProcessorPolicyApplyResult(false, snapshot, CaptureBestEffort(snapshot), ex.Message);
        }
    }

    private ProcessorPolicySnapshot Capture(Guid scheme) => new(
        scheme,
        _native.ReadAcValue(scheme, ProcessorPolicySettingIds.ProcessorSubgroup, ProcessorPolicySettingIds.CoreParkingMinCores),
        _native.ReadAcValue(scheme, ProcessorPolicySettingIds.ProcessorSubgroup, ProcessorPolicySettingIds.EnergyPerformancePreference),
        _native.ReadAcValue(scheme, ProcessorPolicySettingIds.ProcessorSubgroup, ProcessorPolicySettingIds.ProcessorPerformanceBoostMode));

    private ProcessorPolicySnapshot CaptureBestEffort(ProcessorPolicySnapshot fallback)
    {
        try { return Capture(fallback.SchemeId); }
        catch { return fallback; }
    }

    private void WriteVerified(Guid scheme, Guid setting, uint value, string name)
    {
        var error = _native.WriteAcValue(scheme, ProcessorPolicySettingIds.ProcessorSubgroup, setting, value);
        if (error != 0) throw new Win32Exception(error, $"Failed to write {name} ({error}).");
        var readback = _native.ReadAcValue(scheme, ProcessorPolicySettingIds.ProcessorSubgroup, setting);
        if (readback != value) throw new InvalidOperationException($"{name} verification failed: requested {value}, read back {readback}.");
    }

    private void ReactivateIfCurrent(Guid scheme)
    {
        if (_native.GetActiveScheme() != scheme) return;
        var error = _native.SetActiveScheme(scheme);
        if (error != 0) throw new Win32Exception(error, $"Failed to reapply active power scheme ({error}).");
    }

    private void TryRestoreValues(ProcessorPolicySnapshot snapshot)
    {
        try
        {
            _native.WriteAcValue(snapshot.SchemeId, ProcessorPolicySettingIds.ProcessorSubgroup, ProcessorPolicySettingIds.CoreParkingMinCores, snapshot.CoreParkingMinCoresPercent);
            _native.WriteAcValue(snapshot.SchemeId, ProcessorPolicySettingIds.ProcessorSubgroup, ProcessorPolicySettingIds.EnergyPerformancePreference, snapshot.EnergyPerformancePreferencePercent);
            _native.WriteAcValue(snapshot.SchemeId, ProcessorPolicySettingIds.ProcessorSubgroup, ProcessorPolicySettingIds.ProcessorPerformanceBoostMode, snapshot.ProcessorPerformanceBoostMode);
            ReactivateIfCurrent(snapshot.SchemeId);
        }
        catch { }
    }

    private static void ValidatePercent(uint? value, string name)
    {
        if (value is > 100) throw new ArgumentOutOfRangeException(name, "Processor policy percentages must be between 0 and 100.");
    }

    private static void ValidateBoostMode(uint? value)
    {
        if (value is > 6) throw new ArgumentOutOfRangeException(nameof(value), "Processor performance boost mode must be between 0 and 6.");
    }

    private sealed class ProcessorPolicyNative : IProcessorPolicyNative
    {
        public Guid GetActiveScheme()
        {
            var result = PowerGetActiveScheme(IntPtr.Zero, out var ptr);
            if (result != 0) throw new Win32Exception((int)result);
            try { return Marshal.PtrToStructure<Guid>(ptr); }
            finally { LocalFree(ptr); }
        }

        public uint ReadAcValue(Guid schemeId, Guid subgroupId, Guid settingId)
        {
            var scheme = schemeId;
            var subgroup = subgroupId;
            var setting = settingId;
            var result = PowerReadACValueIndex(IntPtr.Zero, ref scheme, ref subgroup, ref setting, out var value);
            if (result != 0) throw new Win32Exception((int)result);
            return value;
        }

        public int WriteAcValue(Guid schemeId, Guid subgroupId, Guid settingId, uint value)
        {
            var scheme = schemeId;
            var subgroup = subgroupId;
            var setting = settingId;
            return unchecked((int)PowerWriteACValueIndex(IntPtr.Zero, ref scheme, ref subgroup, ref setting, value));
        }

        public int SetActiveScheme(Guid schemeId)
        {
            var scheme = schemeId;
            return unchecked((int)PowerSetActiveScheme(IntPtr.Zero, ref scheme));
        }

        [DllImport("powrprof.dll")]
        private static extern uint PowerGetActiveScheme(IntPtr userRootPowerKey, out IntPtr activePolicyGuid);

        [DllImport("powrprof.dll")]
        private static extern uint PowerReadACValueIndex(IntPtr rootPowerKey, ref Guid schemeGuid, ref Guid subGroupOfPowerSettingsGuid, ref Guid powerSettingGuid, out uint acValueIndex);

        [DllImport("powrprof.dll")]
        private static extern uint PowerWriteACValueIndex(IntPtr rootPowerKey, ref Guid schemeGuid, ref Guid subGroupOfPowerSettingsGuid, ref Guid powerSettingGuid, uint acValueIndex);

        [DllImport("powrprof.dll")]
        private static extern uint PowerSetActiveScheme(IntPtr userRootPowerKey, ref Guid schemeGuid);

        [DllImport("kernel32.dll")]
        private static extern IntPtr LocalFree(IntPtr hMem);
    }
}