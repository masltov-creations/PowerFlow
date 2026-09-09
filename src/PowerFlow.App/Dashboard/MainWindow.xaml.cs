using System.Numerics;
using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using PowerFlow.App.Controller;
using PowerFlow.App.Telemetry;
using PowerFlow.App.Tray;
using PowerFlow.Core.Policy;
using PowerFlow.Core.Rules;
using PowerFlow.Windows.Activity;
using Windows.Graphics;
using Windows.Storage.Pickers;
using Windows.UI.ViewManagement;

namespace PowerFlow.App.Dashboard;

public sealed partial class MainWindow : Window
{
    private const int GwlExStyle = -20;
    private const long WS_EX_TOOLWINDOW = 0x00000080L;
    private const long WS_EX_NOACTIVATE = 0x08000000L;
    private const int SwHide = 0;
    private const int SwShowNoActivate = 4;
    private readonly PowerFlowController _controller;
    private readonly Func<PowerFlowConfig, Task> _applyConfig;
    private readonly TelemetryContinuityRecorder _recorder;
    private TelemetryVisibilityLease? _visibilityLease;
    private readonly DispatcherQueue _dispatcher;
    private readonly bool _previewMode;
    private readonly IntPtr _hwnd;
    private bool _explicitShutdown;
    private PowerFlowConfig _config;
    private bool _closed;
    private bool _shellVisible;
    private double _graphWindowSeconds = 60;
    private PowerFlowShellState _shellState = PowerFlowShellState.Hidden;
    private ShellActivationMode _activationMode = ShellActivationMode.PinnedActive;
    private TrayRect? _lastTrayAnchor;
    private TrayRect? _lastWorkArea;
    private string _currentSection = "flow";
    private DispatcherQueueTimer? _presentationTimer;
    private bool _suppressResizeModeSync;

    public PowerFlowShellState ShellState => _shellState;
    public ShellActivationMode ActivationMode => _activationMode;
    public bool IsShellVisible => _shellVisible;
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
        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        ApplyTheme(config.Theme);
        ViewModel.Configure(config);
        ShellRoot.DataContext = ViewModel;
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
        AppWindow.Resize(new SizeInt32(320, 176));
        ApplyShellLayout(PowerFlowShellState.Hidden, 320, 176);
        try { SystemBackdrop = new MicaBackdrop(); } catch { }
        SelectSection("flow");
    }

    public Task ShowAsync(bool showSettings = false, bool fullScreen = false)
        => ShowShellAsync(fullScreen ? PowerFlowShellState.FullScreen : showSettings ? PowerFlowShellState.Expanded : PowerFlowShellState.Compact,
            ShellActivationMode.PinnedActive, null, null, showSettings ? "settings" : "flow", animate: false);

    public async Task ShowShellAsync(PowerFlowShellState state, ShellActivationMode activation, TrayRect? trayAnchor, TrayRect? workArea, string section = "flow", bool animate = true)
    {
        if (trayAnchor is { } anchor) _lastTrayAnchor = anchor;
        if (workArea is { } area) _lastWorkArea = area;
        _currentSection = string.IsNullOrWhiteSpace(section) ? "flow" : section;
        if (_currentSection != "flow" && state == PowerFlowShellState.Compact) state = PowerFlowShellState.Expanded;
        _visibilityLease ??= _recorder.AcquireVisibility();
        ViewModel.UpdateContinuity(_controller.Snapshot, _recorder.History, _recorder.LatestRichTelemetry);
        ApplyVisualState(_controller.Snapshot);
        SelectSection(_currentSection);
        await TransitionToAsync(state, activation, animate);
    }

    public async Task TransitionToAsync(PowerFlowShellState state, ShellActivationMode activation, bool animate)
    {
        if (state == PowerFlowShellState.Hidden)
        {
            await HideShellAsync();
            return;
        }

        if (state == PowerFlowShellState.FullScreen)
        {
            _presentationTimer?.Stop();
            _presentationTimer = null;
            _shellState = state;
            _activationMode = ShellActivationMode.PinnedActive;
            ApplyActivationMode(_activationMode, state);
            _suppressResizeModeSync = true;
            AppWindow.SetPresenter(AppWindowPresenterKind.FullScreen);
            _suppressResizeModeSync = false;
            _shellVisible = true;
            ApplyShellLayout(state, AppWindow.Size.Width, AppWindow.Size.Height);
            Activate();
            return;
        }

        if (IsFullScreenPresenter())
        {
            _suppressResizeModeSync = true;
            AppWindow.SetPresenter(AppWindowPresenterKind.Overlapped);
            _suppressResizeModeSync = false;
        }

        var fromState = _shellVisible ? _shellState : PowerFlowShellState.Hidden;
        _shellState = state;
        _activationMode = activation;
        ApplyActivationMode(activation, state);
        var target = ResolveTargetBounds(state);
        var start = CurrentBounds();
        if (!_shellVisible)
        {
            start = ResolveTargetBounds(PowerFlowShellState.Hidden);
            AppWindow.MoveAndResize(start);
            ApplyShellLayout(state, start.Width, start.Height);
            ApplyTransitionDetailProgress(fromState, state, 0);
            if (activation == ShellActivationMode.TransientNoActivate) ShowWindow(_hwnd, SwShowNoActivate);
            else Activate();
            _shellVisible = true;
        }
        else if (activation == ShellActivationMode.PinnedActive)
        {
            Activate();
        }

        await AnimateShellBoundsAsync(start, target, fromState, state, animate);
        ApplyShellLayout(state, target.Width, target.Height);
        ResetTransitionDetailPresentation();
        if (activation == ShellActivationMode.PinnedActive) Activate();
    }

    public async Task HideShellAsync()
    {
        _presentationTimer?.Stop();
        _presentationTimer = null;
        if (!_shellVisible)
        {
            _shellState = PowerFlowShellState.Hidden;
            ReleaseDashboardVisibility();
            return;
        }

        var fromState = _shellState;
        var start = CurrentBounds();
        var target = ResolveTargetBounds(PowerFlowShellState.Hidden);
        await AnimateShellBoundsAsync(start, target, fromState, PowerFlowShellState.Hidden, animate: true);
        ShowWindow(_hwnd, SwHide);
        _shellVisible = false;
        _shellState = PowerFlowShellState.Hidden;
        GlanceTapTarget.Visibility = Visibility.Collapsed;
        ResetTransitionDetailPresentation();
        ReleaseDashboardVisibility();
    }

    public TrayRect ShellBounds
    {
        get
        {
            var p = AppWindow.Position;
            var s = AppWindow.Size;
            return new TrayRect(p.X, p.Y, p.X + s.Width, p.Y + s.Height);
        }
    }

    public bool ContainsCursor()
    {
        if (!_shellVisible || !GetCursorPos(out var point)) return false;
        return ShellBounds.Contains(point.X, point.Y);
    }

    public void CloseForShutdown()
    {
        _explicitShutdown = true;
        Close();
    }

    private async void OnGlanceTapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
    {
        if (_shellState != PowerFlowShellState.Glance || _activationMode != ShellActivationMode.PinnedActive) return;
        await TransitionToAsync(PowerFlowShellState.Compact, ShellActivationMode.PinnedActive, animate: true);
    }

    private async void OnPresentationToggleClicked(object sender, RoutedEventArgs e)
    {
        var next = _shellState switch
        {
            PowerFlowShellState.Compact => PowerFlowShellState.Expanded,
            PowerFlowShellState.Expanded => PowerFlowShellState.FullScreen,
            PowerFlowShellState.FullScreen => PowerFlowShellState.Expanded,
            _ => PowerFlowShellState.Compact
        };
        await TransitionToAsync(next, ShellActivationMode.PinnedActive, animate: true);
    }

    private async void OnCompactClicked(object sender, RoutedEventArgs e)
        => await TransitionToAsync(PowerFlowShellState.Compact, ShellActivationMode.PinnedActive, animate: true);

    private void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if ((!args.DidSizeChange && !args.DidPresenterChange) || _suppressResizeModeSync || !_shellVisible) return;
        if (IsFullScreenPresenter()) _shellState = PowerFlowShellState.FullScreen;
        else if (_shellState != PowerFlowShellState.Glance)
            _shellState = AppWindow.Size.Width >= 900 && AppWindow.Size.Height >= 560 ? PowerFlowShellState.Expanded : PowerFlowShellState.Compact;
        ApplyShellLayout(_shellState, AppWindow.Size.Width, AppWindow.Size.Height);
    }

    private void ApplyShellLayout(PowerFlowShellState state, int width, int height)
    {
        var shell = PowerFlowShellLayout.Resolve(width, height, state, _currentSection);
        if (shell.State != state && state == PowerFlowShellState.Compact) _shellState = shell.State;
        var glance = state == PowerFlowShellState.Glance;
        var compact = state == PowerFlowShellState.Compact;
        var expanded = state is PowerFlowShellState.Expanded or PowerFlowShellState.FullScreen;

        NavigationRail.IsPaneVisible = shell.ShowNavigationRail;
        NavigationRail.IsPaneToggleButtonVisible = shell.ShowNavigationRail;
        ModeSelectorHost.Visibility = shell.ShowModeCards ? Visibility.Visible : Visibility.Collapsed;
        LiveStatsPanel.Visibility = shell.ShowLiveStatsPanel ? Visibility.Visible : Visibility.Collapsed;
        LowerContextGrid.Visibility = shell.ShowLowerContextPanels ? Visibility.Visible : Visibility.Collapsed;
        BrandTagline.Visibility = state == PowerFlowShellState.Glance ? Visibility.Collapsed : Visibility.Visible;
        NavigationRail.PaneDisplayMode = shell.ShowNavigationRail ? NavigationViewPaneDisplayMode.Left : NavigationViewPaneDisplayMode.LeftMinimal;
        if (shell.ShowNavigationRail) NavigationRail.OpenPaneLength = shell.NavigationWidth;
        GlanceTapTarget.Visibility = glance && _activationMode == ShellActivationMode.PinnedActive ? Visibility.Visible : Visibility.Collapsed;
        PresentationActions.Visibility = glance ? Visibility.Collapsed : Visibility.Visible;
        CompactTelemetryStrip.Visibility = glance || compact ? Visibility.Visible : Visibility.Collapsed;
        TelemetryCard.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        ExpandedContextRail.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        FullScreenContext.Visibility = state == PowerFlowShellState.FullScreen ? Visibility.Visible : Visibility.Collapsed;
        CompactButton.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        PresentationToggleButton.Content = state switch
        {
            PowerFlowShellState.Compact => "EXPAND",
            PowerFlowShellState.Expanded => "FULL SCREEN",
            PowerFlowShellState.FullScreen => "RESTORE",
            _ => "OPEN"
        };

        StateLabelText.FontSize = glance ? 16 : compact ? 18 : 21;
        ReasonText.MaxWidth = glance ? 160 : compact ? 280 : Math.Clamp(width * 0.32, 360, 620);
        DashboardPanel.Padding = new Thickness(shell.ContentPadding);
        DashboardPanel.RowSpacing = shell.PanelGap;
        HeaderGrid.ColumnSpacing = shell.PanelGap;
        ExpandedContextGrid.ColumnSpacing = shell.PanelGap;

        DashboardLayoutProfile legacy;
        if (glance)
        {
            legacy = new DashboardLayoutProfile(DashboardPresentationMode.Compressed, true, false, false, false,
                16, 13, 6, 4, 6, 160, shell.GraphHeight, 3, 180, 8);
            Trajectory.Height = shell.GraphHeight;
            Trajectory.MinHeight = 0;
        }
        else
        {
            legacy = DashboardResponsiveLayout.Resolve(width, height, state == PowerFlowShellState.FullScreen);
            Trajectory.Height = double.NaN;
            Trajectory.MinHeight = 0;
        }
        Trajectory.SetLayoutProfile(legacy);
        CpuValueText.FontSize = legacy.MetricFontSize;
        WattsValueText.FontSize = legacy.MetricFontSize;
        ClockValueText.FontSize = legacy.MetricFontSize;
        CompactCpuValue.FontSize = Math.Max(14, legacy.MetricFontSize - 1);
        CompactWattsValue.FontSize = Math.Max(14, legacy.MetricFontSize - 1);
        CompactClockValue.FontSize = Math.Max(14, legacy.MetricFontSize - 1);
    }

    private RectInt32 ResolveTargetBounds(PowerFlowShellState state)
    {
        if (_lastTrayAnchor is { } tray && _lastWorkArea is { } work)
            return ShellTransitionGeometry.TargetBounds(tray, work, CurrentBounds(), state);

        var current = CurrentBounds();
        var (width, height) = state switch
        {
            PowerFlowShellState.Hidden => (1, 1),
            PowerFlowShellState.Glance => (320, 176),
            PowerFlowShellState.Compact => (760, 440),
            PowerFlowShellState.Expanded => (1280, 800),
            _ => (current.Width, current.Height)
        };
        return new RectInt32(current.X, current.Y, width, height);
    }

    private RectInt32 CurrentBounds()
    {
        var p = AppWindow.Position;
        var s = AppWindow.Size;
        return new RectInt32(p.X, p.Y, Math.Max(1, s.Width), Math.Max(1, s.Height));
    }

    private Task AnimateShellBoundsAsync(RectInt32 start, RectInt32 target, PowerFlowShellState fromState, PowerFlowShellState toState, bool animate)
    {
        _presentationTimer?.Stop();
        _presentationTimer = null;
        var reducedMotion = !animate || !ShouldAnimatePresentation();
        var duration = ShellMotionPolicy.Duration(fromState, toState, reducedMotion);
        if (duration == TimeSpan.Zero || start.Equals(target))
        {
            _suppressResizeModeSync = true;
            AppWindow.MoveAndResize(target);
            _suppressResizeModeSync = false;
            ApplyShellLayout(toState, target.Width, target.Height);
            ResetTransitionDetailPresentation();
            return Task.CompletedTask;
        }

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var frames = Math.Max(1, (int)Math.Round(duration.TotalMilliseconds / 16d));
        var frame = 0;
        var timer = _dispatcher.CreateTimer();
        _presentationTimer = timer;
        timer.Interval = TimeSpan.FromMilliseconds(16);
        _suppressResizeModeSync = true;
        timer.Tick += Tick;
        timer.Start();
        return tcs.Task;

        void Tick(DispatcherQueueTimer sender, object args)
        {
            frame++;
            var t = Math.Clamp(frame / (double)frames, 0, 1);
            var eased = ShellMotionPolicy.Ease(fromState, toState, t);
            var detail = ShellMotionPolicy.DetailProgress(fromState, toState, t);
            var rect = ShellTransitionGeometry.Interpolate(start, target, eased);
            AppWindow.MoveAndResize(rect);
            var layoutState = ShellMotionPolicy.IsGrowth(fromState, toState) || detail <= 0.001 ? toState : fromState;
            ApplyShellLayout(layoutState, rect.Width, rect.Height);
            ApplyTransitionDetailProgress(fromState, toState, detail);
            if (frame < frames) return;
            sender.Stop();
            sender.Tick -= Tick;
            AppWindow.MoveAndResize(target);
            _presentationTimer = null;
            _suppressResizeModeSync = false;
            ApplyShellLayout(toState, target.Width, target.Height);
            ResetTransitionDetailPresentation();
            tcs.TrySetResult();
        }
    }

    private void ApplyTransitionDetailProgress(PowerFlowShellState fromState, PowerFlowShellState toState, double progress)
    {
        var value = Math.Clamp(progress, 0d, 1d);
        foreach (var element in TransitionDetailElements(fromState, toState))
        {
            element.Opacity = value;
            var visual = ElementCompositionPreview.GetElementVisual(element);
            visual.Offset = new Vector3(0, (float)((1d - value) * 7d), 0);
        }
    }

    private IEnumerable<UIElement> TransitionDetailElements(PowerFlowShellState fromState, PowerFlowShellState toState)
    {
        var low = Math.Min(ShellMotionPolicy.Rank(fromState), ShellMotionPolicy.Rank(toState));
        var high = Math.Max(ShellMotionPolicy.Rank(fromState), ShellMotionPolicy.Rank(toState));
        if (low < ShellMotionPolicy.Rank(PowerFlowShellState.Compact) && high >= ShellMotionPolicy.Rank(PowerFlowShellState.Compact))
        {
            yield return ModeSelectorHost;
            yield return PresentationActions;
        }
        if (low < ShellMotionPolicy.Rank(PowerFlowShellState.Expanded) && high >= ShellMotionPolicy.Rank(PowerFlowShellState.Expanded))
        {
            yield return NavigationRail;
            yield return LiveStatsPanel;
            yield return LowerContextGrid;
            yield return TelemetryCard;
            yield return ExpandedContextRail;
        }
    }

    private void ResetTransitionDetailPresentation()
    {
        foreach (var element in new UIElement[] { ModeSelectorHost, PresentationActions, NavigationRail, LiveStatsPanel, LowerContextGrid, TelemetryCard, ExpandedContextRail })
        {
            element.Opacity = 1;
            ElementCompositionPreview.GetElementVisual(element).Offset = Vector3.Zero;
        }
    }

    private void ApplyActivationMode(ShellActivationMode mode, PowerFlowShellState state)
    {
        var style = GetWindowLongPtr(_hwnd, GwlExStyle).ToInt64();
        style = mode == ShellActivationMode.TransientNoActivate
            ? style | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE
            : style & ~WS_EX_NOACTIVATE & ~WS_EX_TOOLWINDOW;
        SetWindowLongPtr(_hwnd, GwlExStyle, new IntPtr(style));
        AppWindow.IsShownInSwitchers = mode == ShellActivationMode.PinnedActive && state != PowerFlowShellState.Glance;
        if (AppWindow.Presenter is not OverlappedPresenter presenter) return;
        var glance = state == PowerFlowShellState.Glance;
        presenter.IsResizable = !glance;
        presenter.IsMaximizable = !glance;
        presenter.IsMinimizable = !glance;
        presenter.SetBorderAndTitleBar(!glance, !glance);
    }

    private bool IsFullScreenPresenter() => AppWindow.Presenter.Kind == AppWindowPresenterKind.FullScreen;

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
        _ = HideShellAsync();
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
        UpdateModeSelector(snapshot);
        var model = TrajectoryProjection.Create(_recorder.History, snapshot, _config);
        Trajectory.Apply(model, ViewModel.Samples, _config, snapshot.History, _graphWindowSeconds);
    }

    private async void OnAutoModeClicked(object sender, RoutedEventArgs e)
    {
        if (string.Equals(_controller.Snapshot.LatchType, "Manual", StringComparison.OrdinalIgnoreCase))
            await _controller.ReleaseManualLatchAsync();
        ApplyVisualState(_controller.Snapshot);
    }

    private async void OnSaverModeClicked(object sender, RoutedEventArgs e) => await _controller.SetManualStateAsync(PowerState.PowerSaver);
    private async void OnBalancedModeClicked(object sender, RoutedEventArgs e) => await _controller.SetManualStateAsync(PowerState.Balanced);
    private async void OnPerformanceModeClicked(object sender, RoutedEventArgs e) => await _controller.SetManualStateAsync(PowerState.HighPerformance);

    private async void OnOpenRulesClicked(object sender, RoutedEventArgs e)
    {
        SelectSection("rules");
        await TransitionToAsync(PowerFlowShellState.Expanded, ShellActivationMode.PinnedActive, animate: true);
    }

    private async void OnOpenSettingsClicked(object sender, RoutedEventArgs e)
    {
        SelectSection("settings");
        await TransitionToAsync(PowerFlowShellState.Expanded, ShellActivationMode.PinnedActive, animate: true);
    }

    private void UpdateModeSelector(ControllerSnapshot snapshot)
    {
        var manual = snapshot.IsLatched && string.Equals(snapshot.LatchType, "Manual", StringComparison.OrdinalIgnoreCase);
        var game = snapshot.IsLatched && string.Equals(snapshot.LatchType, "Game", StringComparison.OrdinalIgnoreCase);
        AutoModeButton.IsChecked = !manual;
        SaverModeButton.IsChecked = snapshot.State == PowerState.PowerSaver;
        BalancedModeButton.IsChecked = snapshot.State == PowerState.Balanced;
        PerformanceModeButton.IsChecked = snapshot.State == PowerState.HighPerformance;
        SaverModeButton.IsEnabled = !game;
        BalancedModeButton.IsEnabled = !game;
        PerformanceModeButton.IsEnabled = !game;
        AutoModeDetail.Text = manual ? "Release manual lock" : game ? "Game latch active" : "Rules · Apps · Smart";
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
        ShellRoot.RequestedTheme = theme switch
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

    private async void OnNavigationChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        var tag = (args.SelectedItemContainer?.Tag as string) ?? "flow";
        _currentSection = tag;
        if (tag != "flow" && _shellState is PowerFlowShellState.Glance or PowerFlowShellState.Compact)
            await TransitionToAsync(PowerFlowShellState.Expanded, ShellActivationMode.PinnedActive, animate: true);
        DashboardPanel.Visibility = tag == "flow" ? Visibility.Visible : Visibility.Collapsed;
        RulesPanel.Visibility = tag == "rules" ? Visibility.Visible : Visibility.Collapsed;
        SettingsPanel.Visibility = tag == "settings" ? Visibility.Visible : Visibility.Collapsed;
    }
    private void SelectSection(string tag)
    {
        foreach (var item in NavigationRail.MenuItems.OfType<NavigationViewItem>())
        {
            if (string.Equals(item.Tag as string, tag, StringComparison.OrdinalIgnoreCase))
            {
                NavigationRail.SelectedItem = item;
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
    [StructLayout(LayoutKind.Sequential)]
    private struct PointNative { public int X; public int Y; }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hwnd, int command);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out PointNative point);
}
