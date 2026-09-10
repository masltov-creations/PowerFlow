using PowerFlow.App.Controller;
using PowerFlow.App.Dashboard;
using PowerFlow.App.Telemetry;
using PowerFlow.Core.Envelope;
using PowerFlow.Core.Policy;
using PowerFlow.Core.Rules;
using PowerFlow.Windows.Activity;
using Xunit;

namespace PowerFlow.App.Tests.Dashboard;

public sealed class AdaptiveGovernorDryRunTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 9, 21, 0, 0, TimeSpan.Zero);

    [Fact]
    public void RichCoreTelemetryFlowsIntoOperatingHistoryWithoutInventingActiveCores()
    {
        var vm = new DashboardViewModel();
        vm.Configure(PowerFlowConfig.Default);
        var snapshot = Snapshot(PowerState.Balanced, 40, T0, "render.exe");
        vm.Update(snapshot, new DashboardTelemetry(48, 4100, T0, null, "reference-host", null, 24));

        var observation = Assert.Single(vm.OperatingHistory);
        Assert.Null(observation.ActiveCores);
        Assert.Equal(24, observation.TotalCores);
        Assert.Equal("render.exe", observation.Actor);
    }

    [Fact]
    public void AppCeilingDryRunProducesBrakeEvidenceWithoutChangingControllerSnapshot()
    {
        var rule = new AppRule("render.exe", AppRuleMode.Balanced, "Render", true,
            new PerformanceEntitlement(EnvelopeZone.Efficient, TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(12), TimeSpan.FromSeconds(6), true));
        var vm = new DashboardViewModel();
        vm.Configure(PowerFlowConfig.Default with { AppRules = new[] { rule } });
        var snapshot = Snapshot(PowerState.Balanced, 96, T0, "render.exe");

        vm.Update(snapshot, new DashboardTelemetry(70, 4300, T0, null, "reference-host", null, 24));

        var observation = Assert.Single(vm.OperatingHistory);
        Assert.Equal(EnvelopeDecisionKind.Brake, observation.Decision);
        Assert.Equal(EnvelopeZone.Efficient, observation.Zone);
        Assert.Contains("ceiling", vm.GovernorDryRunExplanation, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("BRAKE", vm.GovernorDryRunLabel, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(PowerState.Balanced, snapshot.State);
    }

    [Fact]
    public void HighConfidenceHistoryDryRunShowsQualifyingThenLeaseUsingSameRetainedSamples()
    {
        var vm = new DashboardViewModel();
        vm.Configure(PowerFlowConfig.Default);
        var continuity = Enumerable.Range(0, 125)
            .Select(i => new ContinuitySample(
                T0.AddSeconds(i), 100, 70 + i % 3, 4300 + i % 50, PowerState.Balanced,
                "sustained test", false, null, 0, "compute.exe", null, 24))
            .ToArray();
        var snapshot = Snapshot(PowerState.Balanced, 100, T0.AddSeconds(124), "compute.exe");

        vm.UpdateContinuity(snapshot, continuity, new DashboardTelemetry(72, 4320, T0.AddSeconds(124), null, "reference-host", null, 24));

        Assert.Equal(125, vm.OperatingHistory.Count);
        Assert.Contains(vm.OperatingHistory.Take(5), x => x.Decision == EnvelopeDecisionKind.Qualifying);
        Assert.Contains(vm.OperatingHistory.Skip(4), x => x.Decision == EnvelopeDecisionKind.Lease);
        Assert.Contains("LEASE", vm.GovernorDryRunLabel, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("lease", vm.GovernorDryRunExplanation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DryRunSourceContractKeepsRealControllerAuthoritative()
    {
        var root = RepoRoot();
        var vm = File.ReadAllText(Path.Combine(root, "src", "PowerFlow.App", "Dashboard", "DashboardViewModel.cs"));
        Assert.Contains("EnvelopeGovernor", vm, StringComparison.Ordinal);
        Assert.DoesNotContain("SetActiveScheme", vm, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("powercfg", vm, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PowerPlanController", vm, StringComparison.OrdinalIgnoreCase);
    }

    private static ControllerSnapshot Snapshot(PowerState state, double cpu, DateTimeOffset at, string? actor) =>
        new(state, "controller remains authoritative", false, null, cpu, 0, null, actor, at, Array.Empty<TransitionRecord>(), 0, 0);

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "PowerFlow.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException();
    }
}
