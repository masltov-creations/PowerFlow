using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Shapes;
using PowerFlow.App.Controller;
using PowerFlow.Core.Policy;
using PowerFlow.Core.Rules;
using Windows.UI.ViewManagement;

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
    private PowerFlowConfig _config = PowerFlowConfig.Default;
    private DisclosureState _disclosure = DisclosureState.None;
    private double _windowSeconds = 60;

    public TrajectoryControl()
    {
        InitializeComponent();
        Graph.ThresholdsPreviewed += (_, e) => ThresholdsPreviewed?.Invoke(this, e);
        Graph.ThresholdsCommitted += (_, e) => ThresholdsCommitted?.Invoke(this, e);
        Graph.HoverChanged += OnGraphHoverChanged;
    }

    public void SetLayoutProfile(DashboardLayoutProfile profile)
    {
        Graph.MinHeight = profile.GraphMinHeight;
        Root.RowSpacing = profile.TrajectoryRowSpacing;
        HoverLens.MaxWidth = profile.HoverLensMaxWidth;
        var compact = profile.Mode == DashboardPresentationMode.Compressed;
        var full = profile.Mode == DashboardPresentationMode.FullScreen;
        DisclosurePanel.Padding = compact ? new Thickness(9, 6, 9, 6) : full ? new Thickness(15, 10, 15, 10) : new Thickness(11, 8, 11, 8);
        var nodePadding = compact ? new Thickness(9, 4, 9, 4) : full ? new Thickness(15, 7, 15, 7) : new Thickness(12, 5, 12, 5);
        var nodeFont = full ? 13d : 12d;
        foreach (var node in new[] { AutoNode, SaverNode, BalancedNode, PerformanceNode }) { node.Padding = nodePadding; node.FontSize = nodeFont; }
        NowText.FontSize = full ? 14 : 12;
        NextActionText.FontSize = full ? 13 : 12;
        DirectionText.FontSize = full ? 12 : 11;
    }
    public void SetShellPresentation(TrajectoryPresentation presentation, double targetHeight)
    {
        DashboardLayoutProfile profile;
        if (presentation == TrajectoryPresentation.Minimal)
        {
            profile = new DashboardLayoutProfile(
                DashboardPresentationMode.Compressed, true, false, false, false,
                16, 13, 6, 4, 6, 160, Math.Max(48, targetHeight), 3, 180, 8);
            HoverLens.Visibility = Visibility.Collapsed;
            DisclosurePanel.Visibility = Visibility.Collapsed;
        }
        else if (presentation == TrajectoryPresentation.Compact)
        {
            profile = DashboardResponsiveLayout.Resolve(760, 440, false);
            HoverLens.Visibility = Visibility.Collapsed;
        }
        else
        {
            profile = DashboardResponsiveLayout.Resolve(1280, 800, false);
        }

        SetLayoutProfile(profile);
    }
    public event EventHandler? AutoRequested;
    public event EventHandler<TrajectoryManualStateEventArgs>? ManualStateRequested;
    public event EventHandler<TrajectoryRangeChangedEventArgs>? RangeChanged;
    public event EventHandler<ThresholdsChangedEventArgs>? ThresholdsPreviewed;
    public event EventHandler<ThresholdsChangedEventArgs>? ThresholdsCommitted;

    public void Apply(TrajectoryModel model, IReadOnlyList<DashboardSample> samples, PowerFlowConfig config, IReadOnlyList<TransitionRecord> history, double windowSeconds)
    {
        _model = model;
        _config = config;
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
        QuietDisclosureButton.Content = $"QUIET {model.QuietRail.ThresholdPercent:0.#}%";
        PromoteDisclosureButton.Content = $"PROMOTE {model.PromoteRail.ThresholdPercent:0.#}%";
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
        RefreshDisclosure();
    }

    private void OnGraphHoverChanged(object? sender, GraphHoverChangedEventArgs e)
    {
        if (e.Telemetry is null) HideLens();
        else ShowLens(TrajectoryLensProjection.ForSample(e.Telemetry));
    }

    private void OnAutoNode(object sender, RoutedEventArgs e)
    {
        AutoRequested?.Invoke(this, EventArgs.Empty);
        if (_model is not null) ToggleDisclosure(DisclosureKind.Mode, "auto", TrajectoryLensProjection.ForMode(_model.Now.State, true, false));
    }

    private void OnSaverNode(object sender, RoutedEventArgs e) => SelectManualMode(PowerState.PowerSaver);
    private void OnBalancedNode(object sender, RoutedEventArgs e) => SelectManualMode(PowerState.Balanced);
    private void OnPerformanceNode(object sender, RoutedEventArgs e) => SelectManualMode(PowerState.HighPerformance);

    private void SelectManualMode(PowerState state)
    {
        ManualStateRequested?.Invoke(this, new TrajectoryManualStateEventArgs(state));
        if (_model is not null) ToggleDisclosure(DisclosureKind.Mode, $"mode:{state}", TrajectoryLensProjection.ForMode(state, _model.Now.State == state, true));
    }

    private void OnAutoNodeEntered(object sender, PointerRoutedEventArgs e)
    {
        if (_model is not null) ShowLens(TrajectoryLensProjection.ForMode(_model.Now.State, true, false));
    }
    private void OnSaverNodeEntered(object sender, PointerRoutedEventArgs e) => ShowModeLens(PowerState.PowerSaver);
    private void OnBalancedNodeEntered(object sender, PointerRoutedEventArgs e) => ShowModeLens(PowerState.Balanced);
    private void OnPerformanceNodeEntered(object sender, PointerRoutedEventArgs e) => ShowModeLens(PowerState.HighPerformance);
    private void ShowModeLens(PowerState state)
    {
        if (_model is null) return;
        ShowLens(TrajectoryLensProjection.ForMode(state, _model.Now.State == state, _model.Now.IsLatched && string.Equals(_model.Now.LatchType, "Manual", StringComparison.OrdinalIgnoreCase)));
    }

    private void OnNowEntered(object sender, PointerRoutedEventArgs e)
    {
        if (_model is not null) ShowLens(TrajectoryLensProjection.ForNow(_model));
    }
    private void OnQuietEntered(object sender, PointerRoutedEventArgs e)
    {
        if (_model is not null) ShowLens(TrajectoryLensProjection.ForRail(_model.QuietRail, false));
    }
    private void OnPromoteEntered(object sender, PointerRoutedEventArgs e)
    {
        if (_model is not null) ShowLens(TrajectoryLensProjection.ForRail(_model.PromoteRail, true));
    }
    private void OnLensTargetExited(object sender, PointerRoutedEventArgs e) => HideLens();

    private void OnNowClicked(object sender, RoutedEventArgs e)
    {
        if (_model is not null) ToggleDisclosure(DisclosureKind.Now, "now", TrajectoryLensProjection.ForNow(_model));
    }
    private void OnQuietDisclosureClicked(object sender, RoutedEventArgs e)
    {
        if (_model is not null) ToggleDisclosure(DisclosureKind.QuietRail, "quiet", TrajectoryLensProjection.ForRail(_model.QuietRail, false));
    }
    private void OnPromoteDisclosureClicked(object sender, RoutedEventArgs e)
    {
        if (_model is not null) ToggleDisclosure(DisclosureKind.PromoteRail, "promote", TrajectoryLensProjection.ForRail(_model.PromoteRail, true));
    }
    private void OnDisclosureClose(object sender, RoutedEventArgs e)
    {
        _disclosure = DisclosureState.None;
        DisclosurePanel.Visibility = Visibility.Collapsed;
    }

    private void OnTransitionMarkerClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: TrajectoryTransitionMarker marker }) return;
        var lens = TrajectoryLensProjection.ForTransition(marker);
        ToggleDisclosure(DisclosureKind.Transition, lens.Key, lens);
    }

    private void OnTransitionMarkerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Button { Tag: TrajectoryTransitionMarker marker }) ShowLens(TrajectoryLensProjection.ForTransition(marker));
    }

    private void ToggleDisclosure(DisclosureKind kind, string? key, TrajectoryLensModel lens)
    {
        _disclosure = _disclosure.Toggle(kind, key);
        if (_disclosure.Kind == DisclosureKind.None)
        {
            DisclosurePanel.Visibility = Visibility.Collapsed;
            return;
        }
        PopulateDisclosure(lens);
    }

    private void RefreshDisclosure()
    {
        if (_model is null || _disclosure.Kind == DisclosureKind.None) return;
        TrajectoryLensModel? lens = _disclosure.Kind switch
        {
            DisclosureKind.Now => TrajectoryLensProjection.ForNow(_model),
            DisclosureKind.QuietRail => TrajectoryLensProjection.ForRail(_model.QuietRail, false),
            DisclosureKind.PromoteRail => TrajectoryLensProjection.ForRail(_model.PromoteRail, true),
            DisclosureKind.Mode => TrajectoryLensProjection.ForMode(_model.Now.State, true, _model.Now.IsLatched && string.Equals(_model.Now.LatchType, "Manual", StringComparison.OrdinalIgnoreCase)),
            DisclosureKind.Transition => _model.Transitions.Select(TrajectoryLensProjection.ForTransition).FirstOrDefault(x => x.Key == _disclosure.Key),
            _ => null
        };
        if (lens is null)
        {
            _disclosure = DisclosureState.None;
            DisclosurePanel.Visibility = Visibility.Collapsed;
            return;
        }
        PopulateDisclosure(lens);
    }

    private void PopulateDisclosure(TrajectoryLensModel lens)
    {
        DisclosureTitle.Text = lens.Title;
        DisclosurePrimary.Text = lens.Primary;
        DisclosureSecondary.Text = lens.Secondary;
        DisclosureDetail.Text = lens.Detail;
        Reveal(DisclosurePanel);
    }

    private void ShowLens(TrajectoryLensModel lens)
    {
        LensTitle.Text = lens.Title;
        LensPrimary.Text = lens.Primary;
        LensSecondary.Text = lens.Secondary;
        LensDetail.Text = lens.Detail;
        Reveal(HoverLens);
    }

    private void HideLens() => HoverLens.Visibility = Visibility.Collapsed;

    private void Reveal(FrameworkElement element)
    {
        element.Visibility = Visibility.Visible;
        element.Opacity = 1;
        if (!ShouldAnimate()) return;
        var animation = new DoubleAnimation
        {
            From = 0.35,
            To = 1,
            Duration = new Duration(TimeSpan.FromMilliseconds(130)),
            EnableDependentAnimation = true
        };
        Storyboard.SetTarget(animation, element);
        Storyboard.SetTargetProperty(animation, "Opacity");
        var storyboard = new Storyboard();
        storyboard.Children.Add(animation);
        storyboard.Begin();
    }

    private bool ShouldAnimate()
    {
        if (_config.ReducedMotionOverride is true) return false;
        if (_config.ReducedMotionOverride is false) return true;
        try { return new UISettings().AnimationsEnabled; }
        catch { return false; }
    }

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
        var brush = StateBandBrushSource.Background;
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
                IsHitTestVisible = false,
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

        foreach (var marker in _model.Transitions)
        {
            if (marker.At < start || marker.At > latest) continue;
            var normalized = Math.Clamp((marker.At - start).TotalSeconds / _windowSeconds, 0, 1);
            var button = new Button
            {
                Width = 12,
                Height = Math.Max(18, StateBandCanvas.ActualHeight),
                Padding = new Thickness(0),
                Content = "•",
                FontSize = 11,
                Opacity = marker.Success ? 0.82 : 1,
                Tag = marker,
                Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                BorderThickness = new Thickness(0)
            };
            button.Click += OnTransitionMarkerClicked;
            button.PointerEntered += OnTransitionMarkerEntered;
            button.PointerExited += OnLensTargetExited;
            Canvas.SetLeft(button, normalized * Math.Max(1, StateBandCanvas.ActualWidth - 12));
            Canvas.SetTop(button, 0);
            StateBandCanvas.Children.Add(button);
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
