using PowerFlow.Windows.Power;
using Xunit;

namespace PowerFlow.Windows.Tests.Power;

public sealed class WindowsPowerPlanControllerTests
{
    [Fact]
    public async Task ListAsync_ReportsStandardPlansWhenPresent()
    {
        var native = new FakeNative(
            new PowerPlanInfo(PowerPlanIds.PowerSaver, "Power saver"),
            new PowerPlanInfo(PowerPlanIds.Balanced, "Balanced"),
            new PowerPlanInfo(PowerPlanIds.HighPerformance, "High performance"));
        var sut = new WindowsPowerPlanController(native);
        var plans = await sut.ListAsync();
        Assert.Contains(plans, p => p.Id == PowerPlanIds.PowerSaver);
        Assert.Contains(plans, p => p.Id == PowerPlanIds.Balanced);
        Assert.Contains(plans, p => p.Id == PowerPlanIds.HighPerformance);
    }

    [Fact]
    public async Task ListAsync_AllowsHighPerformanceToBeAbsent()
    {
        var native = new FakeNative(
            new PowerPlanInfo(PowerPlanIds.PowerSaver, "Power saver"),
            new PowerPlanInfo(PowerPlanIds.Balanced, "Balanced"));
        var plans = await new WindowsPowerPlanController(native).ListAsync();
        Assert.DoesNotContain(plans, p => p.Id == PowerPlanIds.HighPerformance);
    }

    [Fact]
    public async Task ActivateAsync_VerifiesRequestedPlanByReadingItBack()
    {
        var native = new FakeNative(new PowerPlanInfo(PowerPlanIds.Balanced, "Balanced")) { Active = PowerPlanIds.PowerSaver };
        var result = await new WindowsPowerPlanController(native).ActivateAsync(PowerPlanIds.Balanced);
        Assert.True(result.Success);
        Assert.Equal(PowerPlanIds.Balanced, result.ActiveId);
        Assert.Equal(PowerPlanIds.Balanced, native.Active);
    }

    [Fact]
    public async Task ActivateAsync_ReturnsFailureWhenNativeSetFails()
    {
        var native = new FakeNative(new PowerPlanInfo(PowerPlanIds.Balanced, "Balanced")) { Active = PowerPlanIds.PowerSaver, SetError = 5 };
        var result = await new WindowsPowerPlanController(native).ActivateAsync(PowerPlanIds.Balanced);
        Assert.False(result.Success);
        Assert.Contains("5", result.Error!);
    }

    [Fact]
    public async Task ActivateAsync_ReturnsFailureWhenReadBackDoesNotMatch()
    {
        var native = new FakeNative(new PowerPlanInfo(PowerPlanIds.Balanced, "Balanced")) { Active = PowerPlanIds.PowerSaver, IgnoreSet = true };
        var result = await new WindowsPowerPlanController(native).ActivateAsync(PowerPlanIds.Balanced);
        Assert.False(result.Success);
        Assert.Equal(PowerPlanIds.PowerSaver, result.ActiveId);
        Assert.Contains("verification", result.Error!, StringComparison.OrdinalIgnoreCase);
    }


    [Fact]
    [Trait("Category", "Integration")]
    public async Task RealWindows_ReadOnlySmoke_ListsAndFindsActiveScheme()
    {
        if (!OperatingSystem.IsWindows()) return;
        var sut = new WindowsPowerPlanController();
        var plans = await sut.ListAsync();
        var active = await sut.GetActiveAsync();
        Assert.NotEmpty(plans);
        Assert.Contains(plans, p => p.Id == active.Id);
    }
    private sealed class FakeNative(params PowerPlanInfo[] plans) : IPowerPlanNative
    {
        public IReadOnlyList<PowerPlanInfo> Plans { get; } = plans;
        public Guid Active { get; set; } = PowerPlanIds.Balanced;
        public int SetError { get; set; }
        public bool IgnoreSet { get; set; }
        public IReadOnlyList<PowerPlanInfo> EnumeratePlans() => Plans;
        public Guid GetActiveScheme() => Active;
        public int SetActiveScheme(Guid id)
        {
            if (SetError != 0) return SetError;
            if (!IgnoreSet) Active = id;
            return 0;
        }
    }
}

