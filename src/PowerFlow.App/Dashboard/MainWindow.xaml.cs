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
using PowerFlow.Core.Envelope;
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
        SystemHeaderHost.IsPreviewMode = previewMode;
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
        var profile = PowerFlowShellLayout.Resolve(width, height, state, _currentSection);
        if (profile.State != state && state == PowerFlowShellState.Compact) _shellState = profile.State;

        SystemHeaderHost.Presentation = profile.Header;
        PerformanceTimeline.SetPresentation(profile.Timeline);
        ApplyNavigationPresentation(profile.Navigation, profile.Geometry.NavigationWidth);
        ApplyCockpitGeometry(profile, state, width, height);

        var glance = state == PowerFlowShellState.Glance;
        var expanded = state is PowerFlowShellState.Expanded or PowerFlowShellState.FullScreen;
        GlanceTapTarget.Visibility = glance && _activationMode == ShellActivationMode.PinnedActive ? Visibility.Visible : Visibility.Collapsed;
        PresentationActions.Visibility = glance ? Visibility.Collapsed : Visibility.Visible;
        CompactButton.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        PresentationToggleButton.Content = state switch
        {
            PowerFlowShellState.Compact => "EXPAND",
            PowerFlowShellState.Expanded => "FULL SCREEN",
            PowerFlowShellState.FullScreen => "RESTORE",
            _ => "OPEN"
        };
    }

    private void ApplyNavigationPresentation(NavigationPresentation presentation, double openWidth)
    {
        NavigationRail.OpenPaneLength = Math.Max(128, openWidth);
        NavigationRail.CompactPaneLength = 46;
        switch (presentation)
        {
            case NavigationPresentation.Rail:
                NavigationRail.IsPaneVisible = true;
                NavigationRail.IsPaneToggleButtonVisible = false;
                NavigationRail.PaneDisplayMode = NavigationViewPaneDisplayMode.Left;
                NavigationRail.IsPaneOpen = true;
                break;
            case NavigationPresentation.Overlay:
                NavigationRail.IsPaneVisible = false;
                NavigationRail.IsPaneToggleButtonVisible = false;
                NavigationRail.PaneDisplayMode = NavigationViewPaneDisplayMode.LeftMinimal;
                NavigationRail.IsPaneOpen = false;
                break;
            default:
                NavigationRail.IsPaneVisible = false;
                NavigationRail.IsPaneToggleButtonVisible = false;
                NavigationRail.PaneDisplayMode = NavigationViewPaneDisplayMode.LeftMinimal;
                NavigationRail.IsPaneOpen = false;
                break;
        }
    }

    private void ApplyCockpitGeometry(ShellPresentationProfile profile, PowerFlowShellState state, int width, int height)
    {
        CockpitSurface.Padding = new Thickness(profile.Geometry.ContentPadding);
        CockpitSurface.RowSpacing = profile.Geometry.Gap;
        SystemHeaderRow.ColumnSpacing = profile.Geometry.Gap;
        AdaptiveControlRegion.ColumnSpacing = profile.Geometry.Gap;

        SystemHeaderRowDefinition.Height = new GridLength(Math.Max(0, profile.Geometry.HeaderHeight));
        TimelineRowDefinition.Height = new GridLength(1, GridUnitType.Star);
        AdaptiveControlRowDefinition.Height = profile.GovernorControls switch
        {
            GovernorControlPresentation.Summary => new GridLength(0),
            GovernorControlPresentation.Bias => new GridLength(82),
            GovernorControlPresentation.Contextual => new GridLength(Math.Max(108, profile.Geometry.ControlBandHeight)),
            _ => new GridLength(Math.Max(132, profile.Geometry.ControlBandHeight + 18))
        };
        AdaptiveControlRegion.Visibility = profile.GovernorControls == GovernorControlPresentation.Summary ? Visibility.Collapsed : Visibility.Visible;
        SelectedActorPanel.Visibility = profile.GovernorControls == GovernorControlPresentation.Bias && width < 680 ? Visibility.Collapsed : Visibility.Visible;
        FooterRowDefinition.Height = state is PowerFlowShellState.Expanded or PowerFlowShellState.FullScreen ? GridLength.Auto : new GridLength(0);
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
        var fromProfile = PowerFlowShellLayout.Resolve(Math.Max(1, start.Width), Math.Max(1, start.Height), fromState, _currentSection);
        var toProfile = PowerFlowShellLayout.Resolve(Math.Max(1, target.Width), Math.Max(1, target.Height), toState, _currentSection);

        if (duration == TimeSpan.Zero || start.Equals(target))
        {
            _suppressResizeModeSync = true;
            AppWindow.MoveAndResize(target);
            _suppressResizeModeSync = false;
            ApplyShellLayout(toState, target.Width, target.Height);
            ApplyShellTransitionFrame(fromProfile, toProfile, 1d, reducedMotion: true);
            ResetSemanticMorphPresentation(toProfile);
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
            var t = Math.Clamp(frame / (double)frames, 0d, 1d);
            var eased = ShellMotionPolicy.Ease(fromState, toState, t);
            var rect = ShellTransitionGeometry.Interpolate(start, target, eased);
            AppWindow.MoveAndResize(rect);

            var layoutState = ShellMotionPolicy.IsGrowth(fromState, toState) ? toState : fromState;
            ApplyShellLayout(layoutState, rect.Width, rect.Height);
            ApplyShellTransitionFrame(fromProfile, toProfile, t, reducedMotion: false);

            if (frame < frames) return;
            sender.Stop();
            sender.Tick -= Tick;
            AppWindow.MoveAndResize(target);
            _presentationTimer = null;
            _suppressResizeModeSync = false;
            ApplyShellTransitionFrame(fromProfile, toProfile, 1d, reducedMotion: false);
            ApplyShellLayout(toState, target.Width, target.Height);
            ResetSemanticMorphPresentation(toProfile);
            tcs.TrySetResult();
        }
    }

    private void ApplyShellTransitionFrame(
        ShellPresentationProfile from,
        ShellPresentationProfile to,
        double progress,
        bool reducedMotion)
    {
        if (reducedMotion)
        {
            SystemHeaderHost.ApplyMorph(from.Header, to.Header, 1d, reducedMotion: true);
            PerformanceTimeline.SetPresentation(to.Timeline);
            ResetSemanticMorphPresentation(to);
            return;
        }

        var t = Math.Clamp(progress, 0d, 1d);
        var collapsing = !ShellMotionPolicy.IsGrowth(from.State, to.State);
        SystemHeaderHost.ApplyMorph(from.Header, to.Header, ShellMotionPolicy.PrimaryAnchorProgress(t), reducedMotion: false);
        PerformanceTimeline.SetPresentation(t < 0.46 ? from.Timeline : to.Timeline);
        ApplyNavigationMorph(from.Navigation, to.Navigation, ShellMotionPolicy.NavigationProgress(t, collapsing));
        ApplyPresentationActionsMorph(from.State, to.State, ShellMotionPolicy.ModeMorphProgress(t));
    }

    private void ApplyNavigationMorph(NavigationPresentation from, NavigationPresentation to, double visibility)
    {
        if (from != NavigationPresentation.Rail && to != NavigationPresentation.Rail)
        {
            NavigationRail.Opacity = 1d;
            ElementCompositionPreview.GetElementVisual(NavigationRail).Offset = Vector3.Zero;
            return;
        }
        var value = Math.Clamp(visibility, 0d, 1d);
        NavigationRail.Opacity = value;
        ElementCompositionPreview.GetElementVisual(NavigationRail).Offset = new Vector3((float)(-10d * (1d - value)), 0, 0);
    }

    private void ApplyPresentationActionsMorph(PowerFlowShellState from, PowerFlowShellState to, double targetProgress)
    {
        var fromVisible = from != PowerFlowShellState.Glance && from != PowerFlowShellState.Hidden;
        var toVisible = to != PowerFlowShellState.Glance && to != PowerFlowShellState.Hidden;
        if (fromVisible == toVisible)
        {
            PresentationActions.Opacity = 1d;
            ElementCompositionPreview.GetElementVisual(PresentationActions).Offset = Vector3.Zero;
            return;
        }
        var p = Math.Clamp(targetProgress, 0d, 1d);
        var value = toVisible ? p : 1d - p;
        PresentationActions.Opacity = value;
        ElementCompositionPreview.GetElementVisual(PresentationActions).Offset = new Vector3(0, (float)(-4d * (1d - value)), 0);
    }

    private void ResetSemanticMorphPresentation(ShellPresentationProfile target)
    {
        SystemHeaderHost.ApplyMorph(target.Header, target.Header, 1d, reducedMotion: true);
        PerformanceTimeline.SetPresentation(target.Timeline);
        foreach (var element in new UIElement[] { PresentationActions, NavigationRail, StatusFooter })
        {
            element.Opacity = 1d;
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
        var calibration = EnvelopeCalibration.Calibrate(ViewModel.OperatingHistory);
        PerformanceTimeline.Apply(ViewModel.OperatingHistory, calibration.Envelope, _graphWindowSeconds);
        ModelConfidenceText.Text = calibration.Confidence == EnvelopeConfidence.Low
            ? "LEARNING"
            : $"{calibration.Confidence.ToString().ToUpperInvariant()} CONFIDENCE";
        EnvelopeSummaryText.Text = calibration.SustainedEfficiencyFrontierWatts is double frontier
            ? $"Efficient frontier near {frontier:0} W for observed sustained work"
            : "Learning this machine's efficient operating regions";

        var actor = ShortActor(snapshot.TriggerApplication);
        SelectedActorNameText.Text = string.IsNullOrWhiteSpace(actor) ? "SYSTEM / NO DOMINANT ACTOR" : actor;
        DecisionStateText.Text = snapshot.IsLatched
            ? $"{(snapshot.LatchType ?? "LOCK").ToUpperInvariant()}"
            : snapshot.State switch
            {
                PowerState.PowerSaver => "ECO",
                PowerState.Balanced => "EFFICIENT",
                PowerState.HighPerformance => "BOOST",
                _ => "OBSERVING"
            };
        DecisionExplanationText.Text = string.IsNullOrWhiteSpace(snapshot.Reason) ? "Observing demand and machine response" : snapshot.Reason;
    }

    private static string ShortActor(string? actor)
    {
        if (string.IsNullOrWhiteSpace(actor)) return string.Empty;
        try { return System.IO.Path.GetFileNameWithoutExtension(actor); }
        catch { return actor; }
    }    private Task OpenSectionAsync(string section)
    {
        SelectSection(section);
        return TransitionToAsync(PowerFlowShellState.Expanded, ShellActivationMode.PinnedActive, animate: true);
    }

    private async void OnOpenRulesClicked(object sender, RoutedEventArgs e) => await OpenSectionAsync("rules");
    private async void OnOpenSettingsClicked(object sender, RoutedEventArgs e) => await OpenSectionAsync("settings");
    private async void OnHeaderRulesRequested(object? sender, EventArgs e) => await OpenSectionAsync("rules");
    private async void OnHeaderSettingsRequested(object? sender, EventArgs e) => await OpenSectionAsync("settings");
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
        var cockpitSection = tag is "flow" or "model" or "tune";
        CockpitSurface.Visibility = cockpitSection ? Visibility.Visible : Visibility.Collapsed;
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
