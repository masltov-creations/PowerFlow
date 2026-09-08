using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using PowerFlow.App.Controller;
using PowerFlow.App.Tray;
using PowerFlow.Core.Policy;
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
    private Window? _dashboardWindow;
    private TextBlock? _dashboardStatus;
    private TextBlock? _dashboardReason;
    private bool _shuttingDown;

    public App() => InitializeComponent();

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        _dispatcher = DispatcherQueue.GetForCurrentThread();
        _instanceGuard = SingleInstanceGuard.TryAcquire(@"Local\PowerFlow.Controller.v1");
        if (!_instanceGuard.IsPrimary)
        {
            Exit();
            return;
        }

        try
        {
            var dataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PowerFlow");
            var configStore = new JsonConfigStore(dataDirectory);
            var config = await configStore.LoadAsync();

            _games = new GameLifecycleMonitor();
            _controller = new PowerFlowController(
                config,
                new WindowsPowerPlanController(),
                new SystemTimesActivitySource(),
                _games,
                new PeriodicControllerTickSourceFactory(),
                new SystemControllerDelay(),
                new SystemControllerClock());

            _controller.SnapshotChanged += OnSnapshotChanged;
            await _controller.StartAsync();

            _tray = new TrayIconHost(_controller.Snapshot);
            _tray.CommandInvoked += OnTrayCommandInvoked;
        }
        catch (Exception ex)
        {
            await WriteStartupFailureAsync(ex);
            await ShutdownAsync(exitApplication: true);
        }
    }

    private void OnSnapshotChanged(object? sender, ControllerSnapshot snapshot)
    {
        _dispatcher?.TryEnqueue(() =>
        {
            _tray?.Update(snapshot);
            UpdateDashboard(snapshot);
        });
    }

    private async void OnTrayCommandInvoked(object? sender, TrayIconHost.TrayCommandInvokedEventArgs e)
    {
        if (_controller is null || _shuttingDown) return;
        try
        {
            switch (e.CommandId)
            {
                case TrayMenuCommands.OpenDashboard:
                    OpenDashboard();
                    break;
                case TrayMenuCommands.PowerSaver:
                    await _controller.SetManualStateAsync(PowerState.PowerSaver);
                    break;
                case TrayMenuCommands.Balanced:
                    await _controller.SetManualStateAsync(PowerState.Balanced);
                    break;
                case TrayMenuCommands.HighPerformance:
                    await _controller.SetManualStateAsync(PowerState.HighPerformance);
                    break;
                case TrayMenuCommands.ReleaseLatch:
                    if (string.Equals(_controller.Snapshot.LatchType, "Manual", StringComparison.OrdinalIgnoreCase))
                        await _controller.ReleaseManualLatchAsync();
                    break;
                case TrayMenuCommands.Settings:
                    OpenDashboard(showSettingsHint: true);
                    break;
                case TrayMenuCommands.Exit:
                    await ShutdownAsync(exitApplication: true);
                    break;
            }
        }
        catch (Exception ex)
        {
            await WriteStartupFailureAsync(ex);
        }
    }

    private void OpenDashboard(bool showSettingsHint = false)
    {
        if (_controller is null) return;
        if (_dashboardWindow is null)
        {
            _dashboardStatus = new TextBlock
            {
                FontSize = 28,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 8)
            };
            _dashboardReason = new TextBlock
            {
                FontSize = 14,
                Opacity = 0.72,
                TextWrapping = TextWrapping.Wrap
            };
            var stack = new StackPanel
            {
                Spacing = 8,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Center
            };
            stack.Children.Add(new TextBlock
            {
                Text = "POWERFLOW",
                FontSize = 12,
                CharacterSpacing = 220,
                Opacity = 0.56
            });
            stack.Children.Add(_dashboardStatus);
            stack.Children.Add(_dashboardReason);
            stack.Children.Add(new TextBlock
            {
                Text = "The full motion-flow dashboard is being layered onto this shell next.",
                FontSize = 12,
                Margin = new Thickness(0, 18, 0, 0),
                Opacity = 0.45
            });

            var root = new Grid
            {
                Padding = new Thickness(40),
                Background = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 11, 13, 18))
            };
            root.Children.Add(stack);

            _dashboardWindow = new Window
            {
                Title = "PowerFlow",
                Content = root
            };
            _dashboardWindow.AppWindow.Resize(new global::Windows.Graphics.SizeInt32(900, 560));
            _dashboardWindow.Closed += (_, _) =>
            {
                _dashboardWindow = null;
                _dashboardStatus = null;
                _dashboardReason = null;
            };
        }

        UpdateDashboard(_controller.Snapshot, showSettingsHint);
        _dashboardWindow.Activate();
    }

    private void UpdateDashboard(ControllerSnapshot snapshot, bool showSettingsHint = false)
    {
        if (_dashboardStatus is null || _dashboardReason is null) return;
        _dashboardStatus.Text = snapshot.IsLatched
            ? $"{snapshot.State} · {snapshot.LatchType} locked"
            : snapshot.State.ToString();
        _dashboardReason.Text = showSettingsHint
            ? $"Settings · {snapshot.Reason}"
            : snapshot.Reason;
    }

    private async Task ShutdownAsync(bool exitApplication)
    {
        if (_shuttingDown) return;
        _shuttingDown = true;
        try
        {
            if (_controller is not null)
            {
                _controller.SnapshotChanged -= OnSnapshotChanged;
                await _controller.StopAsync();
            }
        }
        finally
        {
            if (_tray is not null)
            {
                _tray.CommandInvoked -= OnTrayCommandInvoked;
                _tray.Dispose();
                _tray = null;
            }
            _games?.Dispose();
            _games = null;
            _instanceGuard?.Dispose();
            _instanceGuard = null;
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

