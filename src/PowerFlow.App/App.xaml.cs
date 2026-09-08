using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using PowerFlow.App.Controller;
using PowerFlow.App.Dashboard;
using PowerFlow.App.Tray;
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
    private PowerFlowController? _controller;
    private GameLifecycleMonitor? _games;
    private TrayIconHost? _tray;
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
        if (!_instanceGuard.IsPrimary) { Exit(); return; }
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
            _controller.SnapshotChanged += OnSnapshotChanged;
            await _controller.StartAsync();
            if (LaunchIntent.ShouldCreateTray(launchArgs))
            {
                _tray = new TrayIconHost(_controller.Snapshot);
                _tray.CommandInvoked += OnTrayCommandInvoked;
            }
            if (LaunchIntent.ShouldOpenDashboard(launchArgs)) await OpenDashboardAsync(false);
        }
        catch (Exception ex)
        {
            await WriteStartupFailureAsync(ex);
            await ShutdownAsync(true);
        }
    }

    private void OnSnapshotChanged(object? sender, ControllerSnapshot snapshot) => _dispatcher?.TryEnqueue(() => _tray?.Update(snapshot));

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

    private async Task OpenDashboardAsync(bool showSettings)
    {
        if (_controller is null) return;
        if (_dashboardWindow is null)
        {
            _dashboardWindow = new MainWindow(_controller, _config, ApplyConfigAsync, _previewMode);
            _dashboardWindow.Closed += async (_, _) =>
            {
                _dashboardWindow = null;
                if (_previewMode) await ShutdownAsync(true);
            };
        }
        await _dashboardWindow.ShowAsync(showSettings);
    }

    private async Task ApplyConfigAsync(PowerFlowConfig updated)
    {
        if (_configStore is null) return;
        if (!_previewMode) await _configStore.SaveAsync(updated);
        if (_controller is not null) await _controller.UpdatePolicyConfigAsync(updated);
        _config = updated;
        _games?.UpdateRules(updated.AppRules);
        if (!_previewMode) _startupRegistration?.SetEnabled(updated.StartWithWindows);
    }

    private async Task ShutdownAsync(bool exitApplication)
    {
        if (_shuttingDown) return;
        _shuttingDown = true;
        try
        {
            _dashboardWindow?.Close();
            _dashboardWindow = null;
            if (_controller is not null)
            {
                _controller.SnapshotChanged -= OnSnapshotChanged;
                await _controller.StopAsync();
            }
        }
        finally
        {
            if (_tray is not null) { _tray.CommandInvoked -= OnTrayCommandInvoked; _tray.Dispose(); _tray = null; }
            _games?.Dispose(); _games = null;
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
