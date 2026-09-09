using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
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
    private double? _hoverNormalized;
    private bool _thresholdDragging;
    private double _plotLeft = 42;
    private double _plotTop = 19;
    private double _plotWidth = 1;
    private double _plotHeight = 1;
    private double _powerMax = 100;
    private DateTimeOffset _latest;

    public event EventHandler<ThresholdsChangedEventArgs>? ThresholdsPreviewed;
    public event EventHandler<ThresholdsChangedEventArgs>? ThresholdsCommitted;
public TelemetryGraphControl()
    {
        InitializeComponent();
        ActualThemeChanged += (_, _) => Redraw();
    }

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
        const double left = 42;
        const double right = 48;
        const double top = 19;
        const double bottom = 26;
        var plotWidth = Math.Max(1, width - left - right);
        var plotHeight = Math.Max(1, height - top - bottom);
        _plotLeft = left;
        _plotTop = top;
        _plotWidth = plotWidth;
        _plotHeight = plotHeight;

        var light = ActualTheme == ElementTheme.Light;
        var gridBrush = light ? Brush(21, 38, 55, 25) : Brush(255, 255, 255, 18);
        var labelBrush = light ? Brush(21, 38, 55, 125) : Brush(255, 255, 255, 100);
        var cpuBrush = light ? Brush(22, 146, 132, 235) : Brush(84, 224, 207, 235);
        var powerBrush = light ? Brush(73, 112, 206, 230) : Brush(125, 168, 255, 230);
        var transitionBrush = light ? Brush(21, 38, 55, 72) : Brush(255, 255, 255, 72);

        foreach (var percent in new[] { 0d, 25d, 50d, 75d, 100d })
        {
            var y = CpuY(percent, top, plotHeight);
            AddLine(left, y, left + plotWidth, y, gridBrush, 1);
            AddText($"{percent:0}%", 4, y - 7, 9, labelBrush);
        }

        var latest = _samples.Count > 0 ? _samples[^1].At : DateTimeOffset.UtcNow;
        var windowStart = latest.AddSeconds(-_windowSeconds);
        var visibleSamples = _samples.Where(sample => sample.At >= windowStart && sample.At <= latest).ToArray();
        var powerValues = visibleSamples.Where(s => s.PackageWatts.HasValue).Select(s => s.PackageWatts!.Value).ToArray();
        var powerMax = powerValues.Length == 0 ? 100d : Math.Max(100d, Math.Ceiling(powerValues.Max() / 25d) * 25d);
        _powerMax = powerMax;
        _latest = latest;
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
        UpdateThresholdOverlay();
        if (_hoverNormalized is double hover && !_thresholdDragging) UpdateHover(hover);
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_thresholdDragging) return;
        var point = e.GetCurrentPoint(InteractionCanvas).Position;
        if (_samples.Count == 0 || point.X < _plotLeft || point.X > _plotLeft + _plotWidth || point.Y < _plotTop || point.Y > _plotTop + _plotHeight)
        {
            HideHover();
            return;
        }
        _hoverNormalized = Math.Clamp((point.X - _plotLeft) / _plotWidth, 0, 1);
        UpdateHover(_hoverNormalized.Value);
    }

    private void OnPointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (_thresholdDragging) return;
        HideHover();
    }

    private void OnPromoteDragStarted(object sender, DragStartedEventArgs e) => BeginThresholdDrag();
    private void OnQuietDragStarted(object sender, DragStartedEventArgs e) => BeginThresholdDrag();

    private void OnPromoteDragDelta(object sender, DragDeltaEventArgs e)
    {
        var requested = _config.CpuPromotionThresholdPercent - (e.VerticalChange / Math.Max(1, _plotHeight) * 100d);
        requested = Math.Round(requested * 2d) / 2d;
        _config = _config with { CpuPromotionThresholdPercent = ThresholdDragProjection.ClampPromotion(requested, _config.QuietThresholdPercent) };
        PreviewThresholdChange();
    }

    private void OnQuietDragDelta(object sender, DragDeltaEventArgs e)
    {
        var requested = _config.QuietThresholdPercent - (e.VerticalChange / Math.Max(1, _plotHeight) * 100d);
        requested = Math.Round(requested * 2d) / 2d;
        _config = _config with { QuietThresholdPercent = ThresholdDragProjection.ClampQuiet(requested, _config.CpuPromotionThresholdPercent) };
        PreviewThresholdChange();
    }

    private void OnPromoteDragCompleted(object sender, DragCompletedEventArgs e) => CompleteThresholdDrag();
    private void OnQuietDragCompleted(object sender, DragCompletedEventArgs e) => CompleteThresholdDrag();

    private void BeginThresholdDrag()
    {
        _thresholdDragging = true;
        HideHover();
    }

    private void PreviewThresholdChange()
    {
        UpdateThresholdOverlay();
        ThresholdsPreviewed?.Invoke(this, new ThresholdsChangedEventArgs(_config.QuietThresholdPercent, _config.CpuPromotionThresholdPercent));
    }

    private void CompleteThresholdDrag()
    {
        if (!_thresholdDragging) return;
        _thresholdDragging = false;
        ThresholdsCommitted?.Invoke(this, new ThresholdsChangedEventArgs(_config.QuietThresholdPercent, _config.CpuPromotionThresholdPercent));
    }
    private void UpdateThresholdOverlay()
    {
        var x1 = _plotLeft;
        var x2 = _plotLeft + _plotWidth;
        var promoteY = CpuY(_config.CpuPromotionThresholdPercent, _plotTop, _plotHeight);
        var quietY = CpuY(_config.QuietThresholdPercent, _plotTop, _plotHeight);
        SetRail(PromoteRail, PromoteHitRail, PromoteHandle, PromoteLabel, x1, x2, promoteY, $"PROMOTE {_config.CpuPromotionThresholdPercent:0.#}%", -15);
        SetRail(QuietRail, QuietHitRail, QuietHandle, QuietLabel, x1, x2, quietY, $"QUIET {_config.QuietThresholdPercent:0.#}%", 2);
        SetThumb(PromoteThumb, x1, x2, promoteY);
        SetThumb(QuietThumb, x1, x2, quietY);
    }

    private static void SetThumb(Thumb thumb, double x1, double x2, double y)
    {
        thumb.Width = Math.Max(1, x2 - x1);
        Canvas.SetLeft(thumb, x1);
        Canvas.SetTop(thumb, y - 10);
    }
    private static void SetRail(Line rail, Line hitRail, Ellipse handle, TextBlock label, double x1, double x2, double y, string text, double labelOffset)
    {
        rail.X1 = hitRail.X1 = x1;
        rail.X2 = hitRail.X2 = x2;
        rail.Y1 = rail.Y2 = hitRail.Y1 = hitRail.Y2 = y;
        Canvas.SetLeft(handle, x2 - 5.5);
        Canvas.SetTop(handle, y - 5.5);
        label.Text = text;
        Canvas.SetLeft(label, Math.Max(x1, x2 - 82));
        Canvas.SetTop(label, y + labelOffset);
    }

    private void UpdateHover(double normalized)
    {
        var value = TelemetryHoverProjection.Interpolate(_samples, normalized, _latest, _windowSeconds);
        if (value is null) { HideHover(); return; }
        var x = _plotLeft + normalized * _plotWidth;
        HoverLine.X1 = HoverLine.X2 = x;
        HoverLine.Y1 = _plotTop;
        HoverLine.Y2 = _plotTop + _plotHeight;
        HoverLine.Visibility = Visibility.Visible;
        var cpuY = CpuY(value.CpuPercent, _plotTop, _plotHeight);
        Canvas.SetLeft(CpuHoverDot, x - 4);
        Canvas.SetTop(CpuHoverDot, cpuY - 4);
        CpuHoverDot.Visibility = Visibility.Visible;
        if (value.PackageWatts is double pw)
        {
            var powerY = _plotTop + _plotHeight - Math.Clamp(pw / _powerMax, 0, 1) * _plotHeight;
            Canvas.SetLeft(PowerHoverDot, x - 4);
            Canvas.SetTop(PowerHoverDot, powerY - 4);
            PowerHoverDot.Visibility = Visibility.Visible;
        }
        else PowerHoverDot.Visibility = Visibility.Collapsed;
        var watts = value.PackageWatts is double w ? $"{w:0.0} W" : "- W";
        var ghz = value.AverageMhz is double mhz ? $"{mhz / 1000d:0.00} GHz" : "- GHz";
        HoverText.Text = $"{value.At:HH:mm:ss}   CPU {value.CpuPercent:0.0}%   {watts}   {ghz}   {value.State}";
        HoverCard.Visibility = Visibility.Visible;
    }

    private void HideHover()
    {
        _hoverNormalized = null;
        HoverLine.Visibility = Visibility.Collapsed;
        CpuHoverDot.Visibility = Visibility.Collapsed;
        PowerHoverDot.Visibility = Visibility.Collapsed;
        HoverCard.Visibility = Visibility.Collapsed;
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
