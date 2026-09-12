using System.Numerics;
using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
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
    private readonly ShellRenderScheduler _resizeRenderScheduler = new(TimeSpan.FromMilliseconds(110));
    private DispatcherQueueTimer? _resizeFrameTimer;
    private DispatcherQueueTimer? _resizeSettleTimer;
    private bool _suppressResizeModeSync;
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
        if (state == PowerFlowShellState.Hidden)
        {
            await HideShellAsync();
            return;
        }

        if (state == PowerFlowShellState.Compact) _layoutDensity = ShellDensity.Compact;
        else if (state is PowerFlowShellState.Expanded or PowerFlowShellState.Workspace or PowerFlowShellState.FullScreen) _layoutDensity = ShellDensity.Expanded;

        var fromState = _shellVisible ? _shellState : PowerFlowShellState.Hidden;
        var leavingFullScreen = IsFullScreenPresenter() && state != PowerFlowShellState.FullScreen;
        var preservedFullScreenBounds = leavingFullScreen ? CurrentBounds() : default;

        if (leavingFullScreen)
        {
            _suppressResizeModeSync = true;
            AppWindow.SetPresenter(AppWindowPresenterKind.Overlapped);
            AppWindow.MoveAndResize(preservedFullScreenBounds);
            _suppressResizeModeSync = false;
            if (ShellPresenterTransitionPolicy.RequiresLayoutBarrier(fromState, state))
            {
                await WaitForPresenterLayoutAsync();
                AppWindow.MoveAndResize(preservedFullScreenBounds);
                await WaitForPresenterLayoutAsync();
            }
        }

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

        await AnimateShellBoundsAsync(start, target, fromState, state, animate);

        if (state == PowerFlowShellState.FullScreen)
        {
            _suppressResizeModeSync = true;
            AppWindow.SetPresenter(AppWindowPresenterKind.FullScreen);
            _suppressResizeModeSync = false;
            await WaitForPresenterLayoutAsync();
        }

        var finalLogical = CurrentLogicalAppWindowSize();
        ApplyShellLayout(state, finalLogical.Width, finalLogical.Height);
        if (activation == ShellActivationMode.PinnedActive) Activate();
    }
    public async Task HideShellAsync()
    {
        _presentationTimer?.Stop();
        _presentationTimer = null;
        if (_resizeFrameTimer is not null) { _resizeFrameTimer.Stop(); _resizeFrameTimer.Tick -= OnResizeFrameTick; _resizeFrameTimer = null; }
        if (_resizeSettleTimer is not null) { _resizeSettleTimer.Stop(); _resizeSettleTimer.Tick -= OnResizeSettleTick; _resizeSettleTimer = null; }
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
            PowerFlowShellState.FullScreen => PowerFlowShellState.Workspace,
            PowerFlowShellState.Workspace => PowerFlowShellState.FullScreen,
            PowerFlowShellState.Expanded => PowerFlowShellState.Workspace,
            _ => PowerFlowShellState.Expanded
        };
        await TransitionToAsync(next, ShellActivationMode.PinnedActive, animate: true);
    }
    private async void OnCompactClicked(object sender, RoutedEventArgs e)
        => await TransitionToAsync(PowerFlowShellState.Compact, ShellActivationMode.PinnedActive, animate: true);

    private void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if ((!args.DidSizeChange && !args.DidPresenterChange) || _suppressResizeModeSync || !_shellVisible) return;
        _resizeRenderScheduler.SubmitSize(CurrentLogicalAppWindowSize(), DateTimeOffset.UtcNow);
        EnsureResizeRenderLoop();
    }

    private void EnsureResizeRenderLoop()
    {
        if (_resizeFrameTimer is null)
        {
            _resizeFrameTimer = _dispatcher.CreateTimer();
            _resizeFrameTimer.Interval = TimeSpan.FromMilliseconds(16);
            _resizeFrameTimer.IsRepeating = true;
            _resizeFrameTimer.Tick += OnResizeFrameTick;
        }
        if (_resizeSettleTimer is null)
        {
            _resizeSettleTimer = _dispatcher.CreateTimer();
            _resizeSettleTimer.Interval = TimeSpan.FromMilliseconds(25);
            _resizeSettleTimer.IsRepeating = true;
            _resizeSettleTimer.Tick += OnResizeSettleTick;
        }
        if (!_resizeFrameTimer.IsRunning) _resizeFrameTimer.Start();
        if (!_resizeSettleTimer.IsRunning) _resizeSettleTimer.Start();
    }

    private void OnResizeFrameTick(DispatcherQueueTimer sender, object args)
    {
        var pending = _resizeRenderScheduler.ConsumePendingFrame();
        if (pending is not ShellLogicalSize size)
        {
            sender.Stop();
            return;
        }
        ApplyInteractiveResizeFrame(size);
    }

    private void OnResizeSettleTick(DispatcherQueueTimer sender, object args)
    {
        if (!_resizeRenderScheduler.ShouldCommit(DateTimeOffset.UtcNow)) return;
        CommitResizePresentation(CurrentLogicalAppWindowSize());
        _resizeRenderScheduler.MarkCommitted();
        sender.Stop();
    }

    private void ApplyInteractiveResizeFrame(ShellLogicalSize size)
    {
        if (_shellState is PowerFlowShellState.Glance or PowerFlowShellState.FullScreen || IsFullScreenPresenter()) return;
        var progress = ShellResponsiveDensity.MorphProgress(size);
        var compactProfile = PowerFlowShellLayout.Resolve(size.Width, size.Height, _shellState, _currentSection, ShellDensity.Compact);
        var expandedProfile = PowerFlowShellLayout.Resolve(size.Width, size.Height, _shellState, _currentSection, ShellDensity.Expanded);
        ApplyShellGeometryMorph(compactProfile, expandedProfile, progress);
        ApplyShellTransitionFrame(compactProfile, expandedProfile, progress, reducedMotion: false);
        SystemHeaderHost.ShowBrand = true;
        SystemHeaderHost.ApplyBrandMorph(1d - progress);
    }

    private void CommitResizePresentation(ShellLogicalSize size)
    {
        if (_shellState is not PowerFlowShellState.Glance and not PowerFlowShellState.FullScreen && !IsFullScreenPresenter())
            _layoutDensity = ShellResponsiveDensity.Resolve(size, _layoutDensity);
        ApplyShellLayout(_shellState, size.Width, size.Height);
        var profile = PowerFlowShellLayout.Resolve(size.Width, size.Height, _shellState, _currentSection, _layoutDensity);
        ResetSemanticMorphPresentation(profile);
    }
    private void ApplyResponsiveResizeMorph(PowerFlowShellState state, int width, int height)
    {
        var logical = new ShellLogicalSize(width, height);
        var progress = ShellResponsiveDensity.MorphProgress(logical);
        if (!ShouldAnimatePresentation() || progress <= 0d || progress >= 1d)
        {
            var endpointDensity = progress <= 0d
                ? ShellDensity.Compact
                : progress >= 1d
                    ? ShellDensity.Expanded
                    : _layoutDensity;
            _layoutDensity = endpointDensity;
            ApplyShellLayout(state, width, height);
            var endpointProfile = PowerFlowShellLayout.Resolve(width, height, state, _currentSection, endpointDensity);
            ResetSemanticMorphPresentation(endpointProfile);
            return;
        }

        var compactProfile = PowerFlowShellLayout.Resolve(width, height, state, _currentSection, ShellDensity.Compact);
        var expandedProfile = PowerFlowShellLayout.Resolve(width, height, state, _currentSection, ShellDensity.Expanded);
        PrepareShellTransition(compactProfile, expandedProfile);
        ApplyShellGeometryMorph(compactProfile, expandedProfile, progress);
        ApplyShellTransitionFrame(compactProfile, expandedProfile, progress, reducedMotion: false);

        SystemHeaderHost.ShowBrand = true;
        SystemHeaderHost.ApplyBrandMorph(1d - progress);
        CompactButton.Visibility = Visibility.Visible;
        CompactButton.Opacity = progress;
        CompactButton.IsHitTestVisible = progress >= .6d;
        PresentationToggleButton.Content = progress < .5d ? "EXPAND" : "FULL SCREEN";
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
        PresentationActions.Visibility = glance ? Visibility.Collapsed : Visibility.Visible;
        CompactButton.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        CompactButton.Opacity = 1d;
        CompactButton.IsHitTestVisible = expanded;
        PresentationToggleButton.Content = state switch
        {
            PowerFlowShellState.FullScreen => "RESTORE",
            PowerFlowShellState.Workspace => "FULL SCREEN",
            PowerFlowShellState.Expanded => "WORKSPACE",
            _ => "EXPAND"
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
        AdaptiveControlRegion.Visibility = profile.GovernorControls != GovernorControlPresentation.Summary ? Visibility.Visible : Visibility.Collapsed;
        GovernorDetailPanel.Visibility = profile.GovernorControls is GovernorControlPresentation.Contextual or GovernorControlPresentation.Deep ? Visibility.Visible : Visibility.Collapsed;
        GovernorDetailPanel.Opacity = GovernorDetailPanel.Visibility == Visibility.Visible ? 1d : 0d;
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
        var current = CurrentBounds();
        if (_lastTrayAnchor is { } tray && _lastWorkArea is { } work)
            return ShellTransitionGeometry.TargetBounds(tray, work, current, state, scale);

        if (state == PowerFlowShellState.Hidden) return new RectInt32(current.X, current.Y, 1, 1);
        var logical = state switch
        {
            PowerFlowShellState.Glance => new ShellLogicalSize(320, 176),
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

    private Task AnimateShellBoundsAsync(RectInt32 start, RectInt32 target, PowerFlowShellState fromState, PowerFlowShellState toState, bool animate)
    {
        _presentationTimer?.Stop();
        _presentationTimer = null;
        if (_resizeFrameTimer is not null) { _resizeFrameTimer.Stop(); _resizeFrameTimer.Tick -= OnResizeFrameTick; _resizeFrameTimer = null; }
        if (_resizeSettleTimer is not null) { _resizeSettleTimer.Stop(); _resizeSettleTimer.Tick -= OnResizeSettleTick; _resizeSettleTimer = null; }
        var reducedMotion = !animate || !ShouldAnimatePresentation();
        var travel = Math.Sqrt(Math.Pow(target.Width - start.Width, 2) + Math.Pow(target.Height - start.Height, 2));
        var duration = ShellMotionPolicy.DurationForTravel(fromState, toState, reducedMotion, travel);
        var fromLogical = LogicalSize(start);
        var toLogical = LogicalSize(target);
        var fromProfile = PowerFlowShellLayout.Resolve(fromLogical.Width, fromLogical.Height, fromState, _currentSection, fromState is PowerFlowShellState.Expanded or PowerFlowShellState.Workspace or PowerFlowShellState.FullScreen ? ShellDensity.Expanded : ShellDensity.Compact);
        var toProfile = PowerFlowShellLayout.Resolve(toLogical.Width, toLogical.Height, toState, _currentSection, toState is PowerFlowShellState.Expanded or PowerFlowShellState.Workspace or PowerFlowShellState.FullScreen ? ShellDensity.Expanded : ShellDensity.Compact);

        if (duration == TimeSpan.Zero || start.Equals(target))
        {
            _suppressResizeModeSync = true;
            AppWindow.MoveAndResize(target);
            _suppressResizeModeSync = false;
            var targetLogical = LogicalSize(target);
            ApplyShellLayout(toState, targetLogical.Width, targetLogical.Height);
            ApplyShellTransitionFrame(fromProfile, toProfile, 1d, reducedMotion: true);
            ResetSemanticMorphPresentation(toProfile);
            return Task.CompletedTask;
        }

        PrepareShellTransition(fromProfile, toProfile);
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var timer = _dispatcher.CreateTimer();
        _presentationTimer = timer;
        timer.Interval = TimeSpan.FromMilliseconds(16);
        _suppressResizeModeSync = true;
        timer.Tick += Tick;
        timer.Start();
        return tcs.Task;

        void Tick(DispatcherQueueTimer sender, object args)
        {
            var t = Math.Clamp(stopwatch.Elapsed.TotalMilliseconds / duration.TotalMilliseconds, 0d, 1d);
            var eased = ShellMotionPolicy.Ease(fromState, toState, t);
            var rect = ShellTransitionGeometry.Interpolate(start, target, eased);
            AppWindow.MoveAndResize(rect);
            ApplyShellGeometryMorph(fromProfile, toProfile, eased);
            ApplyShellTransitionFrame(fromProfile, toProfile, eased, reducedMotion: false);

            if (t < 1d) return;
            sender.Stop();
            sender.Tick -= Tick;
            stopwatch.Stop();
            AppWindow.MoveAndResize(target);
            _presentationTimer = null;
            _suppressResizeModeSync = false;
            ApplyShellTransitionFrame(fromProfile, toProfile, 1d, reducedMotion: false);
            var finalLogical = LogicalSize(target);
            ApplyShellLayout(toState, finalLogical.Width, finalLogical.Height);
            ResetSemanticMorphPresentation(toProfile);
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
        var fromDetail = from.GovernorControls is GovernorControlPresentation.Contextual or GovernorControlPresentation.Deep;
        var toDetail = to.GovernorControls is GovernorControlPresentation.Contextual or GovernorControlPresentation.Deep;
        if (fromDetail || toDetail) GovernorDetailPanel.Visibility = Visibility.Visible;
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
        var fromDetail = from.GovernorControls is GovernorControlPresentation.Contextual or GovernorControlPresentation.Deep;
        var toDetail = to.GovernorControls is GovernorControlPresentation.Contextual or GovernorControlPresentation.Deep;
        GovernorDetailPanel.Opacity = fromDetail == toDetail ? (toDetail ? 1d : 0d) : toDetail ? t : 1d - t;

        var fromFooter = ShowsFooter(from);
        var toFooter = ShowsFooter(to);
        StatusFooter.Opacity = fromFooter == toFooter ? 1d : toFooter ? t : 1d - t;

        var paneWidth = Math.Max(0d, geometry.NavigationWidth);
        if (from.Navigation == NavigationPresentation.Rail || to.Navigation == NavigationPresentation.Rail)
            NavigationRail.OpenPaneLength = paneWidth;

        var sectionVisual = ElementCompositionPreview.GetElementVisual(SectionHost);
        if (from.Navigation != to.Navigation)
        {
            var shift = to.Navigation == NavigationPresentation.Rail
                ? -paneWidth * (1d - t)
                : -paneWidth * t;
            sectionVisual.Offset = new Vector3((float)shift, 0, 0);
        }
        else sectionVisual.Offset = Vector3.Zero;
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
        ElementCompositionPreview.GetElementVisual(NavigationRail).Offset = Vector3.Zero;
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
        foreach (var element in new UIElement[] { PresentationActions, NavigationRail, StatusFooter, AdaptiveControlRegion, SectionHost })
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
        _presentationTimer?.Stop();
        _presentationTimer = null;
        if (_resizeFrameTimer is not null) { _resizeFrameTimer.Stop(); _resizeFrameTimer.Tick -= OnResizeFrameTick; _resizeFrameTimer = null; }
        if (_resizeSettleTimer is not null) { _resizeSettleTimer.Stop(); _resizeSettleTimer.Tick -= OnResizeSettleTick; _resizeSettleTimer = null; }
        AppWindow.Closing -= OnAppWindowClosing;
        AppWindow.Changed -= OnAppWindowChanged;
        _controller.SnapshotChanged -= OnSnapshotChanged;
        _recorder.ContinuityChanged -= OnContinuityChanged;
        SystemHeaderHost.ModeRequested -= OnModeRequested;
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
