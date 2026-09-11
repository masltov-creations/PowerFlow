using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using PowerFlow.App.Controller;
using Windows.Foundation;

namespace PowerFlow.App.Dashboard;

public sealed partial class EfficiencyCompareControl : UserControl
{
    private EfficiencyExperimentRuntime? _runtime;
    private EfficiencyExperimentSnapshot _snapshot = new(
        EfficiencyExperimentPhase.None,
        EfficiencyExperimentRuntime.DefaultDuration,
        null,
        false,
        false,
        EfficiencyPhaseSummary.Empty,
        EfficiencyPhaseSummary.Empty,
        new(null, null, null, null, null, null, null));

    public EfficiencyCompareControl()
    {
        InitializeComponent();
    }

    public void Initialize(EfficiencyExperimentRuntime runtime)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        Refresh();
    }

    public void Refresh()
    {
        if (_runtime is not null) _snapshot = _runtime.Snapshot;
        UpdateText();
        RedrawCharts();
    }

    private TimeSpan SelectedDuration => DurationSelector.SelectedIndex switch
    {
        1 => TimeSpan.FromMinutes(30),
        2 => TimeSpan.FromHours(5),
        _ => TimeSpan.FromMinutes(5)
    };

    private void OnStartBaselineClicked(object sender, RoutedEventArgs e)
    {
        if (_runtime is null) return;
        _snapshot = _runtime.StartBaseline(SelectedDuration);
        Refresh();
    }

    private void OnStartAfterClicked(object sender, RoutedEventArgs e)
    {
        if (_runtime is null) return;
        _snapshot = _runtime.StartAfter(SelectedDuration);
        Refresh();
    }

    private void OnStopClicked(object sender, RoutedEventArgs e)
    {
        if (_runtime is null) return;
        _snapshot = _runtime.Stop();
        Refresh();
    }

    private void OnResetClicked(object sender, RoutedEventArgs e)
    {
        if (_runtime is null) return;
        _snapshot = _runtime.Reset();
        Refresh();
    }

    private void OnChartSizeChanged(object sender, SizeChangedEventArgs e) => RedrawCharts();

    private void UpdateText()
    {
        var phase = _snapshot.ActivePhase;
        PhaseStatusText.Text = phase == EfficiencyExperimentPhase.None
            ? (_snapshot.BaselineComplete && _snapshot.AfterComplete ? "COMPLETE" : "READY")
            : $"RECORDING {phase.ToString().ToUpperInvariant()}";

        UpdatePhaseText(_snapshot.Baseline, BaselineCoverageText, BaselineWorkText, BaselineEnergyText, BaselineEfficiencyText, BaselinePressureText);
        UpdatePhaseText(_snapshot.After, AfterCoverageText, AfterWorkText, AfterEnergyText, AfterEfficiencyText, AfterPressureText);

        var comparison = _snapshot.Comparison;
        if (comparison.AvoidedEnergyWh is not double avoided || comparison.AvoidedEnergyPercent is not double avoidedPercent)
        {
            ResultText.Text = "Complete Baseline and After to calculate equivalent-work savings.";
            return;
        }

        var energyText = avoided >= 0
            ? $"{avoided:0.###} Wh avoided for equivalent work ({avoidedPercent:+0.0;-0.0;0}%)"
            : $"{-avoided:0.###} Wh more used for equivalent work ({avoidedPercent:+0.0;-0.0;0}%)";
        var efficiency = comparison.WorkEfficiencyImprovementPercent is double efficiencyPercent ? $"efficiency {efficiencyPercent:+0.0;-0.0;0}%" : "efficiency —";
        var rate = comparison.WorkRateChangePercent is double ratePercent ? $"work rate {ratePercent:+0.0;-0.0;0}%" : "work rate —";
        var pressure = comparison.PressureChangePoints is double pressurePoints ? $"pressure {pressurePoints:+0.0;-0.0;0} pt" : "pressure —";
        var queue = comparison.QueueChange is double queueChange ? $"queue {queueChange:+0.00;-0.00;0}" : "queue —";
        ResultText.Text = $"{energyText} • {efficiency} • {rate} • {pressure} • {queue}";
    }

    private static void UpdatePhaseText(
        EfficiencyPhaseSummary phase,
        TextBlock coverage,
        TextBlock work,
        TextBlock energy,
        TextBlock efficiency,
        TextBlock pressure)
    {
        coverage.Text = $"{phase.CoveragePercent:0}% COVERAGE";
        work.Text = phase.SampleCount > 1 ? $"CPU work {phase.CpuWorkMinutes:0.###} full-CPU min" : "CPU work —";
        energy.Text = phase.SampleCount > 1 ? $"Energy {phase.EnergyWh:0.###} Wh • avg {phase.AveragePackageWatts:0.0} W" : "Energy —";
        efficiency.Text = phase.SampleCount > 1 ? $"Efficiency {phase.WorkPerWh:0.###} work-min/Wh" : "Efficiency —";
        pressure.Text = phase.SampleCount > 1 ? $"Pressure {phase.AveragePressurePercent:0.0}% avg / {phase.PeakPressurePercent:0.0}% peak • queue {phase.AverageQueueLength:0.00}" : "Pressure / queue —";
    }

    private void RedrawCharts()
    {
        DrawChart(WorkChart, _snapshot.Baseline.Trace, _snapshot.After.Trace, point => point.CumulativeCpuWorkMinutes);
        DrawChart(EnergyChart, _snapshot.Baseline.Trace, _snapshot.After.Trace, point => point.CumulativeEnergyWh);
    }

    private void DrawChart(Canvas canvas, IReadOnlyList<EfficiencyTracePoint> baseline, IReadOnlyList<EfficiencyTracePoint> after, Func<EfficiencyTracePoint, double> value)
    {
        canvas.Children.Clear();
        var width = canvas.ActualWidth;
        var height = canvas.ActualHeight;
        if (width < 40 || height < 40) return;

        var gridBrush = ResourceBrush("PowerFlowCardBorderBrush", Colors.Gray);
        var baselineBrush = ResourceBrush("PowerFlowTextSecondaryBrush", Colors.Gray);
        var afterBrush = ResourceBrush("PowerFlowAccentBrush", Colors.DodgerBlue);
        var durationSeconds = Math.Max(1d, _snapshot.TargetDuration.TotalSeconds);
        var maxValue = Math.Max(
            baseline.Count > 0 ? baseline.Max(value) : 0d,
            after.Count > 0 ? after.Max(value) : 0d);
        maxValue = Math.Max(maxValue, 0.0001d);

        for (var i = 1; i <= 3; i++)
        {
            var y = height * i / 4d;
            canvas.Children.Add(new Line { X1 = 0, X2 = width, Y1 = y, Y2 = y, Stroke = gridBrush, StrokeThickness = 1, Opacity = .35 });
        }

        AddSeries(canvas, baseline, value, durationSeconds, maxValue, width, height, baselineBrush, 1.8, .75);
        AddSeries(canvas, after, value, durationSeconds, maxValue, width, height, afterBrush, 2.2, .95);

        var legend = new TextBlock { Text = "BASELINE   AFTER", FontSize = 11, Opacity = .58, Foreground = baselineBrush };
        Canvas.SetLeft(legend, 4);
        Canvas.SetTop(legend, 2);
        canvas.Children.Add(legend);
    }

    private static void AddSeries(
        Canvas canvas,
        IReadOnlyList<EfficiencyTracePoint> points,
        Func<EfficiencyTracePoint, double> value,
        double durationSeconds,
        double maxValue,
        double width,
        double height,
        Brush stroke,
        double thickness,
        double opacity)
    {
        if (points.Count < 2) return;
        var polyline = new Polyline
        {
            Stroke = stroke,
            StrokeThickness = thickness,
            StrokeLineJoin = PenLineJoin.Round,
            Opacity = opacity
        };
        foreach (var point in points)
        {
            var x = Math.Clamp(point.ElapsedSeconds / durationSeconds, 0d, 1d) * width;
            var y = (1d - Math.Clamp(value(point) / maxValue, 0d, 1d)) * (height - 8d) + 4d;
            polyline.Points.Add(new Point(x, y));
        }
        canvas.Children.Add(polyline);
    }

    private static Brush ResourceBrush(string key, global::Windows.UI.Color fallback)
    {
        if (Application.Current.Resources.TryGetValue(key, out var resource) && resource is Brush brush) return brush;
        return new SolidColorBrush(fallback);
    }
}