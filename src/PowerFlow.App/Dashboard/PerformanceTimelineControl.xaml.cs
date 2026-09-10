using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using PowerFlow.Core.Envelope;
using Windows.Foundation;

namespace PowerFlow.App.Dashboard;

public sealed class TimelineCursorChangedEventArgs(int? observationIndex) : EventArgs
{
    public int? ObservationIndex { get; } = observationIndex;
}

public sealed partial class PerformanceTimelineControl : UserControl
{
    private static readonly OperatingEnvelope DefaultEnvelope = new(25, 55, 80, null, null, null);
    private IReadOnlyList<OperatingObservation> _observations = Array.Empty<OperatingObservation>();
    private OperatingEnvelope _envelope = DefaultEnvelope;
    private PerformanceTimelineData _data = PerformanceTimelineProjection.Build(Array.Empty<OperatingObservation>());
    private double _windowSeconds = 60;
    private double _plotWidth = 1;
    private double _plotHeight = 1;

    public PerformanceTimelineControl()
    {
        InitializeComponent();
        ActualThemeChanged += (_, _) => Redraw();
    }

    public TimelinePresentation Presentation { get; private set; } = TimelinePresentation.Expanded;

    public void SetPresentation(TimelinePresentation presentation)
    {
        Presentation = presentation;
        var glance = presentation == TimelinePresentation.Glance;
        TimelineHeader.Visibility = glance ? Visibility.Collapsed : Visibility.Visible;
        GlanceSummary.Visibility = glance ? Visibility.Visible : Visibility.Collapsed;
        LaneLabels.Visibility = glance ? Visibility.Collapsed : Visibility.Visible;
        TimelineFooter.Visibility = glance ? Visibility.Collapsed : Visibility.Visible;
        LaneLabelColumn.Width = new GridLength(glance ? 0 : presentation == TimelinePresentation.Compact ? 96 : 112);
        TimelineRoot.MinHeight = presentation switch
        {
            TimelinePresentation.Glance => 72,
            TimelinePresentation.Compact => 170,
            TimelinePresentation.Expanded => 280,
            _ => 340
        };
        TimelinePlotHost.MinHeight = presentation switch
        {
            TimelinePresentation.Glance => 58,
            TimelinePresentation.Compact => 132,
            TimelinePresentation.Expanded => 238,
            _ => 300
        };
        Redraw();
    }
    public event EventHandler<TimelineCursorChangedEventArgs>? CursorChanged;

    public void Apply(
        IReadOnlyList<OperatingObservation> observations,
        OperatingEnvelope? envelope = null,
        double windowSeconds = 60,
        PerformanceTimelineMode mode = PerformanceTimelineMode.Stacked)
    {
        _observations = observations?.ToArray() ?? Array.Empty<OperatingObservation>();
        _envelope = envelope ?? DefaultEnvelope;
        _windowSeconds = Math.Max(1, windowSeconds);
        _data = PerformanceTimelineProjection.Build(_observations, _windowSeconds, mode);
        WindowLabel.Text = mode == PerformanceTimelineMode.NormalizedOverlay ? $"{_windowSeconds:0} SEC · NORMALIZED" : $"{_windowSeconds:0} SEC";
        WindowStartLabel.Text = $"-{_windowSeconds:0}s";
        UpdateLiveLabels();
        Redraw();
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e) => Redraw();

    private void Redraw()
    {
        var width = PlotCanvas.ActualWidth;
        var height = PlotCanvas.ActualHeight;
        if (width < 80 || height < 80) return;

        _plotWidth = width;
        _plotHeight = height;
        PlotCanvas.Children.Clear();
        EnvelopeRailLayer.Children.Clear();
        ActorDecisionLayer.Children.Clear();

        var laneHeight = height / 4d;
        var light = ActualTheme == ElementTheme.Light;
        var grid = light ? Brush(17, 34, 51, 24) : Brush(255, 255, 255, 18);
        var text = light ? Brush(17, 34, 51, 118) : Brush(255, 255, 255, 105);
        var series = new[]
        {
            light ? Brush(12, 145, 132, 235) : Brush(80, 224, 207, 240),
            light ? Brush(68, 105, 196, 225) : Brush(123, 168, 255, 235),
            light ? Brush(136, 88, 192, 225) : Brush(190, 143, 255, 235),
            light ? Brush(194, 112, 43, 225) : Brush(255, 177, 92, 235)
        };

        for (var lane = 0; lane < 4; lane++)
        {
            var top = lane * laneHeight;
            if (lane > 0) AddLine(PlotCanvas, 0, top, width, top, grid, 1);
            AddLine(PlotCanvas, 0, top + laneHeight * .5, width, top + laneHeight * .5, grid, 1);
            DrawLane(_data.Lanes[lane], top + 4, Math.Max(1, laneHeight - 8), series[lane]);
            AddText(PlotCanvas, FormatDomain(_data.Lanes[lane]), Math.Max(0, width - 65), top + 2, 11, text);
        }

        DrawEnvelopeRails(laneHeight);
        DrawDecisionEvents(height);
    }

    private void DrawLane(TimelineLaneProjection lane, double top, double height, Brush stroke)
    {
        var group = new List<Point>();
        foreach (var point in lane.Points)
        {
            if (point.Y is not double y)
            {
                Flush(group, stroke);
                continue;
            }
            group.Add(new Point(point.X * _plotWidth, top + (1 - y) * height));
        }
        Flush(group, stroke);

        void Flush(List<Point> points, Brush brush)
        {
            if (points.Count == 0) return;
            if (points.Count == 1)
            {
                var dot = new Ellipse { Width = 4, Height = 4, Fill = brush, IsHitTestVisible = false };
                Canvas.SetLeft(dot, points[0].X - 2);
                Canvas.SetTop(dot, points[0].Y - 2);
                PlotCanvas.Children.Add(dot);
            }
            else
            {
                var polyline = new Polyline
                {
                    Stroke = brush,
                    StrokeThickness = 1.9,
                    StrokeLineJoin = PenLineJoin.Round,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round,
                    IsHitTestVisible = false
                };
                foreach (var p in points) polyline.Points.Add(p);
                PlotCanvas.Children.Add(polyline);
            }
            points.Clear();
        }
    }

    private void DrawEnvelopeRails(double cpuLaneHeight)
    {
        var light = ActualTheme == ElementTheme.Light;
        var rails = new[]
        {
            (_envelope.EcoCeilingPressure, "ECO", light ? Brush(52, 139, 88, 150) : Brush(104, 211, 142, 165)),
            (_envelope.EfficientCeilingPressure, "EFFICIENT", light ? Brush(24, 130, 161, 165) : Brush(75, 202, 235, 175)),
            (_envelope.ResponsiveCeilingPressure, "RESPONSIVE", light ? Brush(142, 92, 189, 165) : Brush(203, 148, 255, 175))
        };
        foreach (var rail in rails)
        {
            var y = 4 + (1 - Math.Clamp(rail.Item1 / 100d, 0, 1)) * Math.Max(1, cpuLaneHeight - 8);
            var line = AddLine(EnvelopeRailLayer, 0, y, _plotWidth, y, rail.Item3, 1);
            line.StrokeDashArray = new DoubleCollection { 4, 5 };
            AddText(EnvelopeRailLayer, rail.Item2, 4, Math.Max(0, y - 14), 11, rail.Item3);
        }
    }

    private void DrawDecisionEvents(double height)
    {
        var light = ActualTheme == ElementTheme.Light;
        foreach (var marker in _data.Events.TakeLast(16))
        {
            var x = marker.X * _plotWidth;
            var brush = marker.Decision switch
            {
                EnvelopeDecisionKind.Brake => light ? Brush(194, 91, 47, 185) : Brush(255, 133, 89, 205),
                EnvelopeDecisionKind.Lease => light ? Brush(80, 85, 190, 185) : Brush(148, 153, 255, 205),
                EnvelopeDecisionKind.Qualifying => light ? Brush(163, 128, 33, 175) : Brush(245, 204, 92, 195),
                _ => light ? Brush(40, 61, 76, 90) : Brush(255, 255, 255, 80)
            };
            var line = AddLine(ActorDecisionLayer, x, 0, x, height, brush, marker.Decision == EnvelopeDecisionKind.Brake ? 1.5 : 1);
            line.StrokeDashArray = new DoubleCollection { 2, 4 };
            if (!string.IsNullOrWhiteSpace(marker.Actor) || marker.Decision != EnvelopeDecisionKind.None)
            {
                var caption = string.Join(" · ", new[] { ShortActor(marker.Actor), marker.Decision == EnvelopeDecisionKind.None ? marker.Zone.ToString() : marker.Decision.ToString().ToUpperInvariant() }.Where(x => !string.IsNullOrWhiteSpace(x)));
                AddText(ActorDecisionLayer, caption, Math.Clamp(x + 3, 3, Math.Max(3, _plotWidth - 120)), 3, 11, brush);
            }
        }
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_data.Lanes.Count == 0 || _data.Lanes[0].Points.Count == 0) return;
        var p = e.GetCurrentPoint(CursorLayer).Position;
        if (p.X < 0 || p.X > _plotWidth || p.Y < 0 || p.Y > _plotHeight)
        {
            HideCursor();
            return;
        }

        var normalized = Math.Clamp(p.X / Math.Max(1, _plotWidth), 0, 1);
        var index = PerformanceTimelineProjection.FindNearestObservationIndex(_data, normalized);
        if (index is not int observationIndex || observationIndex < 0 || observationIndex >= _observations.Count)
        {
            HideCursor();
            return;
        }

        var observation = _observations[observationIndex];
        var x = _data.Lanes[0].Points.First(point => point.ObservationIndex == observationIndex).X * _plotWidth;
        InspectionCursor.X1 = InspectionCursor.X2 = x;
        InspectionCursor.Y1 = 0;
        InspectionCursor.Y2 = _plotHeight;
        InspectionCursor.Visibility = Visibility.Visible;
        CursorReadout.Text = FormatObservation(observation);
        CursorCard.Visibility = Visibility.Visible;
        CursorChanged?.Invoke(this, new TimelineCursorChangedEventArgs(observationIndex));
    }

    private void OnPointerExited(object sender, PointerRoutedEventArgs e) => HideCursor();

    private void HideCursor()
    {
        InspectionCursor.Visibility = Visibility.Collapsed;
        CursorCard.Visibility = Visibility.Collapsed;
        CursorChanged?.Invoke(this, new TimelineCursorChangedEventArgs(null));
    }

    private void UpdateLiveLabels()
    {
        var latest = _observations.OrderBy(x => x.At).LastOrDefault();
        if (latest is null)
        {
            CpuValueText.Text = PowerValueText.Text = ClockValueText.Text = CoresValueText.Text = "—";
            EnvelopeBadgeText.Text = "LEARNING";
            GlanceSummary.Text = "LEARNING · waiting for telemetry";
            return;
        }
        CpuValueText.Text = $"{latest.CpuPressurePercent:0.0}%";
        PowerValueText.Text = latest.PackageWatts is double watts ? $"{watts:0.0} W" : "—";
        ClockValueText.Text = latest.EffectiveClockMhz is double mhz ? $"{mhz / 1000d:0.00} GHz" : "—";
        CoresValueText.Text = latest.ActiveCores is int active
            ? latest.TotalCores is int total ? $"{active}/{total}" : active.ToString()
            : latest.TotalCores is int knownTotal ? $"—/{knownTotal}" : "—";
        var actor = ShortActor(latest.Actor);
        var actorPart = string.IsNullOrWhiteSpace(actor) ? string.Empty : $" · {actor}";
        var decisionPart = latest.Decision == EnvelopeDecisionKind.None ? string.Empty : $" · {latest.Decision.ToString().ToUpperInvariant()}";
        GlanceSummary.Text = $"{latest.Zone.ToString().ToUpperInvariant()} · {PowerValueText.Text} · {ClockValueText.Text} · {CoresValueText.Text}{actorPart}{decisionPart}";
        EnvelopeBadgeText.Text = latest.Decision switch
        {
            EnvelopeDecisionKind.Brake => $"{latest.Zone.ToString().ToUpperInvariant()} · BRAKING",
            EnvelopeDecisionKind.Lease => $"{latest.Zone.ToString().ToUpperInvariant()} · LEASE",
            EnvelopeDecisionKind.Qualifying => $"{latest.Zone.ToString().ToUpperInvariant()} · QUALIFYING",
            _ => latest.Zone.ToString().ToUpperInvariant()
        };
    }

    private static string FormatObservation(OperatingObservation value)
    {
        var watts = value.PackageWatts is double w ? $"{w:0.0} W" : "— W";
        var ghz = value.EffectiveClockMhz is double mhz ? $"{mhz / 1000d:0.00} GHz" : "— GHz";
        var cores = value.ActiveCores is int active ? value.TotalCores is int total ? $"{active}/{total} cores" : $"{active} cores" : "— cores";
        var actor = string.IsNullOrWhiteSpace(value.Actor) ? string.Empty : $" · {ShortActor(value.Actor)}";
        var decision = value.Decision == EnvelopeDecisionKind.None ? string.Empty : $" · {value.Decision}";
        return $"{value.At:HH:mm:ss} · CPU {value.CpuPressurePercent:0.0}% · {watts} · {ghz} · {cores} · {value.Zone}{actor}{decision}";
    }

    private static string ShortActor(string? actor)
    {
        if (string.IsNullOrWhiteSpace(actor)) return string.Empty;
        try { return System.IO.Path.GetFileNameWithoutExtension(actor); }
        catch { return actor; }
    }

    private static string FormatDomain(TimelineLaneProjection lane) => lane.Metric switch
    {
        PerformanceTimelineMetric.CpuPressure => "100%",
        PerformanceTimelineMetric.PackagePower => $"{lane.DomainMax:0} W",
        PerformanceTimelineMetric.EffectiveClock => $"{lane.DomainMax / 1000d:0.0} GHz",
        PerformanceTimelineMetric.ActiveCores => $"{lane.DomainMax:0}",
        _ => string.Empty
    };

    private static Line AddLine(Canvas canvas, double x1, double y1, double x2, double y2, Brush stroke, double thickness)
    {
        var line = new Line { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Stroke = stroke, StrokeThickness = thickness, IsHitTestVisible = false };
        canvas.Children.Add(line);
        return line;
    }

    private static void AddText(Canvas canvas, string text, double x, double y, double size, Brush foreground)
    {
        var block = new TextBlock { Text = text, FontSize = size, Foreground = foreground, IsHitTestVisible = false };
        Canvas.SetLeft(block, x);
        Canvas.SetTop(block, y);
        canvas.Children.Add(block);
    }

    private static SolidColorBrush Brush(byte r, byte g, byte b, byte a) => new(Microsoft.UI.ColorHelper.FromArgb(a, r, g, b));
}
