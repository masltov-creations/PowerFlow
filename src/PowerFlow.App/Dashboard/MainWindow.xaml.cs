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
            if (activation == ShellActivationMode.TransientNoActivate) ShowWindow(_hwnd, SwShowNoActivate);
            else Activate();
            _shellVisible = true;
        }
        else if (activation == ShellActivationMode.PinnedActive)
        {
            Activate();
        }

        await AnimateShellBoundsAsync(start, target, state, animate);
        ApplyShellLayout(state, target.Width, target.Height);
        if (activation == ShellActivationMode.PinnedActive) Activate();
    }

    public Task HideShellAsync()
    {
        _presentationTimer?.Stop();
        _presentationTimer = null;
        if (_shellVisible) ShowWindow(_hwnd, SwHide);
        _shellVisible = false;
        _shellState = PowerFlowShellState.Hidden;
        GlanceTapTarget.Visibility = Visibility.Collapsed;
        ReleaseDashboardVisibility();
        return Task.CompletedTask;
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

        Navigation.IsPaneVisible = shell.ShowNavigationRail;
        Navigation.IsPaneToggleButtonVisible = shell.ShowNavigationRail;
        Navigation.PaneDisplayMode = shell.ShowNavigationRail ? NavigationViewPaneDisplayMode.Left : NavigationViewPaneDisplayMode.LeftMinimal;
        if (shell.ShowNavigationRail) Navigation.OpenPaneLength = shell.NavigationWidth;
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

    private Task AnimateShellBoundsAsync(RectInt32 start, RectInt32 target, PowerFlowShellState state, bool animate)
    {
        _presentationTimer?.Stop();
        _presentationTimer = null;
        if (!animate || !ShouldAnimatePresentation() || start.Equals(target))
        {
            _suppressResizeModeSync = true;
            AppWindow.MoveAndResize(target);
            _suppressResizeModeSync = false;
            ApplyShellLayout(state, target.Width, target.Height);
            return Task.CompletedTask;
        }

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        const int frames = 12;
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
            var eased = 1 - Math.Pow(1 - t, 3);
            var rect = ShellTransitionGeometry.Interpolate(start, target, eased);
            AppWindow.MoveAndResize(rect);
            ApplyShellLayout(state, rect.Width, rect.Height);
            if (frame < frames) return;
            sender.Stop();
            sender.Tick -= Tick;
            AppWindow.MoveAndResize(target);
            _presentationTimer = null;
            _suppressResizeModeSync = false;
            ApplyShellLayout(state, target.Width, target.Height);
            tcs.TrySetResult();
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
