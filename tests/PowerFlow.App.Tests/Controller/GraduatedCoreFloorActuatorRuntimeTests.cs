using PowerFlow.App.Controller;
using PowerFlow.Windows.Power;
using Xunit;

namespace PowerFlow.App.Tests.Controller;

public sealed class GraduatedCoreFloorActuatorRuntimeTests
{
    private static readonly Guid Saver = PowerPlanIds.PowerSaver;

    [Fact]
    public void Disabled_NeverWritesAndRestoresAnArmedBaseline()
    {
        var native = new FakePolicy(new ProcessorPolicySnapshot(Saver, 10, 60));
        var now = DateTimeOffset.UtcNow;
        var sut = new GraduatedCoreFloorActuatorRuntime(native, () => now);
        sut.Evaluate(new(40, 20, 50), true, false, false, Saver);
        now = now.AddSeconds(1);
        var status = sut.Evaluate(new(40, 20, 50), false, false, false, Saver);
        Assert.Equal(GraduatedCoreActuatorState.Disabled, status.State);
        Assert.Equal((uint)10, native.Current.CoreParkingMinCoresPercent);
        Assert.Empty(native.Applied);
    }

    [Fact]
    public void PreviewAndCoarseAdaptiveActuation_NeverArm()
    {
        var native = new FakePolicy(new ProcessorPolicySnapshot(Saver, 10, 60));
        var sut = new GraduatedCoreFloorActuatorRuntime(native);
        Assert.False(sut.Evaluate(new(40, 20, 50), true, true, false, Saver).LiveWritesEnabled);
        Assert.False(sut.Evaluate(new(40, 20, 50), true, false, true, Saver).LiveWritesEnabled);
        Assert.Empty(native.Applied);
    }

    [Fact]
    public void FirstEligibleEvaluation_OnlyCapturesBaseline()
    {
        var native = new FakePolicy(new ProcessorPolicySnapshot(Saver, 10, 60));
        var sut = new GraduatedCoreFloorActuatorRuntime(native);
        var status = sut.Evaluate(new(40, 20, 50), true, false, false, Saver);
        Assert.Equal(GraduatedCoreActuatorState.Armed, status.State);
        Assert.Empty(native.Applied);
    }

    [Fact]
    public void UnderDelivery_AppliesOneBoundedCoreFloorStepAndNeverTouchesEpp()
    {
        var native = new FakePolicy(new ProcessorPolicySnapshot(Saver, 10, 60));
        var now = DateTimeOffset.UtcNow;
        var sut = new GraduatedCoreFloorActuatorRuntime(native, () => now);
        sut.Evaluate(new(40, 20, 50), true, false, false, Saver);
        now = now.AddSeconds(1);
        var status = sut.Evaluate(new(40, 20, 50), true, false, false, Saver);
        Assert.Equal(GraduatedCoreActuatorState.Applied, status.State);
        Assert.Equal((uint)25, native.Current.CoreParkingMinCoresPercent);
        Assert.Equal((uint)60, native.Current.EnergyPerformancePreferencePercent);
        Assert.Single(native.Applied);
        Assert.Null(native.Applied[0].EnergyPerformancePreferencePercent);
    }

    [Fact]
    public void Cooldown_BlocksASecondImmediateWrite()
    {
        var native = new FakePolicy(new ProcessorPolicySnapshot(Saver, 10, 60));
        var now = DateTimeOffset.UtcNow;
        var sut = new GraduatedCoreFloorActuatorRuntime(native, () => now);
        sut.Evaluate(new(60, 20, 50), true, false, false, Saver);
        now = now.AddSeconds(1);
        sut.Evaluate(new(60, 20, 50), true, false, false, Saver);
        now = now.AddSeconds(2);
        var status = sut.Evaluate(new(60, 20, 50), true, false, false, Saver);
        Assert.Equal(GraduatedCoreActuatorState.Holding, status.State);
        Assert.Single(native.Applied);
    }

    [Fact]
    public void WritesNeverExceedQualified75PercentCeiling()
    {
        var native = new FakePolicy(new ProcessorPolicySnapshot(Saver, 70, 60));
        var now = DateTimeOffset.UtcNow;
        var sut = new GraduatedCoreFloorActuatorRuntime(native, () => now);
        sut.Evaluate(new(100, 10, 40), true, false, false, Saver);
        now = now.AddSeconds(1);
        sut.Evaluate(new(100, 10, 40), true, false, false, Saver);
        Assert.Equal((uint)75, native.Current.CoreParkingMinCoresPercent);
    }

    [Fact]
    public void SchemeChange_RestoresQualifiedBaselineAndSuspendsWithoutTouchingNewScheme()
    {
        var native = new FakePolicy(new ProcessorPolicySnapshot(Saver, 10, 60));
        var now = DateTimeOffset.UtcNow;
        var sut = new GraduatedCoreFloorActuatorRuntime(native, () => now);
        sut.Evaluate(new(40, 20, 50), true, false, false, Saver);
        now = now.AddSeconds(1);
        sut.Evaluate(new(40, 20, 50), true, false, false, Saver);
        native.Current = new ProcessorPolicySnapshot(PowerPlanIds.Balanced, 100, 10);
        now = now.AddSeconds(6);
        var status = sut.Evaluate(new(40, 20, 100), true, false, false, Saver);
        Assert.Equal(GraduatedCoreActuatorState.Suspended, status.State);
        Assert.Equal(PowerPlanIds.Balanced, native.Current.SchemeId);
        Assert.Equal((uint)100, native.Current.CoreParkingMinCoresPercent);
        Assert.Contains(native.Restored, snapshot => snapshot.SchemeId == Saver && snapshot.CoreParkingMinCoresPercent == 10);
    }

    [Fact]
    public void ApplyFailure_FaultLatchesAndRestoresBaseline()
    {
        var native = new FakePolicy(new ProcessorPolicySnapshot(Saver, 10, 60)) { FailApply = true };
        var now = DateTimeOffset.UtcNow;
        var sut = new GraduatedCoreFloorActuatorRuntime(native, () => now);
        sut.Evaluate(new(40, 20, 50), true, false, false, Saver);
        now = now.AddSeconds(1);
        var status = sut.Evaluate(new(40, 20, 50), true, false, false, Saver);
        Assert.Equal(GraduatedCoreActuatorState.Faulted, status.State);
        Assert.False(status.LiveWritesEnabled);
        Assert.Equal((uint)10, native.Current.CoreParkingMinCoresPercent);
    }

    private sealed class FakePolicy(ProcessorPolicySnapshot initial) : IProcessorPolicyController
    {
        public ProcessorPolicySnapshot Current { get; set; } = initial;
        public List<ProcessorPolicyPatch> Applied { get; } = [];
        public List<ProcessorPolicySnapshot> Restored { get; } = [];
        public bool FailApply { get; set; }
        public ProcessorPolicySnapshot CaptureActive() => Current;
        public ProcessorPolicyApplyResult Apply(ProcessorPolicyPatch patch)
        {
            var before = Current;
            Applied.Add(patch);
            if (FailApply) return new(false, before, before, "injected failure");
            Current = Current with
            {
                CoreParkingMinCoresPercent = patch.CoreParkingMinCoresPercent ?? Current.CoreParkingMinCoresPercent,
                EnergyPerformancePreferencePercent = patch.EnergyPerformancePreferencePercent ?? Current.EnergyPerformancePreferencePercent
            };
            return new(true, before, Current);
        }
        public ProcessorPolicyApplyResult Restore(ProcessorPolicySnapshot snapshot)
        {
            Restored.Add(snapshot);
            if (Current.SchemeId == snapshot.SchemeId) Current = snapshot;
            return new(true, snapshot, snapshot);
        }
    }
}