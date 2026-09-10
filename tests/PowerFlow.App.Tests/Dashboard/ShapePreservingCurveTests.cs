using PowerFlow.App.Dashboard;
using Windows.Foundation;
using Xunit;

namespace PowerFlow.App.Tests.Dashboard;

public sealed class ShapePreservingCurveTests
{
    [Fact]
    public void Build_MonotoneSamplesKeepBezierControlsInsideNeighborExtrema()
    {
        Point?[] points = [new(0, 0.10), new(1, 0.35), new(2, 0.55), new(4, 0.90)];

        var figures = ShapePreservingCurve.Build(points);

        var figure = Assert.Single(figures);
        Assert.Equal(3, figure.Segments.Count);
        foreach (var segment in figure.Segments)
        {
            var min = Math.Min(segment.Start.Y, segment.End.Y);
            var max = Math.Max(segment.Start.Y, segment.End.Y);
            Assert.InRange(segment.Control1.Y, min, max);
            Assert.InRange(segment.Control2.Y, min, max);
        }
        Assert.Equal(points[0]!.Value, figure.Start);
        Assert.Equal(points[^1]!.Value, figure.Segments[^1].End);
    }

    [Fact]
    public void Build_FlatSamplesStayFlat()
    {
        Point?[] points = [new(0, 0.42), new(1, 0.42), new(2, 0.42), new(3, 0.42)];

        var figure = Assert.Single(ShapePreservingCurve.Build(points));

        Assert.All(figure.Segments, segment =>
        {
            Assert.Equal(0.42, segment.Control1.Y, 6);
            Assert.Equal(0.42, segment.Control2.Y, 6);
            Assert.Equal(0.42, segment.End.Y, 6);
        });
    }

    [Fact]
    public void Build_MissingSampleSplitsFiguresWithoutBridgingGap()
    {
        Point?[] points = [new(0, 0.2), new(1, 0.4), null, new(3, 0.7), new(4, 0.8)];

        var figures = ShapePreservingCurve.Build(points);

        Assert.Equal(2, figures.Count);
        Assert.Equal(new Point(0, 0.2), figures[0].Start);
        Assert.Equal(new Point(1, 0.4), figures[0].Segments[^1].End);
        Assert.Equal(new Point(3, 0.7), figures[1].Start);
        Assert.Equal(new Point(4, 0.8), figures[1].Segments[^1].End);
    }

    [Fact]
    public void Build_DuplicateXAndNonFiniteSamplesNeverEmitInvalidGeometry()
    {
        Point?[] points = [new(0, 0.2), new(0, 0.3), new(double.NaN, 0.4), new(2, 0.6), new(3, 0.7)];

        var figures = ShapePreservingCurve.Build(points);

        foreach (var point in figures.SelectMany(f => new[] { f.Start }.Concat(f.Segments.SelectMany(s => new[] { s.Control1, s.Control2, s.End }))))
        {
            Assert.True(double.IsFinite(point.X));
            Assert.True(double.IsFinite(point.Y));
        }
    }

    [Fact]
    public void BuildSparseObservations_SkipsControllerOnlyNullsWithoutFragmentingRichTrace()
    {
        Point?[] points =
        [
            new Point(0.0, 20),
            null,
            new Point(0.1, 18),
            null,
            new Point(0.2, 16)
        ];

        var figure = Assert.Single(ShapePreservingCurve.BuildSparseObservations(points, .15));
        Assert.Equal(2, figure.Segments.Count);
        Assert.Equal(.2, figure.Segments[^1].End.X, 3);
    }

    [Fact]
    public void BuildSparseObservations_SplitsAcrossRealTelemetryOutage()
    {
        Point?[] points =
        [
            new Point(0.0, 20),
            null,
            new Point(0.1, 18),
            null,
            new Point(0.5, 16)
        ];

        var figures = ShapePreservingCurve.BuildSparseObservations(points, .15);
        Assert.Equal(2, figures.Count);
        Assert.Single(figures[0].Segments);
        Assert.Empty(figures[1].Segments);
    }}