using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using PowerFlow.App.Controller;
using PowerFlow.Core.Policy;
using PowerFlow.Core.Rules;
using Windows.Foundation;

namespace PowerFlow.App.Dashboard;

public sealed partial class TelemetryGraphControl : UserControl
{
    private IReadOnlyList<DashboardSample> _samples = Array.Empty<DashboardSample>();
    private IReadOnlyList<TransitionRecord> _history = Array.Empty<TransitionRecord>();
    private PowerFlowConfig _config = PowerFlowConfig.Default;
    private double _windowSeconds = 60;
    private Line? _hoverLine;

    public TelemetryGraphControl() => InitializeComponent();

    public void Apply(IReadOnlyList<DashboardSample> samples, PowerFlowConfig config, IReadOnlyList<TransitionRecord>? history = null, double windowSeconds = 60)
    {
        _samples = samples.ToArray();
        _history = history?.ToArray() ?? Array.Empty<TransitionRecord>();
        _config = config;
        _windowSeconds = windowSeconds <= 60 ? 60 : 120;
        Redraw();
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e) => Redraw();

    private void Redraw()
    {
        var width = PlotCanvas.ActualWidth;
        var height = PlotCanvas.ActualHeight;
        if (width < 180 || height < 120) return;

        PlotCanvas.Children.Clear();
        _hoverLine = null;
        HoverCard.Visibility = Visibility.Collapsed;
        const double left = 42;
        const double right = 48;
        const double top = 19;
        const double bottom = 26;
        var plotWidth = Math.Max(1, width - left - right);
        var plotHeight = Math.Max(1, height - top - bottom);

        var gridBrush = Brush(255, 255, 255, 18);
        var labelBrush = Brush(255, 255, 255, 100);
        var cpuBrush = Brush(84, 224, 207, 235);
        var powerBrush = Brush(125, 168, 255, 230);
        var promoteBrush = Brush(220, 140, 255, 205);
        var quietBrush = Brush(84, 224, 207, 155);
        var transitionBrush = Brush(255, 255, 255, 72);

        foreach (var percent in new[] { 0d, 25d, 50d, 75d, 100d })
        {
            var y = CpuY(percent, top, plotHeight);
            AddLine(left, y, left + plotWidth, y, gridBrush, 1);
            AddText($"{percent:0}%", 4, y - 7, 9, labelBrush);
        }

        AddThreshold(_config.CpuPromotionThresholdPercent, $"PROMOTE {_config.CpuPromotionThresholdPercent:0.#}%", promoteBrush, left, top, plotWidth, plotHeight, false);
        AddThreshold(_config.QuietThresholdPercent, $"QUIET {_config.QuietThresholdPercent:0.#}%", quietBrush, left, top, plotWidth, plotHeight, true);

        var latest = _samples.Count > 0 ? _samples[^1].At : DateTimeOffset.UtcNow;
        var windowStart = latest.AddSeconds(-_windowSeconds);
        var visibleSamples = _samples.Where(sample => sample.At >= windowStart && sample.At <= latest).ToArray();
        var powerValues = visibleSamples.Where(s => s.PackageWatts.HasValue).Select(s => s.PackageWatts!.Value).ToArray();
        var powerMax = powerValues.Length == 0 ? 100d : Math.Max(100d, Math.Ceiling(powerValues.Max() / 25d) * 25d);
        AddText($"{powerMax:0} W", width - right + 6, top - 6, 9, labelBrush);
        AddText("0 W", width - right + 6, top + plotHeight - 6, 9, labelBrush);
        AddText($"-{_windowSeconds:0}s", left, height - 20, 9, labelBrush);
        AddText("NOW", left + plotWidth - 22, height - 20, 9, labelBrush);

        foreach (var transition in _history.TakeLast(8))
        {
            var marker = TelemetryPlotProjection.NormalizedTransitionX(transition, latest, _windowSeconds);
            if (marker is not double nx) continue;
            var x = left + nx * plotWidth;
            var line = AddLine(x, top, x, top + plotHeight, transitionBrush, 1);
            line.StrokeDashArray = new DoubleCollection { 2, 4 };
            AddText(ShortState(transition.To), Math.Min(x + 3, left + plotWidth - 42), top + 2, 8, transitionBrush);
        }

        if (visibleSamples.Length > 0)
        {
            var cpuPoints = visibleSamples
                .Select(sample => new PlotPoint(
                    left + TelemetryPlotProjection.NormalizedX(sample.At, latest, _windowSeconds) * plotWidth,
                    CpuY(sample.CpuPercent, top, plotHeight)))
                .ToArray();
            var powerPoints = visibleSamples
                .Where(sample => sample.PackageWatts.HasValue)
                .Select(sample => new PlotPoint(
                    left + TelemetryPlotProjection.NormalizedX(sample.At, latest, _windowSeconds) * plotWidth,
                    top + plotHeight - Math.Clamp(sample.PackageWatts!.Value / powerMax, 0, 1) * plotHeight))
                .ToArray();

            AddSmoothSeries(cpuPoints, cpuBrush, CreateAreaBrush(84, 224, 207, 48), 2.35, top + plotHeight);
            AddSmoothSeries(powerPoints, powerBrush, CreateAreaBrush(125, 168, 255, 32), 2.0, top + plotHeight);
        }
        AddLegend(left + 7, top + 5, "CPU %", cpuBrush);
        AddLegend(left + 66, top + 5, "PACKAGE W", powerBrush);
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_samples.Count == 0) return;
        const double left = 42;
        const double right = 48;
        var width = PlotCanvas.ActualWidth;
        var plotWidth = width - left - right;
        if (plotWidth <= 0) return;
        var point = e.GetCurrentPoint(PlotCanvas).Position;
        var normalized = (point.X - left) / plotWidth;
        if (normalized < 0 || normalized > 1) { OnPointerExited(sender, e); return; }
        var latest = _samples[^1].At;
        var sample = TelemetryPlotProjection.FindNearestSample(_samples, normalized, latest, _windowSeconds);
        if (sample is null) return;
        var x = left + TelemetryPlotProjection.NormalizedX(sample.At, latest, _windowSeconds) * plotWidth;
        if (_hoverLine is null)
        {
            _hoverLine = new Line { Stroke = Brush(255, 255, 255, 110), StrokeThickness = 1, IsHitTestVisible = false };
            PlotCanvas.Children.Add(_hoverLine);
        }
        _hoverLine.X1 = _hoverLine.X2 = x;
        _hoverLine.Y1 = 18;
        _hoverLine.Y2 = Math.Max(18, PlotCanvas.ActualHeight - 26);
        var watts = sample.PackageWatts is double w ? $"{w:0.0} W" : "— W";
        var ghz = sample.AverageMhz is double mhz ? $"{mhz / 1000d:0.00} GHz" : "— GHz";
        HoverText.Text = $"{sample.At:HH:mm:ss}   CPU {sample.CpuPercent:0.0}%   {watts}   {ghz}   {sample.State}";
        HoverCard.Visibility = Visibility.Visible;
    }

    private void OnPointerExited(object sender, PointerRoutedEventArgs e)
    {
        HoverCard.Visibility = Visibility.Collapsed;
        if (_hoverLine is not null)
        {
            PlotCanvas.Children.Remove(_hoverLine);
            _hoverLine = null;
        }
    }

    private void AddSmoothSeries(IReadOnlyList<PlotPoint> points, SolidColorBrush stroke, Brush areaFill, double thickness, double baseline)
    {
        if (points.Count == 0) return;
        if (points.Count > 1)
        {
            var area = new Microsoft.UI.Xaml.Shapes.Path
            {
                Data = BuildSplineGeometry(points, closeToBaseline: true, baseline),
                Fill = areaFill,
                StrokeThickness = 0,
                IsHitTestVisible = false
            };
            PlotCanvas.Children.Add(area);

            var glow = new Microsoft.UI.Xaml.Shapes.Path
            {
                Data = BuildSplineGeometry(points, closeToBaseline: false, baseline),
                Stroke = stroke,
                StrokeThickness = thickness + 6,
                StrokeLineJoin = PenLineJoin.Round,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                Opacity = 0.10,
                IsHitTestVisible = false
            };
            PlotCanvas.Children.Add(glow);

            var line = new Microsoft.UI.Xaml.Shapes.Path
            {
                Data = BuildSplineGeometry(points, closeToBaseline: false, baseline),
                Stroke = stroke,
                StrokeThickness = thickness,
                StrokeLineJoin = PenLineJoin.Round,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                IsHitTestVisible = false
            };
            PlotCanvas.Children.Add(line);
        }
        AddLiveMarker(points[^1], stroke);
    }

    private static PathGeometry BuildSplineGeometry(IReadOnlyList<PlotPoint> points, bool closeToBaseline, double baseline)
    {
        var figure = new PathFigure
        {
            StartPoint = new Point(points[0].X, points[0].Y),
            IsClosed = closeToBaseline,
            IsFilled = closeToBaseline
        };
        foreach (var segment in SmoothGraphProjection.CreateSegments(points))
        {
            figure.Segments.Add(new BezierSegment
            {
                Point1 = new Point(segment.Control1.X, segment.Control1.Y),
                Point2 = new Point(segment.Control2.X, segment.Control2.Y),
                Point3 = new Point(segment.End.X, segment.End.Y)
            });
        }
        if (closeToBaseline)
        {
            figure.Segments.Add(new LineSegment { Point = new Point(points[^1].X, baseline) });
            figure.Segments.Add(new LineSegment { Point = new Point(points[0].X, baseline) });
        }
        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        return geometry;
    }

    private void AddLiveMarker(PlotPoint point, Brush brush)
    {
        var halo = new Ellipse { Width = 14, Height = 14, Fill = brush, Opacity = 0.12, IsHitTestVisible = false };
        Canvas.SetLeft(halo, point.X - 7);
        Canvas.SetTop(halo, point.Y - 7);
        PlotCanvas.Children.Add(halo);
        var dot = new Ellipse { Width = 5, Height = 5, Fill = brush, Opacity = 0.95, IsHitTestVisible = false };
        Canvas.SetLeft(dot, point.X - 2.5);
        Canvas.SetTop(dot, point.Y - 2.5);
        PlotCanvas.Children.Add(dot);
    }

    private static LinearGradientBrush CreateAreaBrush(byte r, byte g, byte b, byte topAlpha)
    {
        var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        brush.GradientStops.Add(new GradientStop { Color = Microsoft.UI.ColorHelper.FromArgb(topAlpha, r, g, b), Offset = 0 });
        brush.GradientStops.Add(new GradientStop { Color = Microsoft.UI.ColorHelper.FromArgb(3, r, g, b), Offset = 1 });
        return brush;
    }
    private void AddThreshold(double percent, string label, Brush brush, double left, double top, double plotWidth, double plotHeight, bool labelBelow)
    {
        var y = CpuY(percent, top, plotHeight);
        var line = AddLine(left, y, left + plotWidth, y, brush, 1.25);
        line.StrokeDashArray = new DoubleCollection { 5, 4 };
        AddText(label, left + plotWidth - 80, y + (labelBelow ? 2 : -15), 8, brush);
    }

    private Line AddLine(double x1, double y1, double x2, double y2, Brush stroke, double thickness)
    {
        var line = new Line { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Stroke = stroke, StrokeThickness = thickness };
        PlotCanvas.Children.Add(line);
        return line;
    }

    private void AddText(string text, double x, double y, double size, Brush foreground)
    {
        var block = new TextBlock { Text = text, FontSize = size, Foreground = foreground, IsHitTestVisible = false };
        Canvas.SetLeft(block, x);
        Canvas.SetTop(block, y);
        PlotCanvas.Children.Add(block);
    }

    private void AddLegend(double x, double y, string text, Brush brush)
    {
        var dot = new Ellipse { Width = 5, Height = 5, Fill = brush, IsHitTestVisible = false };
        Canvas.SetLeft(dot, x);
        Canvas.SetTop(dot, y + 4);
        PlotCanvas.Children.Add(dot);
        AddText(text, x + 9, y, 9, brush);
    }

    private static string ShortState(PowerState state) => state switch
    {
        PowerState.PowerSaver => "SAVER",
        PowerState.Balanced => "BAL",
        PowerState.HighPerformance => "PERF",
        _ => state.ToString().ToUpperInvariant()
    };

    private static double CpuY(double percent, double top, double plotHeight) => top + plotHeight - Math.Clamp(percent / 100d, 0, 1) * plotHeight;
    private static SolidColorBrush Brush(byte r, byte g, byte b, byte a) => new(Microsoft.UI.ColorHelper.FromArgb(a, r, g, b));
}
