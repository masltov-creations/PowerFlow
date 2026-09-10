using PowerFlow.App.Dashboard;
using Xunit;

namespace PowerFlow.App.Tests.Dashboard;

public sealed class SmoothTelemetryGraphTests
{
    [Fact]
    public void SmoothProjection_CreatesOneCubicSegmentPerIntervalWithMonotonicControls()
    {
        var points = new[]
        {
            new PlotPoint(0, 80),
            new PlotPoint(100, 20),
            new PlotPoint(200, 65),
            new PlotPoint(300, 35),
        };

        var segments = SmoothGraphProjection.CreateSegments(points);

        Assert.Equal(3, segments.Count);
        for (var i = 0; i < segments.Count; i++)
        {
            var left = points[i].X;
            var right = points[i + 1].X;
            Assert.InRange(segments[i].Control1.X, left, right);
            Assert.InRange(segments[i].Control2.X, left, right);
            Assert.Equal(points[i + 1], segments[i].End);
        }
    }
    private static string RepoFile(params string[] parts)
    {
        var dir = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(dir, "PowerFlow.sln")))
            dir = Directory.GetParent(dir)?.FullName ?? throw new DirectoryNotFoundException("PowerFlow repo root not found");
        return Path.Combine(new[] { dir }.Concat(parts).ToArray());
    }
}
