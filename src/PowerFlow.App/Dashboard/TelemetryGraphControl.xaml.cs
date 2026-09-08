using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using PowerFlow.Core.Rules;
using Windows.Foundation;

namespace PowerFlow.App.Dashboard;

public sealed partial class TelemetryGraphControl : UserControl
{
    private IReadOnlyList<DashboardSample> _samples = Array.Empty<DashboardSample>();
    private PowerFlowConfig _config = PowerFlowConfig.Default;

    public TelemetryGraphControl() => InitializeComponent();

    public void Apply(IReadOnlyList<DashboardSample> samples, PowerFlowConfig config)
    {
        _samples = samples.ToArray();
        _config = config;
        Redraw();
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e) => Redraw();

    private void Redraw()
    {
        var width = PlotCanvas.ActualWidth;
        var height = PlotCanvas.ActualHeight;
        if (width < 180 || height < 140) return;

        PlotCanvas.Children.Clear();
        const double left = 46;
        const double right = 54;
        const double top = 22;
        const double bottom = 30;
        var plotWidth = Math.Max(1, width - left - right);
        var plotHeight = Math.Max(1, height - top - bottom);

        var gridBrush = Brush(255, 255, 255, 22);
        var labelBrush = Brush(255, 255, 255, 105);
        var cpuBrush = Brush(84, 224, 207, 235);
        var powerBrush = Brush(125, 168, 255, 230);
        var promoteBrush = Brush(220, 140, 255, 205);
        var quietBrush = Brush(84, 224, 207, 155);

        foreach (var percent in new[] { 0d, 25d, 50d, 75d, 100d })
        {
            var y = CpuY(percent, top, plotHeight);
            AddLine(left, y, left + plotWidth, y, gridBrush, 1);
            AddText($"{percent:0}%", 6, y - 8, 10, labelBrush);
        }

        AddThreshold(_config.CpuPromotionThresholdPercent, $"PROMOTE {_config.CpuPromotionThresholdPercent:0.#}%", promoteBrush, left, top, plotWidth, plotHeight, false);
        AddThreshold(_config.QuietThresholdPercent, $"QUIET {_config.QuietThresholdPercent:0.#}%", quietBrush, left, top, plotWidth, plotHeight, true);

        var powerValues = _samples.Where(s => s.PackageWatts.HasValue).Select(s => s.PackageWatts!.Value).ToArray();
        var powerMax = powerValues.Length == 0 ? 100d : Math.Max(100d, Math.Ceiling(powerValues.Max() / 25d) * 25d);
        AddText($"{powerMax:0} W", width - right + 8, top - 7, 10, labelBrush);
        AddText("0 W", width - right + 8, top + plotHeight - 7, 10, labelBrush);
        AddText("-60s", left, height - 23, 10, labelBrush);
        AddText("NOW", left + plotWidth - 24, height - 23, 10, labelBrush);

        if (_samples.Count > 0)
        {
            var cpu = new Polyline { Stroke = cpuBrush, StrokeThickness = 2.4, StrokeLineJoin = PenLineJoin.Round };
            var power = new Polyline { Stroke = powerBrush, StrokeThickness = 2.1, StrokeLineJoin = PenLineJoin.Round, Opacity = 0.9 };
            for (var i = 0; i < _samples.Count; i++)
            {
                var sample = _samples[i];
                var x = left + TelemetryPlotProjection.NormalizedX(sample.At, _samples[^1].At) * plotWidth;
                cpu.Points.Add(new Point(x, CpuY(sample.CpuPercent, top, plotHeight)));
                if (sample.PackageWatts is double watts)
                {
                    var y = top + plotHeight - Math.Clamp(watts / powerMax, 0, 1) * plotHeight;
                    power.Points.Add(new Point(x, y));
                }
            }
            PlotCanvas.Children.Add(cpu);
            if (power.Points.Count > 0) PlotCanvas.Children.Add(power);
        }

        AddLegend(left + 8, top + 6, "CPU %", cpuBrush);
        AddLegend(left + 78, top + 6, "PACKAGE W", powerBrush);
    }

    private void AddThreshold(double percent, string label, Brush brush, double left, double top, double plotWidth, double plotHeight, bool labelBelow)
    {
        var y = CpuY(percent, top, plotHeight);
        var line = AddLine(left, y, left + plotWidth, y, brush, 1.35);
        line.StrokeDashArray = new DoubleCollection { 5, 4 };
        AddText(label, left + plotWidth - 88, y + (labelBelow ? 3 : -17), 9, brush);
    }

    private Line AddLine(double x1, double y1, double x2, double y2, Brush stroke, double thickness)
    {
        var line = new Line { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Stroke = stroke, StrokeThickness = thickness };
        PlotCanvas.Children.Add(line);
        return line;
    }

    private void AddText(string text, double x, double y, double size, Brush foreground)
    {
        var block = new TextBlock { Text = text, FontSize = size, Foreground = foreground };
        Canvas.SetLeft(block, x);
        Canvas.SetTop(block, y);
        PlotCanvas.Children.Add(block);
    }

    private void AddLegend(double x, double y, string text, Brush brush)
    {
        var dot = new Ellipse { Width = 6, Height = 6, Fill = brush };
        Canvas.SetLeft(dot, x);
        Canvas.SetTop(dot, y + 4);
        PlotCanvas.Children.Add(dot);
        AddText(text, x + 10, y, 10, brush);
    }

    private static double CpuY(double percent, double top, double plotHeight) => top + plotHeight - Math.Clamp(percent / 100d, 0, 1) * plotHeight;
    private static SolidColorBrush Brush(byte r, byte g, byte b, byte a) => new(Microsoft.UI.ColorHelper.FromArgb(a, r, g, b));
}
