namespace PowerFlow.Core.Rules;

public enum WorkloadImportance
{
    Critical,
    Interactive,
    Important,
    Background
}

public enum CpuBoostEntitlement
{
    Allow,
    Conditional,
    Deny
}

public sealed record ServicePolicyRule(
    string ServiceName,
    WorkloadImportance Importance,
    CpuBoostEntitlement BoostEntitlement,
    string? DisplayName = null);

public static class WorkloadPolicyDefaults
{
    private static readonly HashSet<string> CriticalServices = new(StringComparer.OrdinalIgnoreCase)
    {
        "RpcSs", "DcomLaunch", "Power", "PlugPlay", "EventLog", "LSM", "Schedule", "Winmgmt", "Dnscache", "NlaSvc", "AudioSrv"
    };

    public static (WorkloadImportance Importance, CpuBoostEntitlement Boost) ForService(string serviceName, uint startType, bool sharedHost)
    {
        if (CriticalServices.Contains(serviceName)) return (WorkloadImportance.Critical, CpuBoostEntitlement.Allow);
        if (startType <= 2) return (WorkloadImportance.Important, sharedHost ? CpuBoostEntitlement.Conditional : CpuBoostEntitlement.Allow);
        if (startType == 3) return (WorkloadImportance.Background, CpuBoostEntitlement.Conditional);
        return (WorkloadImportance.Background, CpuBoostEntitlement.Deny);
    }
}