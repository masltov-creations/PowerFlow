using Windows.Foundation;

namespace PowerFlow.App.Dashboard.HarmonicRing;

public readonly record struct RingGeometryPoint(double X, double Y, bool StartsSegment);

/// <summary>Pure geometry. Angle follows observation time, with midnight/top marking window start;
/// a discontinuity begins a new figure rather than drawing through unavailable data.</summary>
public static class HarmonicRingGeometry
{
    public static IReadOnlyList<IReadOnlyList<RingGeometryPoint>> BuildPressureFigures(
        IReadOnlyList<RingWavePoint> history,
        double diameter,
        double inset = 16)
    {
        ArgumentNullException.ThrowIfNull(history);
        if (!double.IsFinite(diameter) || diameter <= 0 || !double.IsFinite(inset) || inset < 0)
            return Array.Empty<IReadOnlyList<RingGeometryPoint>>();
        var radius = Math.Max(0, diameter / 2 - inset);
        var center = diameter / 2;
        var figures = new List<IReadOnlyList<RingGeometryPoint>>();
        var current = new List<RingGeometryPoint>();
        foreach (var point in history)
        {
            if (!double.IsFinite(point.FractionOfWindow) || !double.IsFinite(point.PressurePercent))
                continue;
            if (point.StartsSegment && current.Count > 0)
            {
                figures.Add(current);
                current = new List<RingGeometryPoint>();
            }
            // Pressure deforms the ring inward/outward while staying inside its viewport.
            var amplitude = Math.Clamp(point.PressurePercent, 0, 100) / 100d;
            var r = radius * (0.75 + 0.25 * amplitude);
            var theta = -Math.PI / 2 + 2 * Math.PI * Math.Clamp(point.FractionOfWindow, 0, 1);
            current.Add(new RingGeometryPoint(center + Math.Cos(theta) * r,
                center + Math.Sin(theta) * r, current.Count == 0));
        }
        if (current.Count > 0) figures.Add(current);
        return figures;
    }

    public static IReadOnlyList<Point> CoreTickCenters(int count, double diameter, double inset = 9)
    {
        if (count <= 0 || count > 256 || diameter <= 0 || !double.IsFinite(diameter))
            return Array.Empty<Point>();
        var radius = Math.Max(0, diameter / 2 - Math.Max(0, inset));
        var center = diameter / 2;
        var ticks = new Point[count];
        for (var i = 0; i < count; i++)
        {
            var theta = -Math.PI / 2 + 2 * Math.PI * i / count;
            ticks[i] = new Point(center + Math.Cos(theta) * radius,
                center + Math.Sin(theta) * radius);
        }
        return ticks;
    }
}
