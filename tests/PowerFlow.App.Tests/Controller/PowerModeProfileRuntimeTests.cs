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
    public void Apply_AcrossPowerSchemesCapturesAndRestoresEachSchemeBaseline()
    {
        var schemeA = Guid.NewGuid();
        var schemeB = Guid.NewGuid();
        var fake = new MultiSchemeFakePolicy(
            new ProcessorPolicySnapshot(schemeA, 25, 35, 3),
            new ProcessorPolicySnapshot(schemeB, 10, 60, 0));
        var sut = new PowerModeProfileRuntime(fake);

        fake.Activate(schemeA);
        sut.Apply(PowerFlowOperatingProfiles.BalancedPerformance, liveWritesEnabled: true);
        fake.Activate(schemeB);
        sut.Apply(PowerFlowOperatingProfiles.Saver, liveWritesEnabled: true);

        Assert.Equal(2, sut.CapturedSchemeCount);
        Assert.Equal((uint)50, fake.Read(schemeA).CoreParkingMinCoresPercent);
        Assert.Equal((uint)10, fake.Read(schemeB).CoreParkingMinCoresPercent);

        sut.Restore();

        Assert.Equal(0, sut.CapturedSchemeCount);
        Assert.Equal(new ProcessorPolicySnapshot(schemeA, 25, 35, 3), fake.Read(schemeA));
        Assert.Equal(new ProcessorPolicySnapshot(schemeB, 10, 60, 0), fake.Read(schemeB));
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
    private sealed class MultiSchemeFakePolicy : IProcessorPolicyController
    {
        private readonly Dictionary<Guid, ProcessorPolicySnapshot> _schemes;
        private Guid _active;

        public MultiSchemeFakePolicy(params ProcessorPolicySnapshot[] snapshots)
        {
            _schemes = snapshots.ToDictionary(snapshot => snapshot.SchemeId);
            _active = snapshots[0].SchemeId;
        }

        public void Activate(Guid schemeId) => _active = schemeId;
        public ProcessorPolicySnapshot Read(Guid schemeId) => _schemes[schemeId];
        public ProcessorPolicySnapshot CaptureActive() => _schemes[_active];

        public ProcessorPolicyApplyResult Apply(ProcessorPolicyPatch patch)
        {
            var before = _schemes[_active];
            var after = before with
            {
                CoreParkingMinCoresPercent = patch.CoreParkingMinCoresPercent ?? before.CoreParkingMinCoresPercent,
                EnergyPerformancePreferencePercent = patch.EnergyPerformancePreferencePercent ?? before.EnergyPerformancePreferencePercent,
                ProcessorPerformanceBoostMode = patch.ProcessorPerformanceBoostMode ?? before.ProcessorPerformanceBoostMode
            };
            _schemes[_active] = after;
            return new(true, before, after);
        }

        public ProcessorPolicyApplyResult Restore(ProcessorPolicySnapshot snapshot)
        {
            var before = _schemes[snapshot.SchemeId];
            _schemes[snapshot.SchemeId] = snapshot;
            return new(true, before, snapshot);
        }
    }
}