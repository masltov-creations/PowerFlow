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
using PowerFlow.Core.Profiling;
using PowerFlow.Core.Rules;
using PowerFlow.Windows.Activity;
using PowerFlow.Windows.Power;
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
    private PowerModeSelection _manualModeSelection = PowerModeSelection.Auto;
    private ShellDensity _layoutDensity = ShellDensity.Compact;
    private ShellActivationMode _activationMode = ShellActivationMode.PinnedActive;
    private TrayRect? _lastTrayAnchor;
    private TrayRect? _lastWorkArea;
    private string _currentSection = "flow";
    private DispatcherQueueTimer? _presentationTimer;
    private TaskCompletionSource? _presentationCompletion;
    private long _transitionGeneration;
    private int _resizeModeSyncSuppressionDepth;
    private DispatcherQueueTimer? _resizeReflowTimer;
    private bool _resizeReflowScheduled;
    private bool _hasPendingResizeReflow;
    private int _pendingResizeWidth;
    private int _pendingResizeHeight;
    private bool _headerDragActive;
    private PointNative _headerDragPointerOrigin;
    private PointInt32 _headerDragWindowOrigin;
    private DispatcherQueueTimer? _headerDragTimer;
    private bool _suppressNavigationSelection;
    private readonly IProcessorPolicyController _processorPolicyController = new WindowsProcessorPolicyController();
    private ProcessorPolicySnapshot? _processorPolicySnapshot;
    private DateTimeOffset _nextProcessorPolicyReadAt;
    private readonly Func<GraduatedCoreActuatorStatus?>? _coreActuatorStatusProvider;
    private readonly Func<PowerModeSelection, Task>? _applyOperatingMode;
    private readonly MachineBaselineSessionRuntime? _machineBaselineSession;
    private readonly Func<PowerFlowOperatingMode?>? _currentProfileProvider;

    public PowerFlowShellState ShellState => _shellState;
    public ShellActivationMode ActivationMode => _activationMode;
    public bool IsShellVisible => _shellVisible;
    public DashboardViewModel ViewModel { get; } = new();

    public MainWindow(PowerFlowController controller, TelemetryContinuityRecorder recorder, PowerFlowConfig config, Func<PowerFlowConfig, Task> applyConfig, bool previewMode = false, Func<GraduatedCoreActuatorStatus?>? coreActuatorStatusProvider = null, Func<PowerModeSelection, Task>? applyOperatingMode = null, MachineBaselineSessionRuntime? machineBaselineSession = null, Func<PowerFlowOperatingMode?>? currentProfileProvider = null)
    {
        InitializeComponent();
        Title = previewMode ? "PowerFlow - Preview" : "PowerFlow";
        SystemHeaderHost.IsPreviewMode = previewMode;
        _controller = controller;
        _recorder = recorder;
        _previewMode = previewMode;
        _coreActuatorStatusProvider = coreActuatorStatusProvider;
        _applyOperatingMode = applyOperatingMode;
        _machineBaselineSession = machineBaselineSession;
        _currentProfileProvider = currentProfileProvider;
        _config = config;
        _applyConfig = applyConfig;
        _dispatcher = DispatcherQueue.GetForCurrentThread();
        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        try
        {
            var iconPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "PowerFlow.ico");
            if (System.IO.File.Exists(iconPath)) AppWindow.SetIcon(iconPath);
        }
        catch { }
        ApplyTheme(config.Theme);
        ApplyInspectionMotionPreference();
        ViewModel.Configure(config);
        ShellRoot.DataContext = ViewModel;
        ViewModel.UpdateContinuity(controller.Snapshot, recorder.History, recorder.LatestRichTelemetry);
        ApplyVisualState(controller.Snapshot);
        RulesPanel.Initialize(config, ApplyConfigFromPageAsync, BrowseExecutableAsync);
        SettingsPanel.Initialize(config, ApplyConfigFromPageAsync, ApplyTheme);
        CpuProfilePanel.Initialize(config.EffectiveCpuCapabilityProfiles, SaveCpuCapabilityProfileAsync, config.EffectiveMachineBaselineRuns, _machineBaselineSession);
        SystemHeaderHost.ModeRequested += OnModeRequested;
        SystemHeaderHost.DragStarted += OnHeaderDragStarted;
        SystemHeaderHost.DragCompleted += OnHeaderDragCompleted;
        controller.SnapshotChanged += OnSnapshotChanged;
        _recorder.ContinuityChanged += OnContinuityChanged;
        AppWindow.Closing += OnAppWindowClosing;
        AppWindow.Changed += OnAppWindowChanged;
        Closed += OnClosed;
        var initialSize = ShellCoordinateProjection.ToPhysicalSize(320, 176, CurrentRasterizationScale());
        AppWindow.Resize(new SizeInt32(initialSize.Width, initialSize.Height));
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
        _visibilityLease ??= _recorder.AcquireVisibility();
        ViewModel.UpdateContinuity(_controller.Snapshot, _recorder.History, _recorder.LatestRichTelemetry);
        ApplyVisualState(_controller.Snapshot);
        SelectSection(_currentSection);
        await TransitionToAsync(state, activation, animate);
        ApplySectionVisibility(_currentSection);
    }

    public async Task TransitionToAsync(PowerFlowShellState state, ShellActivationMode activation, bool animate)
    {
        var generation = BeginShellTransition();
        if (state == PowerFlowShellState.Hidden)
        {
            await HideShellCoreAsync(generation);
            return;
        }

        if (state == PowerFlowShellState.Compact) _layoutDensity = ShellDensity.Compact;
        else if (state is PowerFlowShellState.Expanded or PowerFlowShellState.FullScreen) _layoutDensity = ShellDensity.Expanded;

        var fromState = _shellVisible ? _shellState : PowerFlowShellState.Hidden;
        var leavingFullScreen = IsFullScreenPresenter() && state != PowerFlowShellState.FullScreen;
        var preservedFullScreenBounds = leavingFullScreen ? CurrentBounds() : default;

        if (leavingFullScreen)
        {
            BeginResizeModeSyncSuppression();
            try
            {
                AppWindow.SetPresenter(AppWindowPresenterKind.Overlapped);
                AppWindow.MoveAndResize(preservedFullScreenBounds);
                if (ShellPresenterTransitionPolicy.RequiresLayoutBarrier(fromState, state))
                {
                    await WaitForPresenterLayoutAsync();
                    if (!IsCurrentTransition(generation)) return;
                    AppWindow.MoveAndResize(preservedFullScreenBounds);
                    await WaitForPresenterLayoutAsync();
                    if (!IsCurrentTransition(generation)) return;
                }
            }
            finally
            {
                EndResizeModeSyncSuppression();
            }
        }

        if (!IsCurrentTransition(generation)) return;
        _shellState = state;
        _activationMode = activation;
        ApplyActivationMode(activation, state);
        var target = ResolveTargetBounds(state);
        var start = leavingFullScreen ? preservedFullScreenBounds : CurrentBounds();
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

        await AnimateShellBoundsAsync(start, target, fromState, state, animate, generation);
        if (!IsCurrentTransition(generation)) return;

        if (state == PowerFlowShellState.FullScreen)
        {
            BeginResizeModeSyncSuppression();
            try
            {
                AppWindow.SetPresenter(AppWindowPresenterKind.FullScreen);
                await WaitForPresenterLayoutAsync();
                if (!IsCurrentTransition(generation)) return;
            }
            finally
            {
                EndResizeModeSyncSuppression();
            }
        }

        if (!IsCurrentTransition(generation)) return;
        var finalLogical = CurrentLogicalAppWindowSize();
        ApplyShellLayout(state, finalLogical.Width, finalLogical.Height);
        if (activation == ShellActivationMode.PinnedActive) Activate();
    }

    public Task HideShellAsync()
        => HideShellCoreAsync(BeginShellTransition());

    private async Task HideShellCoreAsync(long generation)
    {
        if (!IsCurrentTransition(generation)) return;
        if (!_shellVisible)
        {
            _shellState = PowerFlowShellState.Hidden;
            ReleaseDashboardVisibility();
            return;
        }

        var fromState = _shellState;
        var start = CurrentBounds();
        var target = ResolveTargetBounds(PowerFlowShellState.Hidden);
        await AnimateShellBoundsAsync(start, target, fromState, PowerFlowShellState.Hidden, animate: true, generation);
        if (!IsCurrentTransition(generation)) return;
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
        var next = _shellState == PowerFlowShellState.FullScreen
            ? PowerFlowShellState.Expanded
            : _layoutDensity == ShellDensity.Compact
                ? PowerFlowShellState.Expanded
                : PowerFlowShellState.FullScreen;
        await TransitionToAsync(next, ShellActivationMode.PinnedActive, animate: true);
    }

    private async void OnCompactClicked(object sender, RoutedEventArgs e)
        => await TransitionToAsync(PowerFlowShellState.Compact, ShellActivationMode.PinnedActive, animate: true);

    private void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if ((!args.DidSizeChange && !args.DidPresenterChange) || IsResizeModeSyncSuppressed || !_shellVisible) return;
        var logical = CurrentLogicalAppWindowSize();
        QueueResizeReflow(logical.Width, logical.Height);
    }

    private void QueueResizeReflow(int width, int height)
    {
        _pendingResizeWidth = Math.Max(1, width);
        _pendingResizeHeight = Math.Max(1, height);
        _hasPendingResizeReflow = true;
        if (_resizeReflowScheduled) return;

        _resizeReflowTimer ??= CreateResizeReflowTimer();
        _resizeReflowScheduled = true;
        _resizeReflowTimer.Start();
    }

    private DispatcherQueueTimer CreateResizeReflowTimer()
    {
        var timer = _dispatcher.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(16);
        timer.IsRepeating = false;
        timer.Tick += OnResizeReflowTimerTick;
        return timer;
    }

    private void OnResizeReflowTimerTick(DispatcherQueueTimer sender, object args)
    {
        _resizeReflowScheduled = false;
        if (_closed || !_shellVisible || IsResizeModeSyncSuppressed || !_hasPendingResizeReflow)
        {
            _hasPendingResizeReflow = false;
            return;
        }

        var width = _pendingResizeWidth;
        var height = _pendingResizeHeight;
        _hasPendingResizeReflow = false;
        ApplyResizeReflow(width, height);
    }

    private void ApplyResizeReflow(int width, int height)
    {
        var logical = new ShellLogicalSize(width, height);
        if (_shellState is not PowerFlowShellState.Glance and not PowerFlowShellState.FullScreen && !IsFullScreenPresenter())
            _layoutDensity = ShellResponsiveDensity.Resolve(logical, _layoutDensity);

        ApplyShellLayout(_shellState, width, height);
        var stableProfile = PowerFlowShellLayout.Resolve(width, height, _shellState, _currentSection, _layoutDensity);
        ResetSemanticMorphPresentation(stableProfile);
    }

    private void CancelPendingResizeReflow()
    {
        _resizeReflowScheduled = false;
        _hasPendingResizeReflow = false;
        _resizeReflowTimer?.Stop();
    }

    private void ApplyShellLayout(PowerFlowShellState state, int width, int height)
    {
        var profile = PowerFlowShellLayout.Resolve(width, height, state, _currentSection, _layoutDensity);

        SystemHeaderHost.Presentation = profile.Header;
        PerformanceTimeline.SetPresentation(profile.Timeline);
        ApplyNavigationPresentation(profile.Navigation, profile.Geometry.NavigationWidth);
        SystemHeaderHost.ShowBrand = profile.Navigation != NavigationPresentation.Rail;
        ApplyCockpitGeometry(profile, state, width, height);
        var glance = state == PowerFlowShellState.Glance;
        var expanded = state == PowerFlowShellState.FullScreen || profile.Navigation == NavigationPresentation.Rail;
        GlanceTapTarget.Visibility = glance && _activationMode == ShellActivationMode.PinnedActive ? Visibility.Visible : Visibility.Collapsed;
        PresentationActions.Visibility = glance ? Visibility.Collapsed : Visibility.Visible;
        CompactButton.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        CompactButton.Opacity = 1d;
        CompactButton.IsHitTestVisible = expanded;
        PresentationToggleButton.Content = state == PowerFlowShellState.FullScreen
            ? "RESTORE"
            : _layoutDensity == ShellDensity.Compact
                ? "EXPAND"
                : "FULL SCREEN";
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
        AdaptiveControlRegion.Visibility = profile.GovernorControls != GovernorControlPresentation.Summary ? Visibility.Visible : Visibility.Collapsed;
        SelectedActorPanel.Visibility = profile.GovernorControls == GovernorControlPresentation.Bias && width < 680 ? Visibility.Collapsed : Visibility.Visible;
        FooterRowDefinition.Height = state == PowerFlowShellState.FullScreen || _layoutDensity == ShellDensity.Expanded ? GridLength.Auto : new GridLength(0);
    }
    private RectInt32 ResolveTargetBounds(PowerFlowShellState state)
    {
        if (state == PowerFlowShellState.FullScreen)
        {
            try
            {
                var display = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest);
                return display.OuterBounds;
            }
            catch { }
        }
        var scale = CurrentRasterizationScale();
        if (_lastTrayAnchor is { } tray && _lastWorkArea is { } work)
            return ShellTransitionGeometry.TargetBounds(tray, work, CurrentBounds(), state, scale);

        var current = CurrentBounds();
        if (state == PowerFlowShellState.Hidden) return new RectInt32(current.X, current.Y, 1, 1);
        var logical = state switch
        {
            PowerFlowShellState.Glance => new ShellLogicalSize(320, 176),
            PowerFlowShellState.Compact => new ShellLogicalSize(760, 440),
            PowerFlowShellState.Expanded => new ShellLogicalSize(1280, 800),
            _ => ShellCoordinateProjection.ToLogicalSize(current.Width, current.Height, scale)
        };
        var physical = ShellCoordinateProjection.ToPhysicalSize(logical.Width, logical.Height, scale);
        return new RectInt32(current.X, current.Y, physical.Width, physical.Height);
    }

    private double CurrentRasterizationScale()
    {
        var scale = Content?.XamlRoot?.RasterizationScale ?? 1d;
        return double.IsFinite(scale) && scale > 0 ? scale : 1d;
    }

    private ShellLogicalSize CurrentLogicalAppWindowSize()
        => ShellCoordinateProjection.ToLogicalSize(AppWindow.Size.Width, AppWindow.Size.Height, CurrentRasterizationScale());

    private ShellLogicalSize LogicalSize(RectInt32 bounds)
        => ShellCoordinateProjection.ToLogicalSize(bounds.Width, bounds.Height, CurrentRasterizationScale());
    private RectInt32 CurrentBounds()
    {
        var p = AppWindow.Position;
        var s = AppWindow.Size;
        return new RectInt32(p.X, p.Y, Math.Max(1, s.Width), Math.Max(1, s.Height));
    }

    private async Task AnimateShellBoundsAsync(RectInt32 start, RectInt32 target, PowerFlowShellState fromState, PowerFlowShellState toState, bool animate, long generation)
    {
        if (!IsCurrentTransition(generation)) return;
        var reducedMotion = !animate || !ShouldAnimatePresentation();
        var travel = Math.Sqrt(Math.Pow(target.Width - start.Width, 2) + Math.Pow(target.Height - start.Height, 2));
        var duration = ShellMotionPolicy.DurationForTravel(fromState, toState, reducedMotion, travel);
        var fromLogical = LogicalSize(start);
        var toLogical = LogicalSize(target);
        var fromProfile = PowerFlowShellLayout.Resolve(fromLogical.Width, fromLogical.Height, fromState, _currentSection, fromState is PowerFlowShellState.Expanded or PowerFlowShellState.FullScreen ? ShellDensity.Expanded : ShellDensity.Compact);
        var toProfile = PowerFlowShellLayout.Resolve(toLogical.Width, toLogical.Height, toState, _currentSection, toState is PowerFlowShellState.Expanded or PowerFlowShellState.FullScreen ? ShellDensity.Expanded : ShellDensity.Compact);

        if (duration == TimeSpan.Zero || start.Equals(target))
        {
            BeginResizeModeSyncSuppression();
            try
            {
                AppWindow.MoveAndResize(target);
            }
            finally
            {
                EndResizeModeSyncSuppression();
            }
            if (!IsCurrentTransition(generation)) return;
            var targetLogical = LogicalSize(target);
            ApplyShellLayout(toState, targetLogical.Width, targetLogical.Height);
            ApplyShellTransitionFrame(fromProfile, toProfile, 1d, reducedMotion: true);
            ResetSemanticMorphPresentation(toProfile);
            return;
        }

        PrepareShellTransition(fromProfile, toProfile);
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var timer = _dispatcher.CreateTimer();
        _presentationTimer = timer;
        _presentationCompletion = tcs;
        timer.Interval = TimeSpan.FromMilliseconds(16);
        BeginResizeModeSyncSuppression();
        timer.Tick += Tick;
        timer.Start();
        try
        {
            await tcs.Task;
        }
        finally
        {
            timer.Stop();
            timer.Tick -= Tick;
            stopwatch.Stop();
            if (ReferenceEquals(_presentationTimer, timer)) _presentationTimer = null;
            if (ReferenceEquals(_presentationCompletion, tcs)) _presentationCompletion = null;
            EndResizeModeSyncSuppression();
        }

        if (!IsCurrentTransition(generation)) return;
        AppWindow.MoveAndResize(target);
        ApplyShellTransitionFrame(fromProfile, toProfile, 1d, reducedMotion: false);
        var finalLogical = LogicalSize(target);
        ApplyShellLayout(toState, finalLogical.Width, finalLogical.Height);
        ResetSemanticMorphPresentation(toProfile);

        void Tick(DispatcherQueueTimer sender, object args)
        {
            if (!IsCurrentTransition(generation))
            {
                sender.Stop();
                tcs.TrySetResult();
                return;
            }

            var t = Math.Clamp(stopwatch.Elapsed.TotalMilliseconds / duration.TotalMilliseconds, 0d, 1d);
            var eased = ShellMotionPolicy.Ease(fromState, toState, t);
            var rect = ShellTransitionGeometry.Interpolate(start, target, eased);
            AppWindow.MoveAndResize(rect);
            ApplyShellGeometryMorph(fromProfile, toProfile, eased);
            ApplyShellTransitionFrame(fromProfile, toProfile, eased, reducedMotion: false);
            if (t < 1d) return;
            sender.Stop();
            tcs.TrySetResult();
        }
    }
    private void PrepareShellTransition(ShellPresentationProfile from, ShellPresentationProfile to)
    {
        if (to.Navigation == NavigationPresentation.Rail)
            ApplyNavigationPresentation(NavigationPresentation.Rail, to.Geometry.NavigationWidth);
        else if (from.Navigation == NavigationPresentation.Rail)
            ApplyNavigationPresentation(NavigationPresentation.Rail, from.Geometry.NavigationWidth);

        if (from.GovernorControls != GovernorControlPresentation.Summary || to.GovernorControls != GovernorControlPresentation.Summary)
            AdaptiveControlRegion.Visibility = Visibility.Visible;
        if (ShowsFooter(from) || ShowsFooter(to)) FooterRowDefinition.Height = GridLength.Auto;
    }

    private void ApplyShellGeometryMorph(ShellPresentationProfile from, ShellPresentationProfile to, double progress)
    {
        var t = Math.Clamp(progress, 0d, 1d);
        var geometry = ShellTransitionGeometry.InterpolateGeometry(from.Geometry, to.Geometry, t);
        CockpitSurface.Padding = new Thickness(geometry.ContentPadding);
        CockpitSurface.RowSpacing = geometry.Gap;
        SystemHeaderRow.ColumnSpacing = geometry.Gap;
        AdaptiveControlRegion.ColumnSpacing = geometry.Gap;
        SystemHeaderRowDefinition.Height = new GridLength(Math.Max(0, geometry.HeaderHeight));
        AdaptiveControlRowDefinition.Height = new GridLength(Math.Max(0, Lerp(ControlBandHeight(from), ControlBandHeight(to), t)));

        var fromControls = from.GovernorControls != GovernorControlPresentation.Summary;
        var toControls = to.GovernorControls != GovernorControlPresentation.Summary;
        AdaptiveControlRegion.Opacity = fromControls == toControls ? 1d : toControls ? t : 1d - t;

        var fromFooter = ShowsFooter(from);
        var toFooter = ShowsFooter(to);
        StatusFooter.Opacity = fromFooter == toFooter ? 1d : toFooter ? t : 1d - t;

        var paneWidth = Math.Max(0d, geometry.NavigationWidth);
        if (from.Navigation == NavigationPresentation.Rail || to.Navigation == NavigationPresentation.Rail)
            NavigationRail.OpenPaneLength = paneWidth;

        if (from.Navigation != to.Navigation)
        {
            var shift = to.Navigation == NavigationPresentation.Rail
                ? -paneWidth * (1d - t)
                : -paneWidth * t;
            SetLayoutTranslation(SectionHost, shift, 0);
        }
        else SetLayoutTranslation(SectionHost, 0, 0);
    }

    private static double ControlBandHeight(ShellPresentationProfile profile) => profile.GovernorControls switch
    {
        GovernorControlPresentation.Summary => 0,
        GovernorControlPresentation.Bias => 82,
        GovernorControlPresentation.Contextual => Math.Max(108, profile.Geometry.ControlBandHeight),
        _ => Math.Max(132, profile.Geometry.ControlBandHeight + 18)
    };

    private static bool ShowsFooter(ShellPresentationProfile profile)
        => profile.State == PowerFlowShellState.FullScreen || profile.Navigation == NavigationPresentation.Rail;

    private static double Lerp(double from, double to, double progress) => from + (to - from) * progress;
    private static void SetLayoutTranslation(UIElement element, double x, double y)
    {
        if (element.RenderTransform is not TranslateTransform transform)
        {
            transform = new TranslateTransform();
            element.RenderTransform = transform;
        }
        transform.X = x;
        transform.Y = y;
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
        SystemHeaderHost.ApplyMorph(from.Header, to.Header, t, reducedMotion: false);
        PerformanceTimeline.ApplyMorph(from.Timeline, to.Timeline, t, reducedMotion: false);
        ApplyNavigationMorph(from.Navigation, to.Navigation, t);
        ApplyPresentationActionsMorph(from.State, to.State, t);
    }

    private void ApplyNavigationMorph(NavigationPresentation from, NavigationPresentation to, double progress)
    {
        NavigationRail.Opacity = 1d;
        SetLayoutTranslation(NavigationRail, 0, 0);
    }
    private void ApplyPresentationActionsMorph(PowerFlowShellState from, PowerFlowShellState to, double targetProgress)
    {
        var fromVisible = from != PowerFlowShellState.Glance && from != PowerFlowShellState.Hidden;
        var toVisible = to != PowerFlowShellState.Glance && to != PowerFlowShellState.Hidden;
        if (fromVisible == toVisible)
        {
            PresentationActions.Opacity = 1d;
            SetLayoutTranslation(PresentationActions, 0, 0);
            return;
        }
        var p = Math.Clamp(targetProgress, 0d, 1d);
        var value = toVisible ? p : 1d - p;
        PresentationActions.Opacity = value;
        SetLayoutTranslation(PresentationActions, 0, -4d * (1d - value));
    }

    private void ResetSemanticMorphPresentation(ShellPresentationProfile target)
    {
        SystemHeaderHost.ApplyMorph(target.Header, target.Header, 1d, reducedMotion: true);
        PerformanceTimeline.SetPresentation(target.Timeline);
        foreach (var element in new UIElement[] { PresentationActions, NavigationRail, StatusFooter, AdaptiveControlRegion, SectionHost })
        {
            element.Opacity = 1d;
            SetLayoutTranslation(element, 0, 0);
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

    private long BeginShellTransition()
    {
        var generation = ++_transitionGeneration;
        CancelPresentationAnimation();
        CancelPendingResizeReflow();
        return generation;
    }

    private bool IsCurrentTransition(long generation)
        => !_closed && generation == _transitionGeneration;

    private void CancelPresentationAnimation()
    {
        var timer = _presentationTimer;
        _presentationTimer = null;
        timer?.Stop();
        var completion = _presentationCompletion;
        _presentationCompletion = null;
        completion?.TrySetResult();
    }

    private bool IsResizeModeSyncSuppressed => _resizeModeSyncSuppressionDepth > 0;
    private void BeginResizeModeSyncSuppression() => _resizeModeSyncSuppressionDepth++;
    private void EndResizeModeSyncSuppression() => _resizeModeSyncSuppressionDepth = Math.Max(0, _resizeModeSyncSuppressionDepth - 1);
    private async Task WaitForPresenterLayoutAsync()
    {
        await DispatcherTurnAsync();
        await DispatcherTurnAsync();
    }

    private Task DispatcherTurnAsync()
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_dispatcher.TryEnqueue(DispatcherQueuePriority.Low, () => tcs.TrySetResult())) tcs.TrySetResult();
        return tcs.Task;
    }
    private bool IsFullScreenPresenter() => AppWindow.Presenter.Kind == AppWindowPresenterKind.FullScreen;

    private bool ShouldAnimatePresentation()
    {
        if (_config.ReducedMotionOverride is bool reduced) return !reduced;
        try { return new UISettings().AnimationsEnabled; }
        catch { return true; }
    }

    private void ApplyInspectionMotionPreference()
    {
        PerformanceTimeline.SetReducedMotion(!ShouldAnimatePresentation());
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
        var learningModel = _config.EffectiveAdaptiveGovernorSettings.ResolveLearningModel(calibration);
        var latestObservation = ViewModel.OperatingHistory.LastOrDefault();
        var modelEntitlement = ResolveModelEntitlement(latestObservation, snapshot);
        PerformanceTimeline.Apply(ViewModel.OperatingHistory, learningModel.Envelope, _graphWindowSeconds);
        PerformanceTimeline.SetCoreThreadHistory(_recorder.History);
        PerformanceTimeline.SetProcessorPolicySnapshot(ReadProcessorPolicySnapshot());
        PerformanceTimeline.SetCoreActuatorStatus(_coreActuatorStatusProvider?.Invoke());
        PerformanceTimeline.SetPolicyContext(learningModel.Envelope, modelEntitlement, EnvelopeTuning.Learned);
        PerformanceTimeline.SetTuneMode(false);
        ModelConfidenceText.Text = learningModel.Confidence == EnvelopeConfidence.Low
            ? "LEARNING"
            : $"{learningModel.Confidence.ToString().ToUpperInvariant()} CONFIDENCE";
        EnvelopeSummaryText.Text = learningModel.Envelope.EfficientPowerFrontierWatts is double frontier
            ? $"{ViewModel.GovernorModelLabel} · frontier near {frontier:0} W · {ViewModel.GovernorModelExplanation}"
            : $"{ViewModel.GovernorModelLabel} · {ViewModel.GovernorModelExplanation}";

        var actor = ShortActor(snapshot.TriggerApplication);
        SelectedActorNameText.Text = string.IsNullOrWhiteSpace(actor) ? "SYSTEM / NO DOMINANT ACTOR" : actor;
        var manualAuthority = snapshot.IsLatched && string.Equals(snapshot.LatchType, "Manual", StringComparison.OrdinalIgnoreCase);
        var currentProfile = _currentProfileProvider?.Invoke();
        if (snapshot.IsLatched && !manualAuthority)
            DecisionStateText.Text = (snapshot.LatchType ?? "LOCK").ToUpperInvariant();
        else if (currentProfile is PowerFlowOperatingMode profile)
            DecisionStateText.Text = $"{(manualAuthority ? "MANUAL" : "AUTO")} / {AppliedProfileLabel(profile)}";
        else
            DecisionStateText.Text = manualAuthority ? "MANUAL" : "AUTO / OBSERVING";
        DecisionExplanationText.Text = string.IsNullOrWhiteSpace(snapshot.Reason) ? "Observing demand and machine response" : snapshot.Reason;
        var selectedMode = manualAuthority
            ? _manualModeSelection != PowerModeSelection.Auto
                ? _manualModeSelection
                : snapshot.State switch
                {
                    PowerState.PowerSaver => PowerModeSelection.Eco,
                    PowerState.HighPerformance => PowerModeSelection.Boost,
                    _ => PowerModeSelection.Efficient
                }
            : PowerModeSelection.Auto;
        var activationError = snapshot.Reason.StartsWith("Power plan activation failed:", StringComparison.OrdinalIgnoreCase) ? snapshot.Reason : null;
        SystemHeaderHost.SetModeSelection(selectedMode, manualAuthority, activationError);
    }

    private static string AppliedProfileLabel(PowerFlowOperatingMode mode) => mode switch
    {
        PowerFlowOperatingMode.Saver => "SAVER",
        PowerFlowOperatingMode.Balanced => "BAL-E",
        PowerFlowOperatingMode.BalancedPerformance => "BAL-P",
        PowerFlowOperatingMode.Performance => "PERF",
        PowerFlowOperatingMode.Ultra => "ULTRA",
        _ => mode.ToString().ToUpperInvariant()
    };
    private async void OnModeRequested(object? sender, PowerModeRequestedEventArgs e)
    {
        try
        {
            if (_applyOperatingMode is not null)
            {
                await _applyOperatingMode(e.Mode);
                _manualModeSelection = e.Mode;
            }
            else if (e.Mode == PowerModeSelection.Auto)
            {
                _manualModeSelection = PowerModeSelection.Auto;
                await _controller.ReleaseManualLatchAsync();
            }
            else
            {
                _manualModeSelection = e.Mode;
                var state = e.Mode switch
                {
                    PowerModeSelection.Eco => PowerState.PowerSaver,
                    PowerModeSelection.Efficient or PowerModeSelection.Responsive => PowerState.Balanced,
                    PowerModeSelection.Boost => PowerState.Balanced,
                    PowerModeSelection.Ultra => PowerState.HighPerformance,
                    _ => PowerState.Balanced
                };
                await _controller.SetManualStateAsync(state);
            }
            ApplyVisualState(_controller.Snapshot);
        }
        catch (Exception ex)
        {
            SystemHeaderHost.SetModeSelection(_manualModeSelection, _manualModeSelection != PowerModeSelection.Auto, ex.Message);
        }
    }

    private ProcessorPolicySnapshot? ReadProcessorPolicySnapshot()
    {
        var now = DateTimeOffset.UtcNow;
        if (_processorPolicySnapshot is not null && now < _nextProcessorPolicyReadAt) return _processorPolicySnapshot;
        try
        {
            _processorPolicySnapshot = _processorPolicyController.CaptureActive();
            _nextProcessorPolicyReadAt = now.AddSeconds(5);
        }
        catch
        {
            _nextProcessorPolicyReadAt = now.AddSeconds(5);
        }
        return _processorPolicySnapshot;
    }


    private PerformanceEntitlement ResolveModelEntitlement(OperatingObservation? latest, ControllerSnapshot snapshot)
    {
        var actor = latest?.Actor ?? snapshot.TriggerApplication;
        if (string.IsNullOrWhiteSpace(actor)) return PerformanceEntitlement.LegacyPerformance;
        var rule = _config.AppRules.FirstOrDefault(candidate => ActorMatches(candidate.ExecutablePath, actor));
        return rule?.EffectiveEntitlement ?? PerformanceEntitlement.LegacyPerformance;
    }


    private static bool ActorMatches(string executablePath, string actor)
    {
        if (string.Equals(executablePath, actor, StringComparison.OrdinalIgnoreCase)) return true;
        try { return string.Equals(System.IO.Path.GetFileName(executablePath), System.IO.Path.GetFileName(actor), StringComparison.OrdinalIgnoreCase); }
        catch { return false; }
    }

    private static string ShortActor(string? actor)
    {
        if (string.IsNullOrWhiteSpace(actor)) return string.Empty;
        try { return System.IO.Path.GetFileNameWithoutExtension(actor); }
        catch { return actor; }
    }    private async Task OpenSectionAsync(string section)
    {
        await NavigateToSectionAsync(section);
        SelectSection(section);
    }

    private void OnHeaderDragStarted(object? sender, EventArgs e)
    {
        _headerDragActive = false;
        if (!_shellVisible || _activationMode != ShellActivationMode.PinnedActive) return;
        if (_shellState is PowerFlowShellState.Hidden or PowerFlowShellState.Glance or PowerFlowShellState.FullScreen) return;
        if (!GetCursorPos(out _headerDragPointerOrigin)) return;
        _headerDragWindowOrigin = AppWindow.Position;
        _headerDragActive = true;
        EnsureHeaderDragTimer();
        _headerDragTimer!.Start();
    }

    private void EnsureHeaderDragTimer()
    {
        if (_headerDragTimer is not null) return;
        _headerDragTimer = _dispatcher.CreateTimer();
        _headerDragTimer.Interval = TimeSpan.FromMilliseconds(16);
        _headerDragTimer.Tick += OnHeaderDragTimerTick;
    }

    private void OnHeaderDragTimerTick(DispatcherQueueTimer sender, object args)
    {
        if (!_headerDragActive || !GetCursorPos(out var pointer)) return;
        var deltaX = pointer.X - _headerDragPointerOrigin.X;
        var deltaY = pointer.Y - _headerDragPointerOrigin.Y;
        if (Math.Abs(deltaX) < 3 && Math.Abs(deltaY) < 3) return;
        AppWindow.Move(new PointInt32(
            _headerDragWindowOrigin.X + deltaX,
            _headerDragWindowOrigin.Y + deltaY));
    }

    private void OnHeaderDragCompleted(object? sender, EventArgs e)
    {
        _headerDragActive = false;
        _headerDragTimer?.Stop();
    }
    private async void OnOpenRulesClicked(object sender, RoutedEventArgs e) => await OpenSectionAsync("rules");
    private async void OnOpenSettingsClicked(object sender, RoutedEventArgs e) => await OpenSectionAsync("settings");
    private async void OnHeaderRulesRequested(object? sender, EventArgs e) => await OpenSectionAsync("rules");
    private async void OnHeaderSettingsRequested(object? sender, EventArgs e) => await OpenSectionAsync("settings");
    private async Task SaveCpuCapabilityProfileAsync(CpuCapabilityProfile profile)
    {
        var profiles = _config.EffectiveCpuCapabilityProfiles
            .Where(existing => !string.Equals(existing.PolicySignature, profile.PolicySignature, StringComparison.OrdinalIgnoreCase))
            .Append(profile)
            .OrderByDescending(existing => existing.CapturedAt)
            .ToArray();
        await ApplyConfigFromPageAsync(_config with { CpuCapabilityProfiles = profiles });
    }

    private async Task ApplyConfigFromPageAsync(PowerFlowConfig config)
    {
        await _applyConfig(config);
        _config = config;
        ApplyTheme(config.Theme);
        ApplyInspectionMotionPreference();
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
        if (_suppressNavigationSelection) return;
        var requested = (args.SelectedItemContainer?.Tag as string) ?? "flow";
        var tag = requested is "flow" or "rules" or "profile" or "settings" ? requested : "flow";
        await NavigateToSectionAsync(tag);
    }

    private Task NavigateToSectionAsync(string tag)
    {
        _currentSection = tag;
        PerformanceTimeline.SetTuneMode(false);
        ApplySectionVisibility(tag);
        var logical = CurrentLogicalAppWindowSize();
        ApplyShellLayout(_shellState, logical.Width, logical.Height);
        return Task.CompletedTask;
    }

    private void ApplySectionVisibility(string tag)
    {
        CockpitSurface.Visibility = tag == "flow" ? Visibility.Visible : Visibility.Collapsed;
        CpuProfilePanel.Visibility = tag == "profile" ? Visibility.Visible : Visibility.Collapsed;
        RulesPanel.Visibility = tag == "rules" ? Visibility.Visible : Visibility.Collapsed;
        SettingsPanel.Visibility = tag == "settings" ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SelectSection(string tag)
    {
        _suppressNavigationSelection = true;
        try
        {
            foreach (var item in NavigationRail.MenuItems.OfType<NavigationViewItem>().Concat(NavigationRail.FooterMenuItems.OfType<NavigationViewItem>()))
            {
                if (!string.Equals(item.Tag as string, tag, StringComparison.OrdinalIgnoreCase)) continue;
                NavigationRail.SelectedItem = item;
                return;
            }
        }
        finally { _suppressNavigationSelection = false; }
    }
    private void OnClosed(object sender, WindowEventArgs args)
    {
        CpuProfilePanel.Detach();
        if (_closed) return;
        _closed = true;
        _transitionGeneration++;
        CancelPresentationAnimation();
        CancelPendingResizeReflow();
        AppWindow.Closing -= OnAppWindowClosing;
        AppWindow.Changed -= OnAppWindowChanged;
        _controller.SnapshotChanged -= OnSnapshotChanged;
        _recorder.ContinuityChanged -= OnContinuityChanged;
        SystemHeaderHost.ModeRequested -= OnModeRequested;
        SystemHeaderHost.DragStarted -= OnHeaderDragStarted;
        SystemHeaderHost.DragCompleted -= OnHeaderDragCompleted;
        if (_headerDragTimer is not null)
        {
            _headerDragTimer.Stop();
            _headerDragTimer.Tick -= OnHeaderDragTimerTick;
            _headerDragTimer = null;
        }
        if (_resizeReflowTimer is not null)
        {
            _resizeReflowTimer.Stop();
            _resizeReflowTimer.Tick -= OnResizeReflowTimerTick;
            _resizeReflowTimer = null;
        }
        CpuProfilePanel.CancelActive();
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
