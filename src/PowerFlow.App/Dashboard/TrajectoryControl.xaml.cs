using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using PowerFlow.App.Controller;
using PowerFlow.Core.Policy;
using PowerFlow.Core.Rules;

namespace PowerFlow.App.Dashboard;

public sealed class TrajectoryManualStateEventArgs(PowerState state) : EventArgs
{
    public PowerState State { get; } = state;
}

public sealed class TrajectoryRangeChangedEventArgs(double seconds) : EventArgs
{
    public double Seconds { get; } = seconds;
}

public sealed partial class TrajectoryControl : UserControl
{
    private TrajectoryModel? _model;
    private double _windowSeconds = 60;

    public TrajectoryControl()
    {
        InitializeComponent();
        Graph.ThresholdsPreviewed += (_, e) => ThresholdsPreviewed?.Invoke(this, e);
        Graph.ThresholdsCommitted += (_, e) => ThresholdsCommitted?.Invoke(this, e);
    }

    public event EventHandler? AutoRequested;
    public event EventHandler<TrajectoryManualStateEventArgs>? ManualStateRequested;
    public event EventHandler<TrajectoryRangeChangedEventArgs>? RangeChanged;
    public event EventHandler<ThresholdsChangedEventArgs>? ThresholdsPreviewed;
    public event EventHandler<ThresholdsChangedEventArgs>? ThresholdsCommitted;

    public void Apply(TrajectoryModel model, IReadOnlyList<DashboardSample> samples, PowerFlowConfig config, IReadOnlyList<TransitionRecord> history, double windowSeconds)
    {
        _model = model;
        _windowSeconds = Math.Max(30, windowSeconds);
        Graph.Apply(samples, config, history, _windowSeconds);

        var manual = model.Now.IsLatched && string.Equals(model.Now.LatchType, "Manual", StringComparison.OrdinalIgnoreCase);
        var game = model.Now.IsLatched && string.Equals(model.Now.LatchType, "Game", StringComparison.OrdinalIgnoreCase);
        AutoNode.IsChecked = !manual;
        SaverNode.IsChecked = model.Now.State == PowerState.PowerSaver;
        BalancedNode.IsChecked = model.Now.State == PowerState.Balanced;
        PerformanceNode.IsChecked = model.Now.State == PowerState.HighPerformance;
        SaverNode.IsEnabled = !game;
        BalancedNode.IsEnabled = !game;

        NowText.Text = $"NOW · {StateName(model.Now.State)} · {model.Now.CpuPercent:0.0}%";
        NextActionText.Text = model.NextAction;
        DirectionText.Text = model.Direction switch
        {
            TrajectoryDirection.TowardBalanced => "pressure → BALANCED",
            TrajectoryDirection.TowardSaver => "quiet → SAVER",
            TrajectoryDirection.HoldPerformance => "GAME HOLD",
            TrajectoryDirection.HoldManual => "MANUAL HOLD",
            _ => "steady"
        };
        StateBandLabel.Text = model.Gaps.Count > 0 ? $"{model.StateSegments.Count} observed intervals · {model.Gaps.Count} gap" : $"{model.StateSegments.Count} observed interval{(model.StateSegments.Count == 1 ? "" : "s")}";
        DrawStateBands();
    }

    private void OnAutoNode(object sender, RoutedEventArgs e) => AutoRequested?.Invoke(this, EventArgs.Empty);
    private void OnSaverNode(object sender, RoutedEventArgs e) => ManualStateRequested?.Invoke(this, new TrajectoryManualStateEventArgs(PowerState.PowerSaver));
    private void OnBalancedNode(object sender, RoutedEventArgs e) => ManualStateRequested?.Invoke(this, new TrajectoryManualStateEventArgs(PowerState.Balanced));
    private void OnPerformanceNode(object sender, RoutedEventArgs e) => ManualStateRequested?.Invoke(this, new TrajectoryManualStateEventArgs(PowerState.HighPerformance));

    private void OnRange60(object sender, RoutedEventArgs e)
    {
        Range60Button.IsChecked = true;
        Range120Button.IsChecked = false;
        RangeChanged?.Invoke(this, new TrajectoryRangeChangedEventArgs(60));
    }

    private void OnRange120(object sender, RoutedEventArgs e)
    {
        Range60Button.IsChecked = false;
        Range120Button.IsChecked = true;
        RangeChanged?.Invoke(this, new TrajectoryRangeChangedEventArgs(120));
    }

    private void OnStateBandSizeChanged(object sender, SizeChangedEventArgs e) => DrawStateBands();

    private void DrawStateBands()
    {
        StateBandCanvas.Children.Clear();
        if (_model is null || _model.Samples.Count == 0 || StateBandCanvas.ActualWidth <= 0) return;
        var latest = _model.Samples[^1].At;
        var start = latest.AddSeconds(-_windowSeconds);
        var brush = Root.Resources["PowerFlowTrackBrush"] as Brush ?? Application.Current.Resources["PowerFlowTrackBrush"] as Brush;
        foreach (var segment in _model.StateSegments)
        {
            if (segment.To < start || segment.From > latest) continue;
            var from = Math.Max(0, (segment.From - start).TotalSeconds / _windowSeconds);
            var to = Math.Min(1, (segment.To - start).TotalSeconds / _windowSeconds);
            var left = from * StateBandCanvas.ActualWidth;
            var width = Math.Max(3, (to - from) * StateBandCanvas.ActualWidth);
            var rect = new Rectangle
            {
                Width = width,
                Height = Math.Max(4, StateBandCanvas.ActualHeight),
                Fill = brush,
                Opacity = segment.State switch
                {
                    PowerState.PowerSaver => 0.38,
                    PowerState.Balanced => 0.62,
                    PowerState.HighPerformance => 0.88,
                    _ => 0.45
                }
            };
            Canvas.SetLeft(rect, left);
            StateBandCanvas.Children.Add(rect);
        }
    }

    private static string StateName(PowerState state) => state switch
    {
        PowerState.PowerSaver => "SAVER",
        PowerState.Balanced => "BALANCED",
        PowerState.HighPerformance => "PERFORMANCE",
        _ => state.ToString().ToUpperInvariant()
    };
}
