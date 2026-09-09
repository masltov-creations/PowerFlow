using PowerFlow.App.Dashboard;
using Xunit;

namespace PowerFlow.App.Tests.Dashboard;

public sealed class DisclosureStateTests
{
    [Fact]
    public void ToggleKeepsOnlyOnePrimaryExpansion()
    {
        var state = DisclosureState.None;
        state = state.Toggle(DisclosureKind.Now);
        Assert.Equal(DisclosureKind.Now, state.Kind);

        state = state.Toggle(DisclosureKind.QuietRail);
        Assert.Equal(DisclosureKind.QuietRail, state.Kind);

        state = state.Toggle(DisclosureKind.QuietRail);
        Assert.Equal(DisclosureKind.None, state.Kind);
    }

    [Fact]
    public void TransitionExpansionPreservesEpisodeIdentity()
    {
        var state = DisclosureState.None.Toggle(DisclosureKind.Transition, "episode-42");
        Assert.Equal("episode-42", state.Key);
        Assert.Equal(DisclosureKind.Transition, state.Kind);
    }
}
