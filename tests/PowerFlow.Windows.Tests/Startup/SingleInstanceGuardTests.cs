using PowerFlow.Windows.Startup;
using Xunit;

namespace PowerFlow.Windows.Tests.Startup;

public sealed class SingleInstanceGuardTests
{
    [Fact]
    public void OnlyOneGuardOwnsANameAtATime()
    {
        var name = $"Local\\PowerFlow.Test.{Guid.NewGuid():N}";
        using (var first = SingleInstanceGuard.TryAcquire(name))
        {
            Assert.True(first.IsPrimary);
            using var second = SingleInstanceGuard.TryAcquire(name);
            Assert.False(second.IsPrimary);
        }

        using var third = SingleInstanceGuard.TryAcquire(name);
        Assert.True(third.IsPrimary);
    }
}
