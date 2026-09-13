using PowerFlow.App.Dashboard;
using PowerFlow.App.Telemetry;
using PowerFlow.App.Controller;
using PowerFlow.Core.Policy;
using PowerFlow.Windows.Activity;
using Xunit;

namespace PowerFlow.App.Tests.Dashboard;

public sealed class CoreStateTimelineProjectionTests
{
    [Fact]
    public void Project_CountsMutuallyExclusivePhysicalCoreStates()
    {
        var at = DateTimeOffset.Parse("2026-09-10T20:00:00Z");
        var logical = new[]
        {
            new LogicalProcessorTelemetry(0, 0, false, 25),
            new LogicalProcessorTelemetry(1, 0, false, 1),
            new LogicalProcessorTelemetry(2, 1, false, 2),
            new LogicalProcessorTelemetry(3, 1, false, null),
            new LogicalProcessorTelemetry(4, 2, true, 90),
            new LogicalProcessorTelemetry(5, 2, true, 0)
        };

        var sample = CoreStateTimelineProjection.Project(at, logical);

        Assert.Equal(1, sample.ActiveCores);
        Assert.Equal(1, sample.AwakeIdleCores);
        Assert.Equal(1, sample.ParkedCores);
        Assert.Equal(3, sample.TotalCores);
    }

    [Fact]
    public void Project_MissingUtilizationNeverPromotesAwakeCoreToActive()
    {
        var sample = CoreStateTimelineProjection.Project(DateTimeOffset.UtcNow,
            new[] { new LogicalProcessorTelemetry(7, 3, false, null) });
        Assert.Equal(0, sample.ActiveCores);
        Assert.Equal(1, sample.AwakeIdleCores);
        Assert.Equal(0, sample.ParkedCores);
    }

    [Fact]
    public void Build_UsesOnlyRichSamplesInsideVisibleWindow()
    {
        var at = DateTimeOffset.Parse("2026-09-10T20:00:00Z");
        var logical = new[] { new LogicalProcessorTelemetry(0, 0, false, 30) };
        var history = new[]
        {
            Sample(at.AddSeconds(-70), logical),
            Sample(at.AddSeconds(-30), logical),
            Sample(at.AddSeconds(-10), null)
        };

        var data = CoreStateTimelineProjection.Build(history, at.AddSeconds(-60), at);

        Assert.Single(data.Samples);
        Assert.Equal(at.AddSeconds(-30), data.Samples[0].At);
        Assert.Equal(1, data.TotalCores);
    }

    [Fact]
    public void Build_RendersAtMostOneLatestCoreSlicePerSecondWithoutDroppingLatestBucket()
    {
        var at = DateTimeOffset.Parse("2026-09-10T20:00:02Z");
        var logical = new[] { new LogicalProcessorTelemetry(0, 0, false, 30) };
        var history = new[]
        {
            Sample(at.AddSeconds(-2.00), logical),
            Sample(at.AddSeconds(-1.75), logical),
            Sample(at.AddSeconds(-1.50), logical),
            Sample(at.AddSeconds(-1.25), logical),
            Sample(at.AddSeconds(-1.00), logical),
            Sample(at.AddSeconds(-0.75), logical),
            Sample(at.AddSeconds(-0.50), logical),
            Sample(at.AddSeconds(-0.25), logical),
            Sample(at, logical)
        };

        var data = CoreStateTimelineProjection.Build(history, at.AddSeconds(-3), at);

        Assert.Equal(3, data.Samples.Count);
        Assert.Equal(at.AddSeconds(-1.25), data.Samples[0].At);
        Assert.Equal(at.AddSeconds(-0.25), data.Samples[1].At);
        Assert.Equal(at, data.Samples[2].At);
    }
    private static ContinuitySample Sample(DateTimeOffset at, IReadOnlyList<LogicalProcessorTelemetry>? logical) =>
        new(at, 10, 40, 2000, PowerState.Balanced, "test", false, null, 0, null, 1, 1, logical);
}
