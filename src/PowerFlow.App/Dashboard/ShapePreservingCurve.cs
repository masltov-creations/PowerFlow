using Windows.Foundation;

namespace PowerFlow.App.Dashboard;

public readonly record struct CubicCurveSegment(Point Start, Point Control1, Point Control2, Point End);

public sealed record CurveFigure(Point Start, IReadOnlyList<CubicCurveSegment> Segments);

public static class ShapePreservingCurve
{
    public static IReadOnlyList<CurveFigure> Build(IReadOnlyList<Point?> points)
    {
        if (points is null || points.Count == 0) return Array.Empty<CurveFigure>();

        var figures = new List<CurveFigure>();
        var run = new List<Point>();
        foreach (var candidate in points)
        {
            if (candidate is not Point point || !IsFinite(point) || (run.Count > 0 && point.X <= run[^1].X))
            {
                FlushRun(run, figures);
                if (candidate is Point restart && IsFinite(restart)) run.Add(restart);
                continue;
            }
            run.Add(point);
        }
        FlushRun(run, figures);
        return figures;
    }

    public static IReadOnlyList<CurveFigure> BuildSparseObservations(IReadOnlyList<Point?> points, double maximumGapX)
    {
        if (points is null || points.Count == 0) return Array.Empty<CurveFigure>();
        var maxGap = double.IsFinite(maximumGapX) && maximumGapX > 0 ? maximumGapX : double.PositiveInfinity;
        var figures = new List<CurveFigure>();
        var run = new List<Point>();
        foreach (var candidate in points)
        {
            if (candidate is not Point point || !IsFinite(point)) continue;
            if (run.Count > 0 && (point.X <= run[^1].X || point.X - run[^1].X > maxGap))
                FlushRun(run, figures);
            run.Add(point);
        }
        FlushRun(run, figures);
        return figures;
    }
    private static void FlushRun(List<Point> run, List<CurveFigure> figures)
    {
        if (run.Count == 0) return;
        figures.Add(BuildRun(run));
        run.Clear();
    }

    private static CurveFigure BuildRun(IReadOnlyList<Point> points)
    {
        if (points.Count == 1) return new CurveFigure(points[0], Array.Empty<CubicCurveSegment>());

        var n = points.Count;
        var h = new double[n - 1];
        var delta = new double[n - 1];
        for (var i = 0; i < n - 1; i++)
        {
            h[i] = points[i + 1].X - points[i].X;
            delta[i] = (points[i + 1].Y - points[i].Y) / h[i];
        }

        var tangent = new double[n];
        if (n == 2)
        {
            tangent[0] = tangent[1] = delta[0];
        }
        else
        {
            tangent[0] = EndpointSlope(h[0], h[1], delta[0], delta[1]);
            tangent[^1] = EndpointSlope(h[^1], h[^2], delta[^1], delta[^2]);
            for (var i = 1; i < n - 1; i++)
            {
                if (delta[i - 1] == 0 || delta[i] == 0 || Math.Sign(delta[i - 1]) != Math.Sign(delta[i]))
                {
                    tangent[i] = 0;
                    continue;
                }
                var w1 = 2 * h[i] + h[i - 1];
                var w2 = h[i] + 2 * h[i - 1];
                tangent[i] = (w1 + w2) / (w1 / delta[i - 1] + w2 / delta[i]);
            }
        }

        var segments = new List<CubicCurveSegment>(n - 1);
        for (var i = 0; i < n - 1; i++)
        {
            var start = points[i];
            var end = points[i + 1];
            var span = h[i];
            var c1 = new Point(start.X + span / 3d, start.Y + tangent[i] * span / 3d);
            var c2 = new Point(end.X - span / 3d, end.Y - tangent[i + 1] * span / 3d);
            var min = Math.Min(start.Y, end.Y);
            var max = Math.Max(start.Y, end.Y);
            c1 = new Point(c1.X, Math.Clamp(c1.Y, min, max));
            c2 = new Point(c2.X, Math.Clamp(c2.Y, min, max));
            segments.Add(new CubicCurveSegment(start, c1, c2, end));
        }
        return new CurveFigure(points[0], segments);
    }

    private static double EndpointSlope(double h0, double h1, double d0, double d1)
    {
        var slope = ((2 * h0 + h1) * d0 - h0 * d1) / (h0 + h1);
        if (Math.Sign(slope) != Math.Sign(d0)) return 0;
        if (Math.Sign(d0) != Math.Sign(d1) && Math.Abs(slope) > Math.Abs(3 * d0)) return 3 * d0;
        return slope;
    }

    private static bool IsFinite(Point point) => double.IsFinite(point.X) && double.IsFinite(point.Y);
}