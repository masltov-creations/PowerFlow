using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using PowerFlow.App.Controller;
using PowerFlow.App.Dashboard;
using PowerFlow.App.Startup;
using PowerFlow.App.Telemetry;
using PowerFlow.App.Tray;
using PowerFlow.Core.Policy;
using PowerFlow.Core.Rules;
using PowerFlow.Windows.Activity;
using PowerFlow.Windows.Configuration;
using PowerFlow.Windows.Games;
using PowerFlow.Windows.Power;
using PowerFlow.Windows.Startup;

namespace PowerFlow.App;

public partial class App : Application
{
    private const string DashboardOpenSignalName = @"Local\PowerFlow.OpenDashboard.v1";
    private SingleInstanceGuard? _instanceGuard;
    private SingleInstanceSignal? _dashboardOpenSignal;
    private int _dashboardOpenRequested;
    private int _dashboardFullScreenRequested;
    private int _runtimeReady;
    private PowerFlowController? _controller;
    private TelemetryContinuityRecorder? _telemetryRecorder;
    private GameLifecycleMonitor? _games;
    private TrayIconHost? _tray;
    private DispatcherQueueTimer? _trayHoverTimer;
    private readonly TrayHoverPolicy _trayHoverPolicy = new(TimeSpan.FromMilliseconds(350));
    private readonly AdaptiveGovernorRuntime _adaptiveGovernorRuntime = new();
    private readonly GraduatedCoreFloorActuatorRuntime _graduatedCoreActuatorRuntime = new();
    private readonly PowerModeProfileRuntime _powerModeProfileRuntime = new();
    private readonly EfficiencyExperimentRuntime _efficiencyExperimentRuntime = new();
    private DispatcherQueue? _dispatcher;
    private MainWindow? _shellWindow;
    private JsonConfigStore? _configStore;
    private PowerFlowConfig _config = PowerFlowConfig.Default;
    private StartupRegistration? _startupRegistration;
    private bool _shuttingDown;
    private bool _previewMode;

    public App() => InitializeComponent();

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        _dispatcher = DispatcherQueue.GetForCurrentThread();
        var launchArgs = Environment.GetCommandLineArgs();
        _previewMode = LaunchIntent.IsPreview(launchArgs);
        _instanceGuard = SingleInstanceGuard.TryAcquire(@"Local\PowerFlow.Controller.v1");
        if (!_instanceGuard.IsPrimary)
        {
            if (!_previewMode && LaunchIntent.ShouldOpenDashboard(launchArgs))
                SingleInstanceSignal.TrySignal(DashboardOpenSignalName, TimeSpan.FromSeconds(1));
            Exit();
            return;
        }

        if (!_previewMode) _dashboardOpenSignal = SingleInstanceSignal.Listen(DashboardOpenSignalName, OnDashboardOpenSignal);
        try
        {
            var dataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PowerFlow");
            _configStore = new JsonConfigStore(dataDirectory);
            _config = await _configStore.LoadAsync();
            if (!_previewMode && !string.IsNullOrWhiteSpace(Environment.ProcessPath))
            {
                _startupRegistration = new StartupRegistration(Environment.ProcessPath!);
                _startupRegistration.SetEnabled(_config.StartWithWindows);
            }

            _games = new GameLifecycleMonitor();
            IPowerPlanController planController = _previewMode
                ? new PreviewPowerPlanController(new WindowsPowerPlanController())
                : new WindowsPowerPlanController();
            _controller = new PowerFlowController(_config, planController, new SystemTimesActivitySource(), _games, new PeriodicControllerTickSourceFactory(), new SystemControllerDelay(), new SystemControllerClock(), new WindowsPowerPlanObserver());
            _telemetryRecorder = new TelemetryContinuityRecorder(() => new DashboardTelemetrySource(), new PeriodicControllerTickSourceFactory(), new SystemControllerClock(), visibleInterval: _config.EffectiveTelemetryVisibleInterval, hiddenInterval: _config.EffectiveTelemetryBackgroundInterval);
            _controller.SnapshotChanged += OnSnapshotChanged;
            await _controller.StartAsync();
            await _telemetryRecorder.StartAsync();
            _telemetryRecorder.ContinuityChanged += OnTelemetryContinuityChanged;

            if (LaunchIntent.ShouldCreateTray(launchArgs))
            {
                _tray = new TrayIconHost(_controller.Snapshot);
                _tray.CommandInvoked += OnTrayCommandInvoked;
                _tray.InteractionRequested += OnTrayInteractionRequested;
            }

            if (LaunchIntent.ShouldOpenDashboard(launchArgs)) Interlocked.Exchange(ref _dashboardOpenRequested, 1);
            if (LaunchIntent.ShouldOpenFullScreen(launchArgs)) Interlocked.Exchange(ref _dashboardFullScreenRequested, 1);
            Volatile.Write(ref _runtimeReady, 1);
            if (LaunchIntent.ShouldOpenPopupPreview(launchArgs)) await ShowGlancePreviewAsync();
            await DrainDashboardOpenRequestAsync();
        }
        catch (Exception ex)
        {
            await WriteStartupFailureAsync(ex);
            await ShutdownAsync(true);
        }
    }

    private void OnDashboardOpenSignal()
    {
        Interlocked.Exchange(ref _dashboardOpenRequested, 1);
        if (Volatile.Read(ref _runtimeReady) != 1 || _shuttingDown) return;
        _dispatcher?.TryEnqueue(async () =>
        {
            try { await DrainDashboardOpenRequestAsync(); }
            catch (Exception ex) { await WriteStartupFailureAsync(ex); }
        });
    }

    private async Task DrainDashboardOpenRequestAsync()
    {
        if (_shuttingDown || Interlocked.Exchange(ref _dashboardOpenRequested, 0) == 0) return;
        var fullScreen = Interlocked.Exchange(ref _dashboardFullScreenRequested, 0) == 1;
        await OpenDashboardAsync(false, fullScreen);
    }

    private void OnSnapshotChanged(object? sender, ControllerSnapshot snapshot)
    {
        _telemetryRecorder?.UpdateControllerSnapshot(snapshot);
        if (_config.AdaptiveActuationEnabled && _controller is not null && _telemetryRecorder is not null)
        {
            var evaluation = _adaptiveGovernorRuntime.Evaluate(snapshot, _telemetryRecorder.History, _config);
            if (evaluation is not null) _ = ApplyAdaptiveGovernorEvaluationAsync(evaluation);
        }
        _dispatcher?.TryEnqueue(() => _tray?.Update(snapshot, _powerModeProfileRuntime.CurrentProfile?.Mode));
    }

    private void OnTelemetryContinuityChanged(object? sender, EventArgs e)
    {
        if (_telemetryRecorder is null || _shuttingDown) return;
        _efficiencyExperimentRuntime.Observe(_telemetryRecorder.History);
        var input = GraduatedCoreFloorActuatorRuntime.BuildInput(_telemetryRecorder.History);
        _graduatedCoreActuatorRuntime.Evaluate(
            input,
            enabled: _config.GraduatedCoreActuationEnabled,
            previewMode: _previewMode,
            coarseAdaptiveActuationEnabled: _config.AdaptiveActuationEnabled,
            qualifiedSchemeId: _config.PowerSaverPlanId ?? PowerPlanIds.PowerSaver);
    }
    private async Task ApplyAdaptiveGovernorEvaluationAsync(AdaptiveGovernorRuntimeEvaluation evaluation)
    {
        if (_controller is null || _shuttingDown) return;
        try
        {
            await _controller.ApplyAdaptiveGovernorDecisionAsync(evaluation.Decision, evaluation.Entitlement, evaluation.Actor);
        }
        catch (Exception ex)
        {
            await WriteStartupFailureAsync(ex);
        }
    }
    private void OnTrayInteractionRequested(object? sender, TrayInteractionRequestedEventArgs e)
    {
        _dispatcher?.TryEnqueue(async () =>
        {
            if (_shuttingDown || _tray is null) return;
            try
            {
                switch (e.Kind)
                {
                    case TrayInteractionKind.Hover:
                        if (_shellWindow?.ActivationMode == ShellActivationMode.PinnedActive && _shellWindow.IsShellVisible) return;
                        _trayHoverPolicy.BeginHover(DateTimeOffset.UtcNow);
                        EnsureTrayHoverTimer();
                        break;
                    case TrayInteractionKind.SingleClick:
                        ResetTransientHoverState();
                        await ShowShellFromTrayAsync(PowerFlowShellState.Glance, ShellActivationMode.PinnedActive, waitForTrayRect: false, animate: true);
                        break;
                    case TrayInteractionKind.DoubleClick:
                        ResetTransientHoverState();
                        await ShowShellFromTrayAsync(PowerFlowShellState.Compact, ShellActivationMode.PinnedActive, waitForTrayRect: false, animate: true);
                        break;
                }
            }
            catch (Exception ex) { await WriteStartupFailureAsync(ex); }
        });
    }

    private void EnsureTrayHoverTimer()
    {
        if (_dispatcher is null) return;
        if (_trayHoverTimer is null)
        {
            _trayHoverTimer = _dispatcher.CreateTimer();
            _trayHoverTimer.Interval = TimeSpan.FromMilliseconds(100);
            _trayHoverTimer.IsRepeating = true;
            _trayHoverTimer.Tick += OnTrayHoverTick;
        }
        if (!_trayHoverTimer.IsRunning) _trayHoverTimer.Start();
    }

    private async void OnTrayHoverTick(DispatcherQueueTimer sender, object args)
    {
        if (_tray is null || _shuttingDown) { StopTrayHoverTimer(); return; }
        var overIcon = _tray.IsPointerOverIcon();
        var overShell = _shellWindow?.ShellState == PowerFlowShellState.Glance && _shellWindow.ContainsCursor();
        var action = _trayHoverPolicy.Evaluate(DateTimeOffset.UtcNow, overIcon, overShell);
        try
        {
            switch (action)
            {
                case TrayHoverAction.Show:
                    await ShowShellFromTrayAsync(PowerFlowShellState.Glance, ShellActivationMode.TransientNoActivate, waitForTrayRect: false, animate: true);
                    break;
                case TrayHoverAction.Hide:
                case TrayHoverAction.Cancel:
                    if (_shellWindow?.ActivationMode == ShellActivationMode.TransientNoActivate)
                        await _shellWindow.HideShellAsync();
                    StopTrayHoverTimer();
                    break;
            }
        }
        catch (Exception ex) { await WriteStartupFailureAsync(ex); }
    }

    private async Task ShowGlancePreviewAsync()
        => await ShowShellFromTrayAsync(PowerFlowShellState.Glance, ShellActivationMode.TransientNoActivate, waitForTrayRect: true, animate: false);

    private async Task ShowShellFromTrayAsync(PowerFlowShellState state, ShellActivationMode activation, bool waitForTrayRect, bool animate)
    {
        if (_tray is null) { await ShowShellAsync(state, activation, null, null, "flow", animate); return; }
        var attempts = waitForTrayRect ? 20 : 1;
        for (var attempt = 0; attempt < attempts; attempt++)
        {
            if (TryResolveTrayGeometry(out var iconRect, out var workArea))
            {
                await ShowShellAsync(state, activation, iconRect, workArea, "flow", animate);
                return;
            }
            if (attempt + 1 < attempts) await Task.Delay(150);
        }
        if (waitForTrayRect) throw new InvalidOperationException("Tray icon rectangle did not become available for shell preview.");
        await ShowShellAsync(state, activation, null, null, "flow", animate);
    }

    private bool TryResolveTrayGeometry(out TrayRect iconRect, out TrayRect workArea)
    {
        iconRect = default;
        workArea = default;
        if (_tray is null) return false;
        if (_tray.TryGetIconRect(out iconRect) && _tray.TryGetWorkArea(iconRect, out workArea)) return true;
        return _tray.TryGetHoverAnchorRect(out iconRect) && _tray.TryGetWorkArea(iconRect, out workArea);
    }

    private void ResetTransientHoverState()
    {
        _ = _trayHoverPolicy.Evaluate(DateTimeOffset.UtcNow, false, false);
        StopTrayHoverTimer();
    }

    private void StopTrayHoverTimer()
    {
        if (_trayHoverTimer?.IsRunning == true) _trayHoverTimer.Stop();
    }

    private async void OnTrayCommandInvoked(object? sender, TrayIconHost.TrayCommandInvokedEventArgs e)
    {
        if (_controller is null || _shuttingDown) return;
        try
        {
            switch (e.CommandId)
            {
                case TrayMenuCommands.OpenDashboard: await OpenDashboardAsync(false); break;
                case TrayMenuCommands.Auto: await ApplyOperatingModeAsync(PowerModeSelection.Auto); break;
                case TrayMenuCommands.PowerSaver: await ApplyOperatingModeAsync(PowerModeSelection.Eco); break;
                case TrayMenuCommands.Balanced: await ApplyOperatingModeAsync(PowerModeSelection.Efficient); break;
                case TrayMenuCommands.BalancedPerformance: await ApplyOperatingModeAsync(PowerModeSelection.Responsive); break;
                case TrayMenuCommands.HighPerformance: await ApplyOperatingModeAsync(PowerModeSelection.Boost); break;
                case TrayMenuCommands.Ultra: await ApplyOperatingModeAsync(PowerModeSelection.Ultra); break;
                case TrayMenuCommands.ReleaseLatch: await ApplyOperatingModeAsync(PowerModeSelection.Auto); break;
                case TrayMenuCommands.Settings: await OpenDashboardAsync(true); break;
                case TrayMenuCommands.Exit: await ShutdownAsync(true); break;
            }
        }
        catch (Exception ex) { await WriteStartupFailureAsync(ex); }
    }

    private (TrayRect? icon, TrayRect? work) TrayGeometry()
        => TryResolveTrayGeometry(out var icon, out var work) ? (icon, work) : (null, null);

    private async Task OpenDashboardAsync(bool showSettings, bool fullScreen = false)
    {
        var geometry = TrayGeometry();
        var state = fullScreen ? PowerFlowShellState.FullScreen : showSettings ? PowerFlowShellState.Expanded : PowerFlowShellState.Compact;
        await ShowShellAsync(state, ShellActivationMode.PinnedActive, geometry.icon, geometry.work, showSettings ? "settings" : "flow", animate: true);
    }

    private async Task ShowShellAsync(PowerFlowShellState state, ShellActivationMode activation, TrayRect? trayAnchor, TrayRect? workArea, string section, bool animate)
    {
        if (_controller is null || _telemetryRecorder is null) return;
        if (_shellWindow is null)
        {
            _shellWindow = new MainWindow(_controller, _telemetryRecorder, _config, ApplyConfigAsync, _previewMode, () => _graduatedCoreActuatorRuntime.Status, ApplyOperatingModeAsync, _efficiencyExperimentRuntime);
            _shellWindow.Closed += async (_, _) =>
            {
                _shellWindow = null;
                if (_previewMode) await ShutdownAsync(true);
            };
        }
        await _shellWindow.ShowShellAsync(state, activation, trayAnchor, workArea, section, animate);
    }

    private async Task ApplyOperatingModeAsync(PowerModeSelection selection)
    {
        if (_controller is null || _shuttingDown) return;
        _powerModeProfileRuntime.Restore("Previous manual PowerFlow mode profile restored.");
        if (selection == PowerModeSelection.Auto)
        {
            await _controller.ReleaseManualLatchAsync();
            _tray?.Update(_controller.Snapshot, null);
            return;
        }

        var mode = selection switch
        {
            PowerModeSelection.Eco => PowerFlowOperatingMode.Saver,
            PowerModeSelection.Efficient => PowerFlowOperatingMode.Balanced,
            PowerModeSelection.Responsive => PowerFlowOperatingMode.BalancedPerformance,
            PowerModeSelection.Boost => PowerFlowOperatingMode.Performance,
            PowerModeSelection.Ultra => PowerFlowOperatingMode.Ultra,
            _ => PowerFlowOperatingMode.Balanced
        };
        var profile = PowerFlowOperatingProfiles.For(mode);
        await _controller.SetManualStateAsync(profile.WindowsState);
        var status = _powerModeProfileRuntime.Apply(profile, liveWritesEnabled: !_previewMode);
        if (!status.Applied && status.LiveWritesEnabled)
            throw new InvalidOperationException(status.Message);
        _tray?.Update(_controller.Snapshot, _powerModeProfileRuntime.CurrentProfile?.Mode);
    }
    private async Task ApplyConfigAsync(PowerFlowConfig updated)
    {
        if (_configStore is null) return;
        if (!_previewMode) await _configStore.SaveAsync(updated);
        if (_controller is not null) await _controller.UpdatePolicyConfigAsync(updated);
        var graduatedWasEnabled = _config.GraduatedCoreActuationEnabled;
        _config = updated;
        _telemetryRecorder?.UpdateCadence(updated.EffectiveTelemetryVisibleInterval, updated.EffectiveTelemetryBackgroundInterval);
        if (graduatedWasEnabled && !updated.GraduatedCoreActuationEnabled)
            _graduatedCoreActuatorRuntime.StopAndRestore("Graduated core-floor actuation disabled; baseline restored.");
        _games?.UpdateRules(updated.AppRules);
        if (!_previewMode) _startupRegistration?.SetEnabled(updated.StartWithWindows);
    }

    private async Task ShutdownAsync(bool exitApplication)
    {
        if (_shuttingDown) return;
        _shuttingDown = true;
        try
        {
            _shellWindow?.CloseForShutdown();
            _shellWindow = null;
            if (_controller is not null)
            {
                _controller.SnapshotChanged -= OnSnapshotChanged;
                await _controller.StopAsync();
            }
            _graduatedCoreActuatorRuntime.StopAndRestore("Application shutdown restored the graduated core-floor baseline.");
            _powerModeProfileRuntime.Restore("Application shutdown restored the manual PowerFlow mode profile baseline.");
            if (_telemetryRecorder is not null)
            {
                _telemetryRecorder.ContinuityChanged -= OnTelemetryContinuityChanged;
                await _telemetryRecorder.DisposeAsync();
                _telemetryRecorder = null;
            }
        }
        finally
        {
            StopTrayHoverTimer();
            if (_trayHoverTimer is not null) { _trayHoverTimer.Tick -= OnTrayHoverTick; _trayHoverTimer = null; }
            if (_tray is not null) { _tray.CommandInvoked -= OnTrayCommandInvoked; _tray.InteractionRequested -= OnTrayInteractionRequested; _tray.Dispose(); _tray = null; }
            _games?.Dispose(); _games = null;
            Volatile.Write(ref _runtimeReady, 0);
            _dashboardOpenSignal?.Dispose(); _dashboardOpenSignal = null;
            _instanceGuard?.Dispose(); _instanceGuard = null;
            _controller = null;
            if (exitApplication) Exit();
        }
    }

    private static async Task WriteStartupFailureAsync(Exception ex)
    {
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PowerFlow");
            Directory.CreateDirectory(dir);
            await File.WriteAllTextAsync(Path.Combine(dir, "last-error.txt"), $"{DateTimeOffset.Now:O}\r\n{ex}");
        }
        catch { }
    }
}
