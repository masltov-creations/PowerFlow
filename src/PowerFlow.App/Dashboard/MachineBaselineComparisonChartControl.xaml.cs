using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using PowerFlow.Core.Profiling;
using Windows.Foundation;

namespace PowerFlow.App.Dashboard;

public enum MachineBaselineComparisonMetric
{
    Throughput,
    Efficiency,
    PackagePower
}

public sealed partial class MachineBaselineComparisonChartControl : UserControl
{
    private MachineBaselineComparisonRun? _run;
    private MachineBaselineComparisonMetric _metric = MachineBaselineComparisonMetric.Throughput;

    public MachineBaselineComparisonChartControl() => InitializeComponent();

    public void SetRun(MachineBaselineComparisonRun? run, MachineBaselineComparisonMetric metric)
    {
        _run = run;
        _metric = metric;
        Redraw();
    }

    private void OnChartSizeChanged(object sender, SizeChangedEventArgs e) => Redraw();

    private void Redraw()
    {
        ChartCanvas.Children.Clear();
        LegendPanel.Children.Clear();
        if (_run is null || _run.Results.Count == 0 || ChartCanvas.ActualWidth < 120 || ChartCanvas.ActualHeight < 100)
        {
            EmptyMessage.Visibility = Visibility.Visible;
            return;
        }
        EmptyMessage.Visibility = Visibility.Collapsed;

        var width = ChartCanvas.ActualWidth;
        var height = ChartCanvas.ActualHeight;
        const double left = 48;
        const double right = 12;
        const double top = 12;
        const double bottom = 30;
        var plotWidth = Math.Max(1, width - left - right);
        var plotHeight = Math.Max(1, height - top - bottom);
        var workers = new[] { 1, 2, 4, 8, 16 };
        var allValues = _run.Results.SelectMany(result => result.Benchmark.Points.Select(Value)).Where(double.IsFinite).Where(value => value >= 0).ToArray();
        var max = allValues.Length == 0 ? 1d : allValues.Max();
        max = Math.Max(1d, max * 1.08d);

        var grid = ResourceBrush("PowerFlowCardBorderBrush", Colors.Gray);
        var secondary = ResourceBrush("PowerFlowTextSecondaryBrush", Colors.Gray);
        for (var tick = 0; tick <= 4; tick++)
        {
            var y = top + plotHeight * tick / 4d;
            ChartCanvas.Children.Add(new Line { X1 = left, X2 = left + plotWidth, Y1 = y, Y2 = y, Stroke = grid, StrokeThickness = 1, Opacity = .45 });
            var value = max * (1d - tick / 4d);
            var label = new TextBlock { Text = AxisValue(value), FontSize = 11, Foreground = secondary, Opacity = .7 };
            Canvas.SetLeft(label, 2);
            Canvas.SetTop(label, Math.Max(0, y - 9));
            ChartCanvas.Children.Add(label);
        }

        for (var i = 0; i < workers.Length; i++)
        {
            var x = left + plotWidth * i / (workers.Length - 1d);
            var label = new TextBlock { Text = $"{workers[i]}T", FontSize = 11, Foreground = secondary, Opacity = .72 };
            Canvas.SetLeft(label, x - 10);
            Canvas.SetTop(label, top + plotHeight + 6);
            ChartCanvas.Children.Add(label);
        }

        foreach (var result in _run.Results)
        {
            var brush = BrushFor(result.Mode);
            var polyline = new Polyline
            {
                Stroke = brush,
                StrokeThickness = result.Mode is MachineBaselineMode.WindowsSaver or MachineBaselineMode.WindowsBalanced ? 1.8 : 2.5,
                StrokeLineJoin = PenLineJoin.Round,
                Opacity = result.Mode is MachineBaselineMode.WindowsSaver or MachineBaselineMode.WindowsBalanced ? .72 : .96
            };
            if (result.Mode is MachineBaselineMode.WindowsSaver or MachineBaselineMode.WindowsBalanced)
                polyline.StrokeDashArray = new DoubleCollection { 4, 3 };

            foreach (var worker in workers)
            {
                var point = result.Benchmark.Points.FirstOrDefault(candidate => candidate.WorkerCount == worker);
                if (point is null) continue;
                var value = Value(point);
                if (!double.IsFinite(value) || value < 0) continue;
                var x = left + plotWidth * Array.IndexOf(workers, worker) / (workers.Length - 1d);
                var y = top + plotHeight * (1d - Math.Clamp(value / max, 0d, 1d));
                polyline.Points.Add(new Point(x, y));
                var dot = new Ellipse { Width = 7, Height = 7, Fill = brush, Stroke = ResourceBrush("PowerFlowCanvasBrush", Colors.Black), StrokeThickness = 1 };
                Canvas.SetLeft(dot, x - 3.5);
                Canvas.SetTop(dot, y - 3.5);
                ChartCanvas.Children.Add(dot);
            }
            ChartCanvas.Children.Insert(Math.Max(0, ChartCanvas.Children.Count - polyline.Points.Count), polyline);
            AddLegend(result.Label, brush, result.Mode is MachineBaselineMode.WindowsSaver or MachineBaselineMode.WindowsBalanced);
        }
    }

    private double Value(CpuCapabilityPoint point) => _metric switch
    {
        MachineBaselineComparisonMetric.Throughput => point.ThroughputMops,
        MachineBaselineComparisonMetric.Efficiency => point.ThroughputPerWatt ?? double.NaN,
        MachineBaselineComparisonMetric.PackagePower => point.PackageWatts ?? double.NaN,
        _ => point.ThroughputMops
    };

    private string AxisValue(double value) => _metric switch
    {
        MachineBaselineComparisonMetric.Throughput => $"{value:0} M/s",
        MachineBaselineComparisonMetric.Efficiency => $"{value:0.00} M/W",
        MachineBaselineComparisonMetric.PackagePower => $"{value:0} W",
        _ => value.ToString("0")
    };

    private void AddLegend(string label, Brush brush, bool native)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, VerticalAlignment = VerticalAlignment.Center };
        var line = new Line { X1 = 0, X2 = 16, Y1 = 5, Y2 = 5, Stroke = brush, StrokeThickness = native ? 1.8 : 2.5 };
        if (native) line.StrokeDashArray = new DoubleCollection { 4, 3 };
        row.Children.Add(new Grid { Width = 16, Height = 10, Children = { line } });
        row.Children.Add(new TextBlock { Text = label, FontSize = 11, Opacity = .76 });
        LegendPanel.Children.Add(row);
    }

    private Brush BrushFor(MachineBaselineMode mode) => mode switch
    {
        MachineBaselineMode.WindowsSaver => ResourceBrush("PowerFlowSaverAccentBrush", Colors.Green),
        MachineBaselineMode.WindowsBalanced => ResourceBrush("PowerFlowBalancedAccentBrush", Colors.DeepSkyBlue),
        MachineBaselineMode.PowerFlowSaver => ResourceBrush("PowerFlowSaverAccentBrush", Colors.LimeGreen),
        MachineBaselineMode.BalancedEfficient => ResourceBrush("PowerFlowHealthyBrush", Colors.LimeGreen),
        MachineBaselineMode.BalancedPerformance => ResourceBrush("PowerFlowAccentBrush", Colors.Cyan),
        MachineBaselineMode.Performance => ResourceBrush("PowerFlowPerformanceAccentBrush", Colors.Orange),
        MachineBaselineMode.Ultra => ResourceBrush("PowerFlowPerformanceAccentBrush", Colors.OrangeRed),
        MachineBaselineMode.Auto => ResourceBrush("PowerFlowAutoAccentBrush", Colors.MediumPurple),
        _ => ResourceBrush("PowerFlowAccentBrush", Colors.Cyan)
    };

    private static Brush ResourceBrush(string key, global::Windows.UI.Color fallback)
    {
        if (Application.Current.Resources.TryGetValue(key, out var resource) && resource is Brush brush) return brush;
        return new SolidColorBrush(fallback);
    }
}