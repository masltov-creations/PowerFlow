using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using PowerFlow.App.Controller;
using PowerFlow.App.Dashboard;
using PowerFlow.App.Tray;
using PowerFlow.App.Telemetry;
using PowerFlow.App.Startup;
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
    private SingleInstanceGuard? _instanceGuard;
    private const string DashboardOpenSignalName = @"Local\PowerFlow.OpenDashboard.v1";
    private SingleInstanceSignal? _dashboardOpenSignal;
    private int _dashboardOpenRequested;
    private int _dashboardFullScreenRequested;
    private int _runtimeReady;
    private PowerFlowController? _controller;
    private TelemetryContinuityRecorder? _telemetryRecorder;
    private GameLifecycleMonitor? _games;
    private TrayIconHost? _tray;
    private TrayHoverWindow? _trayHoverWindow;
    private DispatcherQueueTimer? _trayHoverTimer;
    private readonly TrayHoverPolicy _trayHoverPolicy = new(TimeSpan.FromMilliseconds(350));
    private DispatcherQueue? _dispatcher;
    private MainWindow? _dashboardWindow;
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
            _telemetryRecorder = new TelemetryContinuityRecorder(() => new DashboardTelemetrySource(), new PeriodicControllerTickSourceFactory(), new SystemControllerClock());
            _controller.SnapshotChanged += OnSnapshotChanged;
            await _controller.StartAsync();
            await _telemetryRecorder.StartAsync();
            if (LaunchIntent.ShouldCreateTray(launchArgs))
            {
                _tray = new TrayIconHost(_controller.Snapshot);
                _tray.CommandInvoked += OnTrayCommandInvoked;
                _tray.HoverActivity += OnTrayHoverActivity;
            }
            if (LaunchIntent.ShouldOpenDashboard(launchArgs)) Interlocked.Exchange(ref _dashboardOpenRequested, 1);
            if (LaunchIntent.ShouldOpenFullScreen(launchArgs)) Interlocked.Exchange(ref _dashboardFullScreenRequested, 1);
            Volatile.Write(ref _runtimeReady, 1);
            if (LaunchIntent.ShouldOpenPopupPreview(launchArgs))
            {
                await ShowTrayHoverPreviewAsync();
            }
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
        _dispatcher?.TryEnqueue(() => _tray?.Update(snapshot));
    }


    private void OnTrayHoverActivity(object? sender, EventArgs e)
    {
        _dispatcher?.TryEnqueue(() =>
        {
            if (_shuttingDown || _tray is null) return;
            _trayHoverPolicy.BeginHover(DateTimeOffset.UtcNow);
            EnsureTrayHoverTimer();
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
        var overPopup = _trayHoverWindow?.ContainsCursor() == true;
        var action = _trayHoverPolicy.Evaluate(DateTimeOffset.UtcNow, overIcon, overPopup);
        switch (action)
        {
            case TrayHoverAction.Show:
                await ShowTrayHoverAsync();
                break;
            case TrayHoverAction.Hide:
            case TrayHoverAction.Cancel:
                await HideTrayHoverAsync();
                StopTrayHoverTimer();
                break;
        }
    }

    private async Task ShowTrayHoverPreviewAsync()
    {
        if (_controller is null || _tray is null || _telemetryRecorder is null) return;
        for (var attempt = 0; attempt < 20; attempt++)
        {
            if (_tray.TryGetIconRect(out var iconRect) && _tray.TryGetWorkArea(iconRect, out var workArea))
            {
                if (_trayHoverWindow is null)
                {
                    _trayHoverWindow = new TrayHoverWindow(_controller, _telemetryRecorder, _config);
                    _trayHoverWindow.OpenDashboardRequested += OnTrayHoverOpenDashboardRequested;
                }
                _trayHoverWindow.UpdateConfig(_config);
                await _trayHoverWindow.ShowAsync(iconRect, workArea);
                return;
            }
            await Task.Delay(250);
        }
        throw new InvalidOperationException("Tray icon rectangle did not become available for popup preview.");
    }
    private async Task ShowTrayHoverAsync()
    {
        if (_controller is null || _tray is null) return;
        if (!_tray.TryGetHoverAnchorRect(out var iconRect) || !_tray.TryGetWorkArea(iconRect, out var workArea)) return;
        if (_trayHoverWindow is null)
        {
            if (_telemetryRecorder is null) return;
            _trayHoverWindow = new TrayHoverWindow(_controller, _telemetryRecorder, _config);
            _trayHoverWindow.OpenDashboardRequested += OnTrayHoverOpenDashboardRequested;
        }
        _trayHoverWindow.UpdateConfig(_config);
        await _trayHoverWindow.ShowAsync(iconRect, workArea);
    }

    private async Task HideTrayHoverAsync()
    {
        if (_trayHoverWindow is not null) await _trayHoverWindow.HideAsync();
    }

    private async void OnTrayHoverOpenDashboardRequested(object? sender, EventArgs e)
    {
        await HideTrayHoverAsync();
        StopTrayHoverTimer();
        await OpenDashboardAsync(false);
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
                case TrayMenuCommands.PowerSaver: await _controller.SetManualStateAsync(PowerState.PowerSaver); break;
                case TrayMenuCommands.Balanced: await _controller.SetManualStateAsync(PowerState.Balanced); break;
                case TrayMenuCommands.HighPerformance: await _controller.SetManualStateAsync(PowerState.HighPerformance); break;
                case TrayMenuCommands.ReleaseLatch:
                    if (string.Equals(_controller.Snapshot.LatchType, "Manual", StringComparison.OrdinalIgnoreCase)) await _controller.ReleaseManualLatchAsync();
                    break;
                case TrayMenuCommands.Settings: await OpenDashboardAsync(true); break;
                case TrayMenuCommands.Exit: await ShutdownAsync(true); break;
            }
        }
        catch (Exception ex) { await WriteStartupFailureAsync(ex); }
    }

    private async Task OpenDashboardAsync(bool showSettings, bool fullScreen = false)
    {
        if (_controller is null) return;
        if (_dashboardWindow is null)
        {
            if (_telemetryRecorder is null) return;
            _dashboardWindow = new MainWindow(_controller, _telemetryRecorder, _config, ApplyConfigAsync, _previewMode);
            _dashboardWindow.Closed += async (_, _) =>
            {
                _dashboardWindow = null;
                if (_previewMode) await ShutdownAsync(true);
            };
        }
        await _dashboardWindow.ShowAsync(showSettings, fullScreen);
    }

    private async Task ApplyConfigAsync(PowerFlowConfig updated)
    {
        if (_configStore is null) return;
        if (!_previewMode) await _configStore.SaveAsync(updated);
        if (_controller is not null) await _controller.UpdatePolicyConfigAsync(updated);
        _config = updated;
        _trayHoverWindow?.UpdateConfig(updated);
        _games?.UpdateRules(updated.AppRules);
        if (!_previewMode) _startupRegistration?.SetEnabled(updated.StartWithWindows);
    }

    private async Task ShutdownAsync(bool exitApplication)
    {
        if (_shuttingDown) return;
        _shuttingDown = true;
        try
        {
            _dashboardWindow?.CloseForShutdown();
            _dashboardWindow = null;
            if (_controller is not null)
            {
                _controller.SnapshotChanged -= OnSnapshotChanged;
                await _controller.StopAsync();
            }
            if (_telemetryRecorder is not null)
            {
                await _telemetryRecorder.DisposeAsync();
                _telemetryRecorder = null;
            }
        }
        finally
        {
            StopTrayHoverTimer();
            if (_trayHoverTimer is not null) { _trayHoverTimer.Tick -= OnTrayHoverTick; _trayHoverTimer = null; }
            if (_trayHoverWindow is not null) { _trayHoverWindow.OpenDashboardRequested -= OnTrayHoverOpenDashboardRequested; await _trayHoverWindow.DisposeAsync(); _trayHoverWindow = null; }
            if (_tray is not null) { _tray.CommandInvoked -= OnTrayCommandInvoked; _tray.HoverActivity -= OnTrayHoverActivity; _tray.Dispose(); _tray = null; }
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
