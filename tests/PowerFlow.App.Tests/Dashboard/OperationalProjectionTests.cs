using PowerFlow.App.Controller;
using PowerFlow.App.Dashboard;
using PowerFlow.Core.Policy;
using Xunit;

namespace PowerFlow.App.Tests.Dashboard;

public sealed class OperationalProjectionTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 9, 16, 0, 0, TimeSpan.FromHours(-7));

    [Fact]
    public void ManualLatch_ProjectsOwnerCauseAndNextAsDistinctAnswers()
    {
        var vm = NewViewModel(new ControllerSnapshot(
            PowerState.Balanced, "Pinned for analysis", true, "Manual", 22, .4,
            null, "tool.exe", Now, [], 1, 1));

        Assert.Equal("Manual lock", vm.ControlOwnerLabel);
        Assert.Contains("tool.exe", vm.CauseLabel);
        Assert.Contains("manual", vm.ControlDetailLabel, StringComparison.OrdinalIgnoreCase);
        Assert.NotEqual(vm.ControlOwnerLabel, vm.NextActionLabel);
    }

    [Fact]
    public void AutoPolicy_ProjectsAutoOwnerAndPolicyProgress()
    {
        var vm = NewViewModel(new ControllerSnapshot(
            PowerState.PowerSaver, "Quiet desktop", false, null, 9, .65,
            TimeSpan.FromSeconds(3.2), null, Now, [], 1, 1));

        Assert.Equal("Auto", vm.ControlOwnerLabel);
        Assert.Contains("65%", vm.PolicyProgressLabel);
        Assert.Contains("4s", vm.CooldownLabel);
        Assert.Contains("Quiet desktop", vm.CauseLabel);
    }

    [Fact]
    public void ActiveSources_NeverClaimPerProcessWatts()
    {
        var vm = NewViewModel(new ControllerSnapshot(
            PowerState.HighPerformance, "Matched game rule", true, "Game", 48, 1,
            null, "game.exe", Now, [], 1, 1));

        Assert.Contains(vm.ActiveSourceLines, line => line.Contains("game.exe", StringComparison.OrdinalIgnoreCase));
        Assert.All(vm.ActiveSourceLines, line => Assert.DoesNotContain(" W", line));
    }

    private static DashboardViewModel NewViewModel(ControllerSnapshot snapshot)
    {
        var vm = new DashboardViewModel();
        vm.Update(snapshot, null);
        return vm;
    }
}