using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using PowerFlow.Core.Envelope;
using Windows.Foundation;

namespace PowerFlow.App.Dashboard.HarmonicRing;

/// <summary>
/// A retained-data-only ring. Hosting shell controls the window and its animation schedule.
/// This control deliberately owns no polling or continuously running frame timer.
/// </summary>
public sealed partial class HarmonicRingControl : UserControl
{
    private HarmonicRingSnapshot? _snapshot;
    private double _diameter = 240;
    private readonly List<Ellipse> _coreTicks = new();

    public HarmonicRingControl()
    {
        InitializeComponent();
        Render();
    }

    public void UpdateSnapshot(HarmonicRingSnapshot? snapshot)
    {
        _snapshot = snapshot;
        Render();
    }

    private void OnRootSizeChanged(object sender, SizeChangedEventArgs args)
    {
        var d = Math.Max(180d, Math.Min(args.NewSize.Width, args.NewSize.Height));
        if (Math.Abs(d - _diameter) < 0.5) return;
        _diameter = d;
        Render();
    }

    private void Render()
    {
        if (DrawingSurface is null) return;
        var d = _diameter;
        RingSurface.Width = d;
        RingSurface.Height = d;
        DrawingSurface.Width = d;
        DrawingSurface.Height = d;
        SetCircle(AmbientRing, d, 11);
        SetCircle(InnerRing, d, d * 0.195);

        var current = _snapshot;
        AppliedProfileText.Text = current?.ConfirmedAppliedProfile is string profile
            ? "APPLIED " + profile.ToUpperInvariant()
            : "APPLIED UNKNOWN";
        PressureText.Text = Label(current?.Pressure, "0", "%");
        WattsText.Text = Label(current?.PackageWatts, "0.0", " W");
        ClockText.Text = Label(current?.EffectiveClockMhz, "0.00", " GHz", 0.001d);
        var active = current?.AwakeCores;
        var total = current?.TotalCores;
        CoresText.Text = active?.IsCurrent == true && total?.IsCurrent == true
            ? $"CORES {active.Value!.Value:0} / {total.Value!.Value:0}"
            : "CORES — / —";
        TrendText.Text = current?.PressureTrend switch
        {
            RingPressureTrend.Building => "PRESSURE BUILDING",
            RingPressureTrend.Ebbing => "PRESSURE EBBING",
            RingPressureTrend.Steady => "PRESSURE STEADY",
            _ => "PRESSURE UNKNOWN"
        };
        UsageText.Text = "USAGE NOT MEASURED";

        var figures = HarmonicRingGeometry.BuildPressureFigures(
            current?.PressureHistory ?? Array.Empty<RingWavePoint>(), d, Math.Max(12, d * .052));
        var waveGeometry = new PathGeometry();
        foreach (var run in figures)
        {
            if (run.Count == 0) continue;
            var start = new Point(run[0].X, run[0].Y);
            var figure = new PathFigure { StartPoint = start, IsClosed = false, IsFilled = false };
            for (var i = 1; i < run.Count; i++)
                figure.Segments.Add(new LineSegment { Point = new Point(run[i].X, run[i].Y) });
            waveGeometry.Figures.Add(figure);
        }
        PressureStroke.Data = waveGeometry;
        PressureStroke.Opacity = current?.Pressure.IsCurrent == true ? 1 : .3;
        var powerArc = new PathGeometry();
        if (current?.PackageWatts.IsCurrent == true)
        {
            // A warm *indicator arc*, not fabricated power history or an inferred %
            // of hardware TDP. Numeric watts remain the only absolute reading.
            var points = HarmonicRingGeometry.CoreTickCenters(48, d, Math.Max(16, d * .092));
            var arc = new PathFigure { StartPoint = points[5], IsFilled = false };
            for (var i = 6; i < 19; i++)
                arc.Segments.Add(new LineSegment { Point = points[i] });
            powerArc.Figures.Add(arc);
        }
        PowerAccent.Data = powerArc;
        UpdateCoreTicks(current, d);
    }

    private void UpdateCoreTicks(HarmonicRingSnapshot? snapshot, double diameter)
    {
        if (CoreTickLayer is null) return;
        var count = snapshot?.TotalCores.IsCurrent == true
            ? (int)Math.Clamp(snapshot.TotalCores.Value!.Value, 1, 64) : 0;
        if (_coreTicks.Count != count)
        {
            CoreTickLayer.Children.Clear();
            _coreTicks.Clear();
            for (var i = 0; i < count; i++)
            {
                var tick = new Ellipse { Width = 4, Height = 4 };
                _coreTicks.Add(tick);
                CoreTickLayer.Children.Add(tick);
            }
        }
        var centers = HarmonicRingGeometry.CoreTickCenters(count, diameter, 7);
        var active = snapshot?.AwakeCores.IsCurrent == true
            ? (int)Math.Clamp(snapshot.AwakeCores.Value!.Value, 0, count) : -1;
        for (var i = 0; i < _coreTicks.Count; i++)
        {
            var tick = _coreTicks[i];
            tick.Fill = i < active
                ? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 38, 210, 242))
                : new SolidColorBrush(Windows.UI.Color.FromArgb(110, 99, 121, 145));
            Canvas.SetLeft(tick, centers[i].X - 2);
            Canvas.SetTop(tick, centers[i].Y - 2);
        }
    }

    private static void SetCircle(Ellipse ring, double diameter, double inset)
    {
        Canvas.SetLeft(ring, inset);
        Canvas.SetTop(ring, inset);
        ring.Width = Math.Max(0, diameter - inset * 2);
        ring.Height = Math.Max(0, diameter - inset * 2);
    }

    private static string Label(RingReading? value, string format, string unit, double factor = 1)
    {
        if (value?.IsCurrent != true) return "—" + unit;
        return (value.Value!.Value * factor).ToString(format, System.Globalization.CultureInfo.InvariantCulture) + unit;
    }
}
