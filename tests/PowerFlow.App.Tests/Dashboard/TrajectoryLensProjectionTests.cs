using PowerFlow.App.Dashboard;
using PowerFlow.Core.Policy;
using Xunit;

namespace PowerFlow.App.Tests.Dashboard;

public sealed class TrajectoryLensProjectionTests
{
    [Fact]
    public void SampleLensShowsExactTelemetryWithoutInventingMissingValues()
    {
        var at = new DateTimeOffset(2026, 9, 8, 23, 30, 0, TimeSpan.Zero);
        var sample = new HoverTelemetry(at, 31.25, null, 4225, PowerState.Balanced);

        var lens = TrajectoryLensProjection.ForSample(sample);

        Assert.Equal(TrajectoryLensKind.Sample, lens.Kind);
        Assert.Contains("31.3%", lens.Primary);
        Assert.Contains("— W", lens.Secondary);
        Assert.Contains("4.23 GHz", lens.Secondary);
        Assert.Contains("BALANCED", lens.Title);
    }

    [Fact]
    public void NowLensExplainsDirectionAndQualification()
    {
        var model = new TrajectoryModel(
            Array.Empty<PowerFlow.App.Telemetry.ContinuitySample>(), [], [], [],
            new TrajectoryRail(21, TimeSpan.FromSeconds(25), "QUIET 21%"),
            new TrajectoryRail(44, TimeSpan.FromSeconds(4), "PROMOTE 44%"),
            new TrajectoryNow(PowerState.PowerSaver, 46, .75, false, null, "CPU demand"),
            TrajectoryDirection.TowardBalanced,
            "Qualifying for Balanced: CPU is above 44% (4s hold)");

        var lens = TrajectoryLensProjection.ForNow(model);

        Assert.Equal(TrajectoryLensKind.Now, lens.Kind);
        Assert.Contains("NOW", lens.Title);
        Assert.Contains("75%", lens.Secondary);
        Assert.Contains("Balanced", lens.Detail);
    }

    [Fact]
    public void TransitionLensCarriesEpisodeIdentityAndFailureState()
    {
        var marker = new TrajectoryTransitionMarker(DateTimeOffset.UtcNow, PowerState.Balanced, PowerState.HighPerformance, "activation failed", false);

        var lens = TrajectoryLensProjection.ForTransition(marker);

        Assert.Equal(TrajectoryLensKind.Transition, lens.Kind);
        Assert.NotNull(lens.Key);
        Assert.Contains("FAILED", lens.Title);
        Assert.Contains("activation failed", lens.Detail);
    }

    [Fact]
    public void RailAndModeLensesExplainPolicyWithoutChangingIt()
    {
        var quiet = TrajectoryLensProjection.ForRail(new TrajectoryRail(21, TimeSpan.FromSeconds(25), "QUIET 21%"), promote: false);
        var mode = TrajectoryLensProjection.ForMode(PowerState.PowerSaver, isCurrent: true, isManual: false);

        Assert.Equal(TrajectoryLensKind.QuietRail, quiet.Kind);
        Assert.Contains("25s", quiet.Detail);
        Assert.Equal(TrajectoryLensKind.Mode, mode.Kind);
        Assert.Contains("SAVER", mode.Title);
        Assert.Contains("current", mode.Detail, StringComparison.OrdinalIgnoreCase);
    }
}
