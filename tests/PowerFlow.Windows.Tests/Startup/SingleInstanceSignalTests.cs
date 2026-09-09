using PowerFlow.Windows.Startup;
using Xunit;

namespace PowerFlow.Windows.Tests.Startup;

public sealed class SingleInstanceSignalTests
{
    [Fact]
    public async Task ExistingPrimaryListenerReceivesDashboardSignal()
    {
        var name = $"Local\\PowerFlow.Test.DashboardSignal.{Guid.NewGuid():N}";
        var seen = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        using var listener = SingleInstanceSignal.Listen(name, () => seen.TrySetResult());

        Assert.True(SingleInstanceSignal.TrySignal(name, TimeSpan.FromSeconds(1)));
        await seen.Task.WaitAsync(TimeSpan.FromSeconds(2));
    }
}
