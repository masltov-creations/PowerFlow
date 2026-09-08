using PowerFlow.App.Controller;
using PowerFlow.App.Dashboard;
using PowerFlow.App.Tray;
using PowerFlow.Core.Policy;
using PowerFlow.Core.Rules;
using Xunit;

namespace PowerFlow.App.Tests.Dashboard;

public sealed class ManualLockPresentationTests
{
    private static ControllerSnapshot Snapshot(PowerState state) =>
        new(state, $"{state} locked - Manual", true, "Manual", 8, 0, null, "Windows Power Options",
            DateTimeOffset.UnixEpoch, Array.Empty<TransitionRecord>(), 0, 0);

    [Fact]
    public void ManualPowerSaverLock_IsNamedAsPowerSaverInTray()
    {
        var model = TrayMenuCommands.Build(Snapshot(PowerState.PowerSaver));
        Assert.Equal("Power Saver Locked - Manual", model.StatusText);
        Assert.True(model.PowerSaverChecked);
        Assert.True(model.ReleaseLatchEnabled);
    }

    [Fact]
    public void ManualPowerSaverLock_DecisionMeterExplainsAutomationIsPaused()
    {
        var meter = DecisionMeterProjection.Create(PowerFlowConfig.Default, Snapshot(PowerState.PowerSaver));
        Assert.Equal("MANUAL LOCK", meter.Title);
        Assert.Contains("Power Saver", meter.Explanation, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("automatic", meter.Explanation, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("paused", meter.Explanation, StringComparison.OrdinalIgnoreCase);
    }
}
