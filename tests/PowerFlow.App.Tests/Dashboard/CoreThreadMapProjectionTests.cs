using PowerFlow.App.Dashboard;
using PowerFlow.Windows.Activity;
using Xunit;

namespace PowerFlow.App.Tests.Dashboard;

public sealed class CoreThreadMapProjectionTests
{
    [Fact]
    public void Build_GroupsLogicalSiblingsByPhysicalCoreAndClassifiesTruthfully()
    {
        var source = new[]
        {
            new LogicalProcessorTelemetry(0, 0, false, 42),
            new LogicalProcessorTelemetry(1, 0, false, 1),
            new LogicalProcessorTelemetry(2, 1, true, 88),
            new LogicalProcessorTelemetry(3, 1, false, null)
        };

        var map = CoreThreadMapProjection.Build(source);

        Assert.Equal(2, map.Cores.Count);
        Assert.Equal(new[] { 0, 1 }, map.Cores.Select(core => core.PhysicalCoreIndex));
        Assert.Equal(ThreadOccupancyState.Active, map.Cores[0].Threads[0].State);
        Assert.Equal(ThreadOccupancyState.AwakeIdle, map.Cores[0].Threads[1].State);
        Assert.Equal(ThreadOccupancyState.Parked, map.Cores[1].Threads[0].State);
        Assert.Equal(ThreadOccupancyState.AwakeIdle, map.Cores[1].Threads[1].State);
        Assert.Equal(1, map.ActiveThreads);
        Assert.Equal(1, map.ParkedThreads);
        Assert.Equal(3, map.AwakeThreads);
        Assert.Equal(2, map.AwakeCores);
    }

    [Fact]
    public void Build_DoesNotInventActiveStateWhenUtilizationIsMissing()
    {
        var map = CoreThreadMapProjection.Build(new[] { new LogicalProcessorTelemetry(7, 3, false, null) });

        Assert.Equal(ThreadOccupancyState.AwakeIdle, Assert.Single(Assert.Single(map.Cores).Threads).State);
        Assert.Equal(0, map.ActiveThreads);
        Assert.Equal(1, map.AwakeThreads);
    }
}
