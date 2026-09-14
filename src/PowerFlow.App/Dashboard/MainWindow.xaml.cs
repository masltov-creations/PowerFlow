using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
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
    private readonly ShellMotionCoordinator _motionCoordinator = new();
    private TaskCompletionSource? _motionCompletion;
    private ShellPresentationProfile _motionFromProfile = null!;
    private ShellPresentationProfile _motionToProfile = null!;
    private PowerFlowShellState _motionToState;
    private DispatcherQueueTimer? _motionFrameTimer;
    private bool _motionClockActive;
    private bool _motionResizeSuppressed;
    private long _motionGeneration;
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
    private bool _hoverSurfaceDragArmed;
    private uint _hoverSurfacePointerId;
    private bool _suppressNavigationSelection;
    private readonly IProcessorPolicyController _processorPolicyController = new WindowsProcessorPolicyController();
    private ProcessorPolicySnapshot? _processorPolicySnapshot;
    private DateTimeOffset _nextProcessorPolicyReadAt;
    private readonly Func<GraduatedCoreActuatorStatus?>? _coreActuatorStatusProvider;
    private readonly Func<PowerModeSelection, Task>? _applyOperatingMode;
    private readonly MachineBaselineSessionRuntime? _machineBaselineSession;
    private readonly Func<PowerFlowOperatingMode?>? _currentProfileProvider;
    private readonly Func<TensionShadowEvaluation?>? _tensionShadowProvider;
    private int _tensionChangeVersion;

    public PowerFlowShellState ShellState => _shellState;
    public ShellActivationMode ActivationMode => _activationMode;
    public bool IsShellVisible => _shellVisible;
    public DashboardViewModel ViewModel { get; } = new();

    public MainWindow(PowerFlowController controller, TelemetryContinuityRecorder recorder, PowerFlowConfig config, Func<PowerFlowConfig, Task> applyConfig, bool previewMode = false, Func<GraduatedCoreActuatorStatus?>? coreActuatorStatusProvider = null, Func<PowerModeSelection, Task>? applyOperatingMode = null, MachineBaselineSessionRuntime? machineBaselineSession = null, Func<PowerFlowOperatingMode?>? currentProfileProvider = null, Func<TensionShadowEvaluation?>? tensionShadowProvider = null)
    {
        InitializeComponent();
        ConfigureCustomTitleBar();
        Title = previewMode ? "PowerFlow - Preview" : "PowerFlow";
        SystemHeaderHost.IsPreviewMode = previewMode;
        _controller = controller;
        _recorder = recorder;
        _previewMode = previewMode;
        _coreActuatorStatusProvider = coreActuatorStatusProvider;
        _applyOperatingMode = applyOperatingMode;
        _machineBaselineSession = machineBaselineSession;
        _currentProfileProvider = currentProfileProvider;
        _tensionShadowProvider = tensionShadowProvider;
        _config = config;
        _applyConfig = applyConfig;
        GovernorTensionSlider.Value = config.EffectiveGovernorTensionPercent;
        GovernorTensionSlider.ValueChanged += OnGovernorTensionChanged;
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
        SystemHeaderHost.LiveRequested += OnHeaderLiveRequested;
        SystemHeaderHost.RulesRequested += OnHeaderRulesRequested;
        SystemHeaderHost.BaselineRequested += OnHeaderBaselineRequested;
        SystemHeaderHost.SettingsRequested += OnHeaderSettingsRequested;
        controller.SnapshotChanged += OnSnapshotChanged;
        _recorder.ContinuityChanged += OnContinuityChanged;
        AppWindow.Closing += OnAppWindowClosing;
        AppWindow.Changed += OnAppWindowChanged;
        Closed += OnClosed;
        var initialSize = ShellCoordinateProjection.ToPhysicalSize(ShellResizeStateProjection.MinimumWidth, ShellResizeStateProjection.MinimumHeight, CurrentRasterizationScale());
        AppWindow.Resize(new SizeInt32(initialSize.Width, initialSize.Height));
        ApplyShellLayout(PowerFlowShellState.Hidden, ShellResizeStateProjection.MinimumWidth, ShellResizeStateProjection.MinimumHeight);
        try { SystemBackdrop = new MicaBackdrop(); } catch { }
        SelectSection("flow");
    }

    private void ConfigureCustomTitleBar()
    {
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(DragSurface);
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
        else if (state is PowerFlowShellState.Expanded or PowerFlowShellState.Workspace or PowerFlowShellState.FullScreen) _layoutDensity = ShellDensity.Expanded;

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
            ApplyShellLayout(fromState, start.Width, start.Height);
            if (activation == ShellActivationMode.TransientNoActivate) ShowWindow(_hwnd, SwShowNoActivate);
            else Activate();
            _shellVisible = true;
            await WaitForPresenterLayoutAsync();
            if (!IsCurrentTransition(generation)) return;
            start = CurrentBounds();
            if (_lastTrayAnchor is { } seedTray && _lastWorkArea is { } seedWork)
            {
                start = ShellTransitionGeometry.TraySeedBounds(seedTray, seedWork, start.Width, start.Height);
                AppWindow.MoveAndResize(start);
            }
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
        var next = _shellState switch
        {
            PowerFlowShellState.FullScreen => PowerFlowShellState.Workspace,
            PowerFlowShellState.Workspace => PowerFlowShellState.FullScreen,
            PowerFlowShellState.Expanded => PowerFlowShellState.Workspace,
            _ => PowerFlowShellState.Expanded
        };
        await TransitionToAsync(next, ShellActivationMode.PinnedActive, animate: true);
    }
    private async void OnHoverViewClicked(object sender, RoutedEventArgs e)
        => await TransitionToAsync(PowerFlowShellState.Glance, ShellActivationMode.PinnedActive, animate: true);

    private async void OnCompactViewClicked(object sender, RoutedEventArgs e)
        => await TransitionToAsync(PowerFlowShellState.Compact, ShellActivationMode.PinnedActive, animate: true);

    private async void OnExpandedViewClicked(object sender, RoutedEventArgs e)
        => await TransitionToAsync(PowerFlowShellState.Expanded, ShellActivationMode.PinnedActive, animate: true);
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
        var requested = new ShellLogicalSize(width, height);
        var logical = ShellResizeStateProjection.ClampMinimum(requested);
        if (logical != requested)
        {
            var physical = ShellCoordinateProjection.ToPhysicalSize(logical.Width, logical.Height, CurrentRasterizationScale());
            BeginResizeModeSyncSuppression();
            try { AppWindow.Resize(new SizeInt32(physical.Width, physical.Height)); }
            finally { EndResizeModeSyncSuppression(); }
        }

        if (!IsFullScreenPresenter())
        {
            var resolvedState = ShellResizeStateProjection.Resolve(logical, _shellState);
            if (resolvedState != _shellState)
            {
                _shellState = resolvedState;
                _layoutDensity = resolvedState == PowerFlowShellState.Expanded ? ShellDensity.Expanded : ShellDensity.Compact;
            }
            else if (_shellState is not PowerFlowShellState.Glance and not PowerFlowShellState.FullScreen)
            {
                _layoutDensity = ShellResponsiveDensity.Resolve(logical, _layoutDensity);
            }
        }

        ApplyShellLayout(_shellState, logical.Width, logical.Height);
        var stableProfile = PowerFlowShellLayout.Resolve(logical.Width, logical.Height, _shellState, _currentSection, _layoutDensity);
        ResetSemanticMorphPresentation(stableProfile);
        ApplyDisclosureProgress(ShellDisclosurePolicy.Progress(logical, _shellState), settled: true);
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
        var expanded = state is PowerFlowShellState.Workspace or PowerFlowShellState.FullScreen || profile.Navigation == NavigationPresentation.Rail;
        GlanceTapTarget.Visibility = glance && _activationMode == ShellActivationMode.PinnedActive ? Visibility.Visible : Visibility.Collapsed;
        var interactiveGlance = glance && _activationMode == ShellActivationMode.PinnedActive;
        PresentationActions.Visibility = glance && !interactiveGlance ? Visibility.Collapsed : Visibility.Visible;
        ViewModeLabel.Text = state switch
        {
            PowerFlowShellState.Glance => "HOVER",
            PowerFlowShellState.Compact => "COMPACT",
            _ => "EXPANDED"
        };
        PresentationToggleButton.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        PresentationToggleButton.Content = state switch
        {
            PowerFlowShellState.FullScreen => "RESTORE",
            PowerFlowShellState.Workspace => "FULL SCREEN",
            _ => "WORKSPACE"
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
        var compactMoveStrip = state is PowerFlowShellState.Hidden or PowerFlowShellState.Glance;
        var headerTopInset = compactMoveStrip ? 10d : 16d;
        DragStripRowDefinition.Height = new GridLength(headerTopInset);
        DragSurface.Height = headerTopInset;
        DragHandle.Opacity = compactMoveStrip ? 0d : .32d;
        SystemHeaderHost.Margin = new Thickness(0);
        PresentationActions.Margin = new Thickness(0);
        CockpitSurface.Padding = new Thickness(profile.Geometry.ContentPadding);
        CockpitSurface.RowSpacing = profile.Geometry.Gap;
        HeaderContentRow.ColumnSpacing = profile.Geometry.Gap;
        SystemHeaderRow.Margin = new Thickness(profile.Geometry.ContentPadding, 0, profile.Geometry.ContentPadding, 0);
        AdaptiveControlRegion.ColumnSpacing = profile.Geometry.Gap;

        SystemHeaderRowDefinition.Height = profile.Header is HeaderPresentation.Minimal or HeaderPresentation.Compact
            ? new GridLength(1, GridUnitType.Auto)
            : new GridLength(Math.Max(0, profile.Geometry.HeaderHeight));
        TimelineRowDefinition.Height = new GridLength(1, GridUnitType.Star);
        TimelineRowDefinition.MinHeight = state == PowerFlowShellState.Glance ? 92d : 156d;
        AdaptiveControlRowDefinition.Height = profile.GovernorControls == GovernorControlPresentation.Bias
            ? new GridLength(1, GridUnitType.Auto)
            : new GridLength(ControlBandHeight(profile));
        AdaptiveControlRegion.Visibility = profile.GovernorControls != GovernorControlPresentation.Summary ? Visibility.Visible : Visibility.Collapsed;
        ApplyDisclosureProgress(ShellDisclosurePolicy.Progress(new ShellLogicalSize(width, height), state), settled: true);
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
        var current = CurrentBounds();
        if (_lastTrayAnchor is { } tray && _lastWorkArea is { } work
            && !(state == PowerFlowShellState.Glance && _activationMode == ShellActivationMode.PinnedActive && _shellVisible))
            return ShellTransitionGeometry.TargetBounds(tray, work, current, state, scale);

        if (state == PowerFlowShellState.Hidden) return new RectInt32(current.X, current.Y, 1, 1);
        var logical = state switch
        {
            PowerFlowShellState.Glance => new ShellLogicalSize(ShellResizeStateProjection.MinimumWidth, ShellResizeStateProjection.MinimumHeight),
            PowerFlowShellState.Compact => new ShellLogicalSize(760, 440),
            PowerFlowShellState.Expanded => new ShellLogicalSize(1280, 800),
            PowerFlowShellState.Workspace => new ShellLogicalSize(1360, 860),
            _ => ShellCoordinateProjection.ToLogicalSize(current.Width, current.Height, scale)
        };
        var physical = ShellCoordinateProjection.ToPhysicalSize(logical.Width, logical.Height, scale);
        var x = current.X + current.Width / 2 - physical.Width / 2;
        var y = current.Y + current.Height / 2 - physical.Height / 2;
        return new RectInt32(x, y, physical.Width, physical.Height);
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

    private Task AnimateShellBoundsAsync(RectInt32 start, RectInt32 target, PowerFlowShellState fromState, PowerFlowShellState toState, bool animate, long generation)
    {
        if (!IsCurrentTransition(generation)) return Task.CompletedTask;
        var reducedMotion = !animate || !ShouldAnimatePresentation();
        var travel = Math.Sqrt(Math.Pow(target.Width - start.Width, 2) + Math.Pow(target.Height - start.Height, 2));
        var duration = ShellMotionPolicy.DurationForTravel(fromState, toState, reducedMotion, travel);
        var now = DateTimeOffset.UtcNow;
        var activeFrame = _motionClockActive ? _motionCoordinator.Sample(now) : default;
        var effectiveStart = _motionClockActive ? activeFrame.Bounds : start;
        var fromLogical = LogicalSize(effectiveStart);
        var toLogical = LogicalSize(target);
        var fromProfile = PowerFlowShellLayout.Resolve(fromLogical.Width, fromLogical.Height, fromState, _currentSection, fromState is PowerFlowShellState.Expanded or PowerFlowShellState.Workspace or PowerFlowShellState.FullScreen ? ShellDensity.Expanded : ShellDensity.Compact);
        var toProfile = PowerFlowShellLayout.Resolve(toLogical.Width, toLogical.Height, toState, _currentSection, toState is PowerFlowShellState.Expanded or PowerFlowShellState.Workspace or PowerFlowShellState.FullScreen ? ShellDensity.Expanded : ShellDensity.Compact);
        _motionFromDisclosure = ShellDisclosurePolicy.Progress(fromLogical, fromState);
        _motionToDisclosure = ShellDisclosurePolicy.Progress(toLogical, toState);

        if (duration == TimeSpan.Zero || effectiveStart.Equals(target))
        {
            StopShellMotionClock(completeAwaiter: true);
            BeginResizeModeSyncSuppression();
            try
            {
                AppWindow.MoveAndResize(target);
            }
            finally
            {
                EndResizeModeSyncSuppression();
            }
            if (!IsCurrentTransition(generation)) return Task.CompletedTask;
            var targetLogical = LogicalSize(target);
            ApplyShellLayout(toState, targetLogical.Width, targetLogical.Height);
            ApplyShellTransitionFrame(fromProfile, toProfile, 1d, reducedMotion: true);
            ResetSemanticMorphPresentation(toProfile);
            return Task.CompletedTask;
        }

        PrepareShellTransition(fromProfile, toProfile);
        _motionCompletion?.TrySetResult();
        _motionCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _motionFromProfile = fromProfile;
        _motionToProfile = toProfile;
        _motionToState = toState;
        _motionGeneration = generation;
        var material = ShellMotionPolicy.NativeMaterial(fromState, toState);
        if (_motionClockActive)
        {
            _motionCoordinator.Retarget(target, material, duration, now);
        }
        else
        {
            _motionCoordinator.Begin(effectiveStart, target, material, duration, now);
            BeginResizeModeSyncSuppression();
            _motionResizeSuppressed = true;
        }
        StartShellMotionClock();
        return _motionCompletion.Task;
    }

    private void StartShellMotionClock()
    {
        if (_motionClockActive) return;
        if (_motionFrameTimer is null)
        {
            _motionFrameTimer = _dispatcher.CreateTimer();
            _motionFrameTimer.Interval = TimeSpan.FromMilliseconds(16);
            _motionFrameTimer.IsRepeating = true;
            _motionFrameTimer.Tick += OnShellMotionTick;
        }
        _motionClockActive = true;
        _motionFrameTimer.Start();
    }

    private void StopShellMotionClock(bool completeAwaiter = false)
    {
        if (_motionClockActive)
        {
            _motionFrameTimer?.Stop();
            _motionClockActive = false;
        }
        if (_motionResizeSuppressed)
        {
            EndResizeModeSyncSuppression();
            _motionResizeSuppressed = false;
        }
        if (!completeAwaiter) return;
        var completion = _motionCompletion;
        _motionCompletion = null;
        completion?.TrySetResult();
    }

    private void OnShellMotionTick(DispatcherQueueTimer sender, object args)
    {
        if (!_motionClockActive) return;
        var frame = _motionCoordinator.Sample(DateTimeOffset.UtcNow);
        ApplyMotionFrame(frame);
    }

    private void ApplyMotionFrame(ShellMotionFrame frame)
    {
        if (!IsCurrentTransition(_motionGeneration))
        {
            StopShellMotionClock(completeAwaiter: true);
            return;
        }
        AppWindow.MoveAndResize(frame.Bounds);
        ApplyShellGeometryMorph(_motionFromProfile, _motionToProfile, frame.Sample.Progress);
        ApplyShellTransitionFrame(_motionFromProfile, _motionToProfile, frame.ChildSample.Progress, reducedMotion: false);
        var disclosure = Lerp(_motionFromDisclosure, _motionToDisclosure, Math.Clamp(frame.ChildSample.Progress, 0d, 1d));
        ApplyDisclosureProgress(disclosure, settled: false);
        // Keep material response in the motion model only. Per-frame WinUI Scale mutation here can native-fail-fast CoreMessaging during AppWindow motion.
        if (!frame.IsComplete) return;

        StopShellMotionClock();
        AppWindow.MoveAndResize(frame.Bounds);
        var finalLogical = LogicalSize(frame.Bounds);
        ApplyShellLayout(_motionToState, finalLogical.Width, finalLogical.Height);
        ResetSemanticMorphPresentation(_motionToProfile);
        ApplyDisclosureProgress(_motionToDisclosure, settled: true);
        var completion = _motionCompletion;
        _motionCompletion = null;
        completion?.TrySetResult();
    }


    private void PrepareShellTransition(ShellPresentationProfile from, ShellPresentationProfile to)
    {
        if (to.Navigation == NavigationPresentation.Rail)
            ApplyNavigationPresentation(NavigationPresentation.Rail, to.Geometry.NavigationWidth);
        else if (from.Navigation == NavigationPresentation.Rail)
            ApplyNavigationPresentation(NavigationPresentation.Rail, from.Geometry.NavigationWidth);

        if (from.GovernorControls != GovernorControlPresentation.Summary || to.GovernorControls != GovernorControlPresentation.Summary)
            AdaptiveControlRegion.Visibility = Visibility.Visible;
    }

    private void ApplyShellGeometryMorph(ShellPresentationProfile from, ShellPresentationProfile to, double progress)
    {
        var t = Math.Clamp(progress, 0d, 1d);
        var geometry = ShellTransitionGeometry.InterpolateGeometry(from.Geometry, to.Geometry, t);
        CockpitSurface.Padding = new Thickness(geometry.ContentPadding);
        CockpitSurface.RowSpacing = geometry.Gap;
        HeaderContentRow.ColumnSpacing = geometry.Gap;
        SystemHeaderRow.Margin = new Thickness(geometry.ContentPadding, 0, geometry.ContentPadding, 0);
        AdaptiveControlRegion.ColumnSpacing = geometry.Gap;
        SystemHeaderRowDefinition.Height = new GridLength(Math.Max(0, geometry.HeaderHeight));
        AdaptiveControlRowDefinition.Height = new GridLength(Math.Max(0, Lerp(ControlBandHeight(from), ControlBandHeight(to), t)));

        var fromControls = from.GovernorControls != GovernorControlPresentation.Summary;
        var toControls = to.GovernorControls != GovernorControlPresentation.Summary;
        AdaptiveControlRegion.Opacity = fromControls == toControls ? 1d : toControls ? t : 1d - t;


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
        GovernorControlPresentation.Bias => 96,
        GovernorControlPresentation.Contextual => Math.Max(144, profile.Geometry.ControlBandHeight),
        _ => Math.Max(156, profile.Geometry.ControlBandHeight + 18)
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
        foreach (var element in new UIElement[] { PresentationActions, NavigationRail, AdaptiveControlRegion, SectionHost })
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
        var completion = _motionCompletion;
        _motionCompletion = null;
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
        var configuredTension = _config.EffectiveGovernorTensionPercent;
        var shadowPolicy = GovernorTensionPolicy.Resolve(configuredTension, learningModel.Envelope, modelEntitlement);
        var shadowEvaluation = _tensionShadowProvider?.Invoke();

        PerformanceTimeline.Apply(ViewModel.OperatingHistory, learningModel.Envelope, _graphWindowSeconds);
        PerformanceTimeline.SetCoreThreadHistory(_recorder.History);
        PerformanceTimeline.SetProcessorPolicySnapshot(ReadProcessorPolicySnapshot());
        PerformanceTimeline.SetCoreActuatorStatus(_coreActuatorStatusProvider?.Invoke());
        PerformanceTimeline.SetPolicyContext(learningModel.Envelope, modelEntitlement, EnvelopeTuning.Learned);
        PerformanceTimeline.SetShadowPolicyContext(shadowPolicy.EffectiveEnvelope);
        PerformanceTimeline.SetTuneMode(false);

        ModelConfidenceText.Text = learningModel.Confidence == EnvelopeConfidence.Low
            ? "LEARNING"
            : $"{learningModel.Confidence.ToString().ToUpperInvariant()} CONFIDENCE";

        var actor = ShortActor(snapshot.TriggerApplication);
        SelectedActorNameText.Text = string.IsNullOrWhiteSpace(actor) ? "SYSTEM / NO DOMINANT ACTOR" : actor;
        var manualAuthority = snapshot.IsLatched && string.Equals(snapshot.LatchType, "Manual", StringComparison.OrdinalIgnoreCase);
        var currentProfile = _currentProfileProvider?.Invoke();
        var baseline = _config.EffectiveMachineBaselineRuns.OrderByDescending(run => run.CapturedAt).FirstOrDefault();
        var comparison = GovernorComparisonProjection.Create(ViewModel.LatestGovernorDecision, currentProfile, manualAuthority, shadowEvaluation, baseline);

        CurrentAutoStateText.Text = comparison.CurrentStateText;
        CurrentAutoDetailText.Text = comparison.CurrentDetailText;
        TensionShadowStateText.Text = $"SHADOW · T{configuredTension:0.#}";
        TensionShadowDetailText.Text = comparison.ShadowDetailText;
        GovernorTensionValueText.Text = $"T{configuredTension:0.#}";
        GovernorReferenceText.Text = shadowPolicy.Reference switch
        {
            GovernorTensionReference.SaverBias => "SAVER BIAS",
            GovernorTensionReference.BalancedEfficient => "BAL-E",
            GovernorTensionReference.BalancedPerformance => "BAL-P",
            GovernorTensionReference.PerformanceBias => "PERF BIAS",
            _ => "BAL-E"
        };
        CurrentEvidenceText.Text = comparison.CurrentEvidenceText;
        ShadowEvidenceText.Text = comparison.ShadowEvidenceText;
        ToolTipService.SetToolTip(GovernorTensionSlider,
            $"Shadow only. {comparison.ScaleBehaviorText}; {comparison.SustainBehaviorText}; {comparison.SettleBehaviorText}. Current AUTO remains applied.");

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
    private async void OnGovernorTensionChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        var value = Math.Clamp(e.NewValue, 0d, 100d);
        if (Math.Abs(_config.EffectiveGovernorTensionPercent - value) < .01d) return;

        _config = _config with { GovernorTensionPercent = value };
        ViewModel.Configure(_config);
        ApplyVisualState(_controller.Snapshot);
        var version = Interlocked.Increment(ref _tensionChangeVersion);
        try
        {
            await Task.Delay(250);
            if (version != Volatile.Read(ref _tensionChangeVersion)) return;
            await ApplyConfigFromPageAsync(_config with { GovernorTensionPercent = value });
        }
        catch (Exception ex)
        {
            TensionShadowDetailText.Text = $"SHADOW UPDATE FAILED · {ex.Message}";
        }
    }
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

    private void OnHoverSurfacePointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!_shellVisible || _shellState != PowerFlowShellState.Glance || _activationMode != ShellActivationMode.PinnedActive) return;
        if (!e.GetCurrentPoint(GlanceTapTarget).Properties.IsLeftButtonPressed) return;
        if (!GetCursorPos(out _headerDragPointerOrigin)) return;
        _headerDragWindowOrigin = AppWindow.Position;
        _hoverSurfacePointerId = e.Pointer.PointerId;
        _hoverSurfaceDragArmed = true;
    }

    private void OnHoverSurfacePointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_hoverSurfaceDragArmed || e.Pointer.PointerId != _hoverSurfacePointerId) return;
        if (!e.GetCurrentPoint(GlanceTapTarget).Properties.IsLeftButtonPressed)
        {
            CancelHoverSurfaceDrag();
            return;
        }
        if (!GetCursorPos(out var pointer)) return;
        var deltaX = pointer.X - _headerDragPointerOrigin.X;
        var deltaY = pointer.Y - _headerDragPointerOrigin.Y;
        if (Math.Abs(deltaX) < 3 && Math.Abs(deltaY) < 3) return;
        if (!_headerDragActive)
        {
            _headerDragActive = true;
            GlanceTapTarget.CapturePointer(e.Pointer);
            EnsureHeaderDragTimer();
            _headerDragTimer!.Start();
        }
        e.Handled = true;
    }

    private void OnHoverSurfacePointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_hoverSurfaceDragArmed || e.Pointer.PointerId != _hoverSurfacePointerId) return;
        var moved = _headerDragActive;
        if (_headerDragActive) GlanceTapTarget.ReleasePointerCapture(e.Pointer);
        CancelHoverSurfaceDrag();
        if (moved) e.Handled = true;
    }

    private void OnHoverSurfacePointerCanceled(object sender, PointerRoutedEventArgs e) => CancelHoverSurfaceDrag();
    private void OnHoverSurfacePointerCaptureLost(object sender, PointerRoutedEventArgs e) => CancelHoverSurfaceDrag();

    private void CancelHoverSurfaceDrag()
    {
        _hoverSurfaceDragArmed = false;
        _headerDragActive = false;
        _headerDragTimer?.Stop();
    }
    private void OnHeaderDragStarted(object? sender, EventArgs e)
    {
        _headerDragActive = false;
        if (!_shellVisible || _activationMode != ShellActivationMode.PinnedActive) return;
        if (_shellState is PowerFlowShellState.Hidden or PowerFlowShellState.FullScreen) return;
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
    private async void OnHeaderLiveRequested(object? sender, EventArgs e) => await OpenSectionAsync("flow");
    private async void OnHeaderRulesRequested(object? sender, EventArgs e) => await OpenSectionAsync("rules");
    private async void OnHeaderBaselineRequested(object? sender, EventArgs e) => await OpenSectionAsync("profile");
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
        StopShellMotionClock(completeAwaiter: true);
        CancelPendingResizeReflow();
        _headerDragActive = false;
        AppWindow.Closing -= OnAppWindowClosing;
        AppWindow.Changed -= OnAppWindowChanged;
        _controller.SnapshotChanged -= OnSnapshotChanged;
        _recorder.ContinuityChanged -= OnContinuityChanged;
        SystemHeaderHost.ModeRequested -= OnModeRequested;
        SystemHeaderHost.DragStarted -= OnHeaderDragStarted;
        SystemHeaderHost.DragCompleted -= OnHeaderDragCompleted;
        SystemHeaderHost.LiveRequested -= OnHeaderLiveRequested;
        SystemHeaderHost.RulesRequested -= OnHeaderRulesRequested;
        SystemHeaderHost.BaselineRequested -= OnHeaderBaselineRequested;
        SystemHeaderHost.SettingsRequested -= OnHeaderSettingsRequested;
        GovernorTensionSlider.ValueChanged -= OnGovernorTensionChanged;
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
    }    [StructLayout(LayoutKind.Sequential)]
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
