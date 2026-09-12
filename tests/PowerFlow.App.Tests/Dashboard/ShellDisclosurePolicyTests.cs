using PowerFlow.App.Dashboard;
using Xunit;

namespace PowerFlow.App.Tests.Dashboard;

public sealed class ShellDisclosurePolicyTests
{
    [Fact]
    public void CanonicalStatesIncreaseDisclosureFromPeekThroughWorkspace()
    {
        var peek = ShellDisclosurePolicy.Progress(new ShellLogicalSize(320, 176), PowerFlowShellState.Glance);
        var live = ShellDisclosurePolicy.Progress(new ShellLogicalSize(760, 440), PowerFlowShellState.Compact);
        var dashboard = ShellDisclosurePolicy.Progress(new ShellLogicalSize(1280, 800), PowerFlowShellState.Expanded);
        var workspace = ShellDisclosurePolicy.Progress(new ShellLogicalSize(1360, 860), PowerFlowShellState.Workspace);
        Assert.Equal(0d, peek, 6);
        Assert.InRange(live, .10d, .30d);
        Assert.InRange(dashboard, .70d, .90d);
        Assert.Equal(1d, workspace, 6);
    }

    [Fact]
    public void ManualResizeContinuouslyIncreasesDisclosureWithoutChangingState()
    {
        var sizes = new[]
        {
            new ShellLogicalSize(760, 440),
            new ShellLogicalSize(860, 520),
            new ShellLogicalSize(900, 560),
            new ShellLogicalSize(1100, 680),
            new ShellLogicalSize(1280, 800)
        };
        var values = sizes.Select(s => ShellDisclosurePolicy.Progress(s, PowerFlowShellState.Compact)).ToArray();
        for (var i = 1; i < values.Length; i++) Assert.True(values[i] > values[i - 1], $"Disclosure did not increase at {sizes[i]}");
        Assert.All(values, v => Assert.InRange(v, 0d, 1d));
    }

    [Theory]
    [InlineData(PowerFlowShellState.Workspace)]
    [InlineData(PowerFlowShellState.FullScreen)]
    public void DeepStatesExposeCompleteWorkspaceEvenWhenDisplayClampsSize(PowerFlowShellState state)
        => Assert.Equal(1d, ShellDisclosurePolicy.Progress(new ShellLogicalSize(1180, 720), state), 6);

    [Fact]
    public void SemanticWindowsAreSmoothAndOrdered()
    {
        var low = ShellDisclosurePolicy.ContextProgress(.20d);
        var middle = ShellDisclosurePolicy.ContextProgress(.50d);
        var high = ShellDisclosurePolicy.ContextProgress(.85d);
        Assert.True(low < middle && middle < high);
        Assert.InRange(ShellDisclosurePolicy.FooterProgress(.20d), 0d, .05d);
        Assert.InRange(ShellDisclosurePolicy.FooterProgress(.95d), .95d, 1d);
    }
}