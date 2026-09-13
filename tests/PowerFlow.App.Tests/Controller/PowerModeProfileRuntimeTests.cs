using PowerFlow.App.Controller;
using PowerFlow.Windows.Power;
using Xunit;

namespace PowerFlow.App.Tests.Controller;

public sealed class PowerModeProfileRuntimeTests
{
    [Fact]
    public void Apply_UsesMeasuredProfileAndRestoreReturnsBaseline()
    {
        var fake = new FakePolicy(new ProcessorPolicySnapshot(Guid.NewGuid(), 100, 10, 2));
        var sut = new PowerModeProfileRuntime(fake);
        var applied = sut.Apply(PowerFlowOperatingProfiles.Balanced, liveWritesEnabled: true);
        Assert.True(applied.Applied);
        Assert.Equal((uint)25, fake.Current.CoreParkingMinCoresPercent);
        Assert.Equal((uint)35, fake.Current.EnergyPerformancePreferencePercent);
        Assert.Equal((uint)3, fake.Current.ProcessorPerformanceBoostMode);
        sut.Restore();
        Assert.Equal((uint)100, fake.Current.CoreParkingMinCoresPercent);
        Assert.Equal((uint)10, fake.Current.EnergyPerformancePreferencePercent);
        Assert.Equal((uint)2, fake.Current.ProcessorPerformanceBoostMode);
    }

    [Fact]
    public void Apply_ProfileTransitionPreservesOriginalBaselineWithoutIntermediateRestore()
    {
        var initial = new ProcessorPolicySnapshot(Guid.NewGuid(), 100, 10, 2);
        var fake = new FakePolicy(initial);
        var sut = new PowerModeProfileRuntime(fake);

        sut.Apply(PowerFlowOperatingProfiles.Balanced, liveWritesEnabled: true);
        sut.Apply(PowerFlowOperatingProfiles.Performance, liveWritesEnabled: true);

        Assert.Equal(2, fake.ApplyCount);
        Assert.Equal(0, fake.RestoreCount);
        Assert.Equal((uint)75, fake.Current.CoreParkingMinCoresPercent);
        Assert.Equal((uint)10, fake.Current.EnergyPerformancePreferencePercent);

        sut.Restore();
        Assert.Equal(1, fake.RestoreCount);
        Assert.Equal(initial, fake.Current);
    }
    [Fact]
    public void Preview_DoesNotWrite()
    {
        var initial = new ProcessorPolicySnapshot(Guid.NewGuid(), 10, 60, 2);
        var fake = new FakePolicy(initial);
        var status = new PowerModeProfileRuntime(fake).Apply(PowerFlowOperatingProfiles.Saver, liveWritesEnabled: false);
        Assert.False(status.LiveWritesEnabled);
        Assert.Equal(initial, fake.Current);
        Assert.Equal(0, fake.ApplyCount);
    }

    private sealed class FakePolicy(ProcessorPolicySnapshot initial) : IProcessorPolicyController
    {
        public ProcessorPolicySnapshot Current { get; private set; } = initial;
        public int ApplyCount { get; private set; }
        public int RestoreCount { get; private set; }
        public ProcessorPolicySnapshot CaptureActive() => Current;
        public ProcessorPolicyApplyResult Apply(ProcessorPolicyPatch patch)
        {
            ApplyCount++;
            var before = Current;
            Current = Current with
            {
                CoreParkingMinCoresPercent = patch.CoreParkingMinCoresPercent ?? Current.CoreParkingMinCoresPercent,
                EnergyPerformancePreferencePercent = patch.EnergyPerformancePreferencePercent ?? Current.EnergyPerformancePreferencePercent,
                ProcessorPerformanceBoostMode = patch.ProcessorPerformanceBoostMode ?? Current.ProcessorPerformanceBoostMode
            };
            return new(true, before, Current);
        }
        public ProcessorPolicyApplyResult Restore(ProcessorPolicySnapshot snapshot)
        {
            RestoreCount++;
            var before = Current;
            Current = snapshot;
            return new(true, before, Current);
        }
    }
}