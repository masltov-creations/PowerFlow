using PowerFlow.Core.Rules;
using Xunit;

namespace PowerFlow.Core.Tests.Rules;

public sealed class ThemePreferenceTests
{
    [Fact]
    public void DefaultTheme_FollowsSystem()
    {
        Assert.Equal(ThemePreference.System, PowerFlowConfig.Default.Theme);
    }
}
