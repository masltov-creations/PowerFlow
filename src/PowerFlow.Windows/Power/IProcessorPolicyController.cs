namespace PowerFlow.Windows.Power;

public sealed record ProcessorPolicySnapshot(
    Guid SchemeId,
    uint CoreParkingMinCoresPercent,
    uint EnergyPerformancePreferencePercent,
    uint ProcessorPerformanceBoostMode = 2);

public sealed record ProcessorPolicyPatch(
    uint? CoreParkingMinCoresPercent = null,
    uint? EnergyPerformancePreferencePercent = null,
    uint? ProcessorPerformanceBoostMode = null);

public sealed record ProcessorPolicyApplyResult(
    bool Success,
    ProcessorPolicySnapshot Before,
    ProcessorPolicySnapshot After,
    string? Error = null);

public interface IProcessorPolicyController
{
    ProcessorPolicySnapshot CaptureActive();
    ProcessorPolicyApplyResult Apply(ProcessorPolicyPatch patch);
    ProcessorPolicyApplyResult Restore(ProcessorPolicySnapshot snapshot);
}

public interface IProcessorPolicyNative
{
    Guid GetActiveScheme();
    uint ReadAcValue(Guid schemeId, Guid subgroupId, Guid settingId);
    int WriteAcValue(Guid schemeId, Guid subgroupId, Guid settingId, uint value);
    int SetActiveScheme(Guid schemeId);
}