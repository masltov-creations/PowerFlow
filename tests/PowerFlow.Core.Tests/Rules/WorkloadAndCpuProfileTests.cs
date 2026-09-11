using PowerFlow.Core.Profiling;
using PowerFlow.Core.Rules;
using Xunit;

namespace PowerFlow.Core.Tests.Rules;

public sealed class WorkloadAndCpuProfileTests
{
    [Fact]
    public void CriticalService_DefaultsToBoostAllowed()
    {
        var policy = WorkloadPolicyDefaults.ForService("RpcSs", 2, sharedHost: true);
        Assert.Equal(WorkloadImportance.Critical, policy.Importance);
        Assert.Equal(CpuBoostEntitlement.Allow, policy.Boost);
    }

    [Fact]
    public void SharedAutoService_DefaultsToConditionalRatherThanBlanketAllow()
    {
        var policy = WorkloadPolicyDefaults.ForService("SomeAutoService", 2, sharedHost: true);
        Assert.Equal(WorkloadImportance.Important, policy.Importance);
        Assert.Equal(CpuBoostEntitlement.Conditional, policy.Boost);
    }

    [Fact]
    public void DisabledBackgroundService_DefaultsToDeny()
    {
        var policy = WorkloadPolicyDefaults.ForService("SomeDisabledService", 4, sharedHost: false);
        Assert.Equal(WorkloadImportance.Background, policy.Importance);
        Assert.Equal(CpuBoostEntitlement.Deny, policy.Boost);
    }

    [Fact]
    public void CapabilityAnalysis_FindsEfficiencyKneeAndMaxThroughput()
    {
        var points = new[]
        {
            new CpuCapabilityPoint(1, 100, 30, 130, 4.4),
            new CpuCapabilityPoint(2, 190, 40, 128, 4.35),
            new CpuCapabilityPoint(4, 340, 60, 126, 4.28),
            new CpuCapabilityPoint(8, 500, 100, 124, 4.20),
            new CpuCapabilityPoint(16, 540, 150, 120, 4.08)
        };
        var profile = CpuCapabilityAnalysis.Build(DateTimeOffset.UnixEpoch, "BAL-P", "sig", points);
        Assert.Equal(4, profile.BestEfficiencyWorkers);
        Assert.Equal(8, profile.KneeWorkers);
        Assert.Equal(16, profile.MaxThroughputWorkers);
    }
}