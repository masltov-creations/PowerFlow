using PowerFlow.Windows.Power;
using Xunit;

namespace PowerFlow.Windows.Tests.Power;

public sealed class WindowsProcessorPolicyControllerTests
{
    [Fact]
    public void CaptureActive_ReadsExactSchemeValues()
    {
        var native = new FakeNative { Active = PowerPlanIds.PowerSaver, CoreMin = 10, Epp = 60 };
        var snapshot = new WindowsProcessorPolicyController(native).CaptureActive();
        Assert.Equal(PowerPlanIds.PowerSaver, snapshot.SchemeId);
        Assert.Equal((uint)10, snapshot.CoreParkingMinCoresPercent);
        Assert.Equal((uint)60, snapshot.EnergyPerformancePreferencePercent);
        Assert.Equal((uint)2, snapshot.ProcessorPerformanceBoostMode);
    }

    [Fact]
    public void Apply_WritesOnlyRequestedKnobAndVerifiesReadback()
    {
        var native = new FakeNative { Active = PowerPlanIds.PowerSaver, CoreMin = 10, Epp = 60 };
        var result = new WindowsProcessorPolicyController(native).Apply(new ProcessorPolicyPatch(EnergyPerformancePreferencePercent: 40));
        Assert.True(result.Success, result.Error);
        Assert.Equal((uint)10, native.CoreMin);
        Assert.Equal((uint)40, native.Epp);
        Assert.Equal(1, native.ReapplyCount);
    }

    [Fact]
    public void Apply_RevertsSnapshotWhenSecondWriteFails()
    {
        var native = new FakeNative { Active = PowerPlanIds.PowerSaver, CoreMin = 10, Epp = 60, FailEppWrite = true };
        var result = new WindowsProcessorPolicyController(native).Apply(new ProcessorPolicyPatch(25, 40));
        Assert.False(result.Success);
        Assert.Equal((uint)10, native.CoreMin);
        Assert.Equal((uint)60, native.Epp);
    }

    [Fact]
    public void Restore_DoesNotSwitchUserBackWhenAnotherSchemeBecameActive()
    {
        var native = new FakeNative { Active = PowerPlanIds.PowerSaver, CoreMin = 10, Epp = 60 };
        var sut = new WindowsProcessorPolicyController(native);
        var snapshot = sut.CaptureActive();
        native.CoreMin = 30;
        native.Epp = 25;
        native.Active = PowerPlanIds.Balanced;
        var result = sut.Restore(snapshot);
        Assert.True(result.Success, result.Error);
        Assert.Equal(PowerPlanIds.Balanced, native.Active);
        Assert.Equal(0, native.ReapplyCount);
    }

    [Fact]
    public void Apply_CanWriteBoostModeAndRestoreIt()
    {
        var native = new FakeNative { Active = PowerPlanIds.PowerSaver, CoreMin = 10, Epp = 60, BoostMode = 2 };
        var sut = new WindowsProcessorPolicyController(native);
        var baseline = sut.CaptureActive();
        var apply = sut.Apply(new ProcessorPolicyPatch(ProcessorPerformanceBoostMode: 0));
        Assert.True(apply.Success, apply.Error);
        Assert.Equal((uint)0, native.BoostMode);
        var restore = sut.Restore(baseline);
        Assert.True(restore.Success, restore.Error);
        Assert.Equal((uint)2, native.BoostMode);
    }
    [Fact]
    public void Apply_RejectsOutOfRangePercent()
    {
        var native = new FakeNative { Active = PowerPlanIds.PowerSaver };
        Assert.Throws<ArgumentOutOfRangeException>(() => new WindowsProcessorPolicyController(native).Apply(new ProcessorPolicyPatch(101)));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void RealWindows_ReadOnlySmoke_CapturesActiveProcessorPolicy()
    {
        if (!OperatingSystem.IsWindows()) return;
        var snapshot = new WindowsProcessorPolicyController().CaptureActive();
        Assert.InRange(snapshot.CoreParkingMinCoresPercent, 0u, 100u);
        Assert.InRange(snapshot.EnergyPerformancePreferencePercent, 0u, 100u);
        Assert.InRange(snapshot.ProcessorPerformanceBoostMode, 0u, 6u);
    }

    private sealed class FakeNative : IProcessorPolicyNative
    {
        public Guid Active { get; set; }
        public uint CoreMin { get; set; }
        public uint Epp { get; set; }
        public uint BoostMode { get; set; } = 2;
        public bool FailEppWrite { get; set; }
        public int ReapplyCount { get; private set; }

        public Guid GetActiveScheme() => Active;
        public uint ReadAcValue(Guid schemeId, Guid subgroupId, Guid settingId)
            => settingId == ProcessorPolicySettingIds.CoreParkingMinCores ? CoreMin
                : settingId == ProcessorPolicySettingIds.ProcessorPerformanceBoostMode ? BoostMode
                : Epp;
        public int WriteAcValue(Guid schemeId, Guid subgroupId, Guid settingId, uint value)
        {
            if (settingId == ProcessorPolicySettingIds.EnergyPerformancePreference && FailEppWrite) return 5;
            if (settingId == ProcessorPolicySettingIds.CoreParkingMinCores) CoreMin = value;
            else if (settingId == ProcessorPolicySettingIds.ProcessorPerformanceBoostMode) BoostMode = value;
            else Epp = value;
            return 0;
        }
        public int SetActiveScheme(Guid schemeId)
        {
            Active = schemeId;
            ReapplyCount++;
            return 0;
        }
    }
}