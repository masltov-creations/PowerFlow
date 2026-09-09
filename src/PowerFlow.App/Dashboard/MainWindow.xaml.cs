using System.Numerics;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using PowerFlow.App.Controller;
using PowerFlow.App.Telemetry;
using PowerFlow.Core.Policy;
using PowerFlow.Core.Rules;
using PowerFlow.Windows.Activity;
using Windows.Graphics;
using Windows.Storage.Pickers;
using Windows.UI.ViewManagement;

namespace PowerFlow.App.Dashboard;

public sealed partial class MainWindow : Window
{
    private const int CompressedWidth = 760;
    private const int CompressedHeight = 440;
    private const int ExpandedWidth = 1120;
    private const int ExpandedHeight = 720;
    private readonly PowerFlowController _controller;
    private readonly Func<PowerFlowConfig, Task> _applyConfig;
    private readonly TelemetryContinuityRecorder _recorder;
    private TelemetryVisibilityLease? _visibilityLease;
    private readonly DispatcherQueue _dispatcher;
    private readonly bool _previewMode;
    private bool _explicitShutdown;
    private PowerFlowConfig _config;
    private bool _closed;
    private double _graphWindowSeconds = 60;
    private DashboardPresentationMode _presentationMode = DashboardPresentationMode.Compressed;
    private DispatcherQueueTimer? _presentationTimer;
    private bool _suppressResizeModeSync;

    public DashboardViewModel ViewModel { get; } = new();

    public MainWindow(PowerFlowController controller, TelemetryContinuityRecorder recorder, PowerFlowConfig config, Func<PowerFlowConfig, Task> applyConfig, bool previewMode = false)
    {
        InitializeComponent();
        Title = previewMode ? "PowerFlow - Preview" : "PowerFlow";
        PreviewModeBadge.Visibility = previewMode ? Visibility.Visible : Visibility.Collapsed;
        _controller = controller;
        _recorder = recorder;
        _previewMode = previewMode;
        _config = config;
        _applyConfig = applyConfig;
        _dispatcher = DispatcherQueue.GetForCurrentThread();
        ApplyTheme(config.Theme);
        ViewModel.Configure(config);
        Root.DataContext = ViewModel;
        ViewModel.UpdateContinuity(controller.Snapshot, recorder.History, recorder.LatestRichTelemetry);
        ApplyVisualState(controller.Snapshot);
        RulesPanel.Initialize(config, ApplyConfigFromPageAsync, BrowseExecutableAsync);
        SettingsPanel.Initialize(config, ApplyConfigFromPageAsync, () => _controller.ListPowerPlansAsync(), ApplyTheme);
        controller.SnapshotChanged += OnSnapshotChanged;
        _recorder.ContinuityChanged += OnContinuityChanged;
        Trajectory.AutoRequested += OnTrajectoryAutoRequested;
        Trajectory.ManualStateRequested += OnTrajectoryManualStateRequested;
        Trajectory.RangeChanged += OnTrajectoryRangeChanged;
        Trajectory.ThresholdsPreviewed += OnThresholdsPreviewed;
        Trajectory.ThresholdsCommitted += OnThresholdsCommitted;
        AppWindow.Closing += OnAppWindowClosing;
        AppWindow.Changed += OnAppWindowChanged;
        Closed += OnClosed;
        AppWindow.Resize(new SizeInt32(CompressedWidth, CompressedHeight));
        ApplyPresentationMode(DashboardPresentationMode.Compressed, animate: false, resizeWindow: false);
        try { SystemBackdrop = new MicaBackdrop(); } catch { }
        SelectSection("flow");
    }

    public Task ShowAsync(bool showSettings = false, bool fullScreen = false)
    {
        _visibilityLease ??= _recorder.AcquireVisibility();
        ViewModel.UpdateContinuity(_controller.Snapshot, _recorder.History, _recorder.LatestRichTelemetry);
        ApplyVisualState(_controller.Snapshot);
        ApplyPresentationMode(fullScreen ? DashboardPresentationMode.FullScreen : showSettings ? DashboardPresentationMode.Expanded : DashboardPresentationMode.Compressed, animate: false);
        SelectSection(showSettings ? "settings" : "flow");
        Activate();
        return Task.CompletedTask;
    }

    public void CloseForShutdown()
    {
        _explicitShutdown = true;
        Close();
    }


    private void OnPresentationToggleClicked(object sender, RoutedEventArgs e)
    {
        if (IsFullScreenPresenter())
        {
            ApplyPresentationMode(DashboardPresentationMode.Expanded, animate: true);
            return;
        }

        var next = _presentationMode == DashboardPresentationMode.Compressed
            ? DashboardPresentationMode.Expanded
            : DashboardPresentationMode.FullScreen;
        ApplyPresentationMode(next, animate: true);
    }

    private void OnCompactClicked(object sender, RoutedEventArgs e) => ApplyPresentationMode(DashboardPresentationMode.Compressed, animate: true);

    private void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if ((!args.DidSizeChange && !args.DidPresenterChange) || _suppressResizeModeSync) return;
        ApplyResponsiveLayout(animate: false);
    }

    private void ApplyPresentationMode(DashboardPresentationMode mode, bool animate, bool resizeWindow = true)
    {
        _presentationMode = mode;
        if (mode == DashboardPresentationMode.FullScreen && resizeWindow)
        {
            _presentationTimer?.Stop();
            _presentationTimer = null;
            _suppressResizeModeSync = true;
            AppWindow.SetPresenter(AppWindowPresenterKind.FullScreen);
            _suppressResizeModeSync = false;
            ApplyResponsiveLayout(animate);
            return;
        }

        if (mode != DashboardPresentationMode.FullScreen && IsFullScreenPresenter())
        {
            _suppressResizeModeSync = true;
            AppWindow.SetPresenter(AppWindowPresenterKind.Overlapped);
            _suppressResizeModeSync = false;
        }

        if (resizeWindow)
        {
            var target = mode == DashboardPresentationMode.Expanded
                ? new SizeInt32(ExpandedWidth, ExpandedHeight)
                : new SizeInt32(CompressedWidth, CompressedHeight);
            AnimateWindowTo(target, animate);
        }
        else
        {
            ApplyResponsiveLayout(animate);
        }
    }

    private void ApplyResponsiveLayout(bool animate)
    {
        var size = AppWindow.Size;
        var profile = DashboardResponsiveLayout.Resolve(size.Width, size.Height, IsFullScreenPresenter());
        _presentationMode = profile.Mode;

        CompactTelemetryStrip.Visibility = profile.ShowCompactTelemetry ? Visibility.Visible : Visibility.Collapsed;
        TelemetryCard.Visibility = profile.ShowTelemetryCard ? Visibility.Visible : Visibility.Collapsed;
        ExpandedContextRail.Visibility = profile.ShowContextRail ? Visibility.Visible : Visibility.Collapsed;
        FullScreenContext.Visibility = profile.ShowFullContext ? Visibility.Visible : Visibility.Collapsed;
        CompactButton.Visibility = profile.Mode == DashboardPresentationMode.Compressed ? Visibility.Collapsed : Visibility.Visible;

        var isFullScreen = IsFullScreenPresenter();
        PresentationToggleButton.Content = isFullScreen ? "RESTORE" : profile.Mode == DashboardPresentationMode.Compressed ? "EXPAND" : "FULL SCREEN";
        ToolTipService.SetToolTip(PresentationToggleButton, isFullScreen ? "Return to the expanded dashboard" : profile.Mode == DashboardPresentationMode.Compressed ? "Open the working dashboard" : "Enter full-screen cockpit");

        StateLabelText.FontSize = profile.StateFontSize;
        ReasonText.MaxWidth = profile.ReasonMaxWidth;
        CpuValueText.FontSize = profile.MetricFontSize;
        WattsValueText.FontSize = profile.MetricFontSize;
        ClockValueText.FontSize = profile.MetricFontSize;
        CompactCpuValue.FontSize = Math.Max(14, profile.MetricFontSize - 1);
        CompactWattsValue.FontSize = Math.Max(14, profile.MetricFontSize - 1);
        CompactClockValue.FontSize = Math.Max(14, profile.MetricFontSize - 1);
        DashboardPanel.Padding = new Thickness(profile.PanelHorizontalPadding, profile.PanelVerticalPadding, profile.PanelHorizontalPadding, profile.PanelVerticalPadding + 2);
        HeaderGrid.ColumnSpacing = profile.HeaderColumnSpacing;
        ExpandedContextGrid.ColumnSpacing = profile.ContextColumnSpacing;
        ExpandedContextRail.Padding = new Thickness(profile.PanelHorizontalPadding * 0.75, profile.PanelVerticalPadding, profile.PanelHorizontalPadding * 0.75, profile.PanelVerticalPadding);
        Trajectory.SetLayoutProfile(profile);
        AnimatePresentationContent(animate);
    }

    private bool IsFullScreenPresenter() => AppWindow.Presenter.Kind == AppWindowPresenterKind.FullScreen;

    private void AnimateWindowTo(SizeInt32 target, bool animate)
    {
        _presentationTimer?.Stop();
        _presentationTimer = null;
        var start = AppWindow.Size;
        if (!animate || !ShouldAnimatePresentation())
        {
            _suppressResizeModeSync = true;
            AppWindow.Resize(target);
            _suppressResizeModeSync = false;
            ApplyResponsiveLayout(animate: false);
            return;
        }

        const int frames = 12;
        var frame = 0;
        var timer = _dispatcher.CreateTimer();
        _presentationTimer = timer;
        timer.Interval = TimeSpan.FromMilliseconds(16);
        _suppressResizeModeSync = true;
        timer.Tick += Tick;
        timer.Start();

        void Tick(DispatcherQueueTimer sender, object args)
        {
            frame++;
            var t = Math.Clamp(frame / (double)frames, 0, 1);
            var eased = 1 - Math.Pow(1 - t, 3);
            var width = (int)Math.Round(start.Width + (target.Width - start.Width) * eased);
            var height = (int)Math.Round(start.Height + (target.Height - start.Height) * eased);
            AppWindow.Resize(new SizeInt32(width, height));
            ApplyResponsiveLayout(animate: false);
            if (frame < frames) return;
            sender.Stop();
            sender.Tick -= Tick;
            AppWindow.Resize(target);
            _presentationTimer = null;
            _suppressResizeModeSync = false;
            ApplyResponsiveLayout(animate: false);
        }
    }

    private void AnimatePresentationContent(bool animate)
    {
        if (!animate || !ShouldAnimatePresentation()) return;
        var visual = ElementCompositionPreview.GetElementVisual(DashboardPanel);
        var compositor = visual.Compositor;
        var opacity = compositor.CreateScalarKeyFrameAnimation();
        opacity.Duration = TimeSpan.FromMilliseconds(170);
        opacity.InsertKeyFrame(0, 0.72f);
        opacity.InsertKeyFrame(1, 1f);
        visual.StartAnimation("Opacity", opacity);
        var offset = compositor.CreateVector3KeyFrameAnimation();
        offset.Duration = TimeSpan.FromMilliseconds(190);
        var travel = _presentationMode == DashboardPresentationMode.Compressed ? -5 : _presentationMode == DashboardPresentationMode.FullScreen ? 11 : 8;
        offset.InsertKeyFrame(0, new Vector3(0, travel, 0));
        offset.InsertKeyFrame(1, Vector3.Zero);
        visual.StartAnimation("Offset", offset);
    }
    private bool ShouldAnimatePresentation()
    {
        if (_config.ReducedMotionOverride is bool reduced) return !reduced;
        try { return new UISettings().AnimationsEnabled; }
        catch { return true; }
    }
    private void OnAppWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (DashboardClosePolicy.Decide(_previewMode, _explicitShutdown) == DashboardCloseDisposition.Close) return;
        args.Cancel = true;
        AppWindow.Hide();
        ReleaseDashboardVisibility();
    }

    private void ReleaseDashboardVisibility()
    {
        _visibilityLease?.Dispose();
        _visibilityLease = null;
    }

    private void OnSnapshotChanged(object? sender, ControllerSnapshot snapshot) => _dispatcher.TryEnqueue(() =>
    {
        ViewModel.UpdateContinuity(snapshot, _recorder.History, _recorder.LatestRichTelemetry);
        ApplyVisualState(snapshot);
    });

    private void OnContinuityChanged(object? sender, EventArgs e) => _dispatcher.TryEnqueue(() =>
    {
        var snapshot = _controller.Snapshot;
        ViewModel.UpdateContinuity(snapshot, _recorder.History, _recorder.LatestRichTelemetry);
        ApplyVisualState(snapshot);
    });

    private void ApplyVisualState(ControllerSnapshot snapshot)
    {
        var model = TrajectoryProjection.Create(_recorder.History, snapshot, _config);
        Trajectory.Apply(model, ViewModel.Samples, _config, snapshot.History, _graphWindowSeconds);
    }

    private async void OnTrajectoryAutoRequested(object? sender, EventArgs e)
    {
        if (string.Equals(_controller.Snapshot.LatchType, "Manual", StringComparison.OrdinalIgnoreCase))
            await _controller.ReleaseManualLatchAsync();
        ApplyVisualState(_controller.Snapshot);
    }

    private async void OnTrajectoryManualStateRequested(object? sender, TrajectoryManualStateEventArgs e) => await _controller.SetManualStateAsync(e.State);

    private void OnTrajectoryRangeChanged(object? sender, TrajectoryRangeChangedEventArgs e)
    {
        _graphWindowSeconds = e.Seconds;
        ApplyVisualState(_controller.Snapshot);
    }

    private void OnThresholdsPreviewed(object? sender, ThresholdsChangedEventArgs e)
    {
        _config = _config with { QuietThresholdPercent = e.QuietPercent, CpuPromotionThresholdPercent = e.PromotionPercent };
        ViewModel.Configure(_config);
        ApplyVisualState(_controller.Snapshot);
    }

    private async void OnThresholdsCommitted(object? sender, ThresholdsChangedEventArgs e)
    {
        await ApplyConfigFromPageAsync(_config with { QuietThresholdPercent = e.QuietPercent, CpuPromotionThresholdPercent = e.PromotionPercent });
    }

    private async Task ApplyConfigFromPageAsync(PowerFlowConfig config)
    {
        await _applyConfig(config);
        _config = config;
        ApplyTheme(config.Theme);
        ViewModel.Configure(config);
        ApplyVisualState(_controller.Snapshot);
        RulesPanel.RefreshConfig(config);
        SettingsPanel.RefreshConfig(config);
    }

    private void ApplyTheme(ThemePreference theme)
    {
        Root.RequestedTheme = theme switch
        {
            ThemePreference.Light => ElementTheme.Light,
            ThemePreference.Dark => ElementTheme.Dark,
            _ => ElementTheme.Default
        };
    }

    private async Task<string?> BrowseExecutableAsync()
    {
        var picker = new FileOpenPicker { ViewMode = PickerViewMode.List, SuggestedStartLocation = PickerLocationId.ComputerFolder };
        picker.FileTypeFilter.Add(".exe");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
        var file = await picker.PickSingleFileAsync();
        return file?.Path;
    }

    private void OnNavigationChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        var tag = (args.SelectedItemContainer?.Tag as string) ?? "flow";
        if (tag != "flow" && _presentationMode == DashboardPresentationMode.Compressed)
            ApplyPresentationMode(DashboardPresentationMode.Expanded, animate: true);
        DashboardPanel.Visibility = tag == "flow" ? Visibility.Visible : Visibility.Collapsed;
        RulesPanel.Visibility = tag == "rules" ? Visibility.Visible : Visibility.Collapsed;
        SettingsPanel.Visibility = tag == "settings" ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SelectSection(string tag)
    {
        foreach (var item in Navigation.MenuItems.OfType<NavigationViewItem>())
        {
            if (string.Equals(item.Tag as string, tag, StringComparison.OrdinalIgnoreCase))
            {
                Navigation.SelectedItem = item;
                return;
            }
        }
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        if (_closed) return;
        _closed = true;
        _presentationTimer?.Stop();
        _presentationTimer = null;
        AppWindow.Closing -= OnAppWindowClosing;
        AppWindow.Changed -= OnAppWindowChanged;
        _controller.SnapshotChanged -= OnSnapshotChanged;
        _recorder.ContinuityChanged -= OnContinuityChanged;
        Trajectory.AutoRequested -= OnTrajectoryAutoRequested;
        Trajectory.ManualStateRequested -= OnTrajectoryManualStateRequested;
        Trajectory.RangeChanged -= OnTrajectoryRangeChanged;
        Trajectory.ThresholdsPreviewed -= OnThresholdsPreviewed;
        Trajectory.ThresholdsCommitted -= OnThresholdsCommitted;
        ReleaseDashboardVisibility();
    }
}
