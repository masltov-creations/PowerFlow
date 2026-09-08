using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using PowerFlow.App.Controller;
using PowerFlow.Core.Rules;
using PowerFlow.Windows.Activity;

namespace PowerFlow.App.Dashboard;

public sealed partial class MainWindow : Window
{
    private readonly PowerFlowController _controller;
    private readonly Func<PowerFlowConfig, Task> _applyConfig;
    private readonly DashboardTelemetrySession _telemetrySession;
    private readonly DispatcherQueue _dispatcher;
    private PowerFlowConfig _config;
    private bool _reducedMotion;
    private bool _sessionStarted;
    private bool _closed;

    public DashboardViewModel ViewModel { get; } = new();

    public MainWindow(PowerFlowController controller, PowerFlowConfig config, Func<PowerFlowConfig, Task> applyConfig, bool previewMode = false)
    {
        InitializeComponent();
        Title = previewMode ? "PowerFlow — Preview" : "PowerFlow";
        PreviewModeBadge.Visibility = previewMode ? Visibility.Visible : Visibility.Collapsed;
        _controller = controller;
        _config = config;
        _applyConfig = applyConfig;
        _dispatcher = DispatcherQueue.GetForCurrentThread();
        _telemetrySession = new DashboardTelemetrySession(() => new DashboardTelemetrySource(), new PeriodicControllerTickSourceFactory(), new SystemControllerClock());
        _reducedMotion = ResolveReducedMotion(config);
        Root.DataContext = ViewModel;
        ViewModel.Update(controller.Snapshot, null);
        FlowField.ApplySnapshot(controller.Snapshot, _reducedMotion);
        RulesPanel.Initialize(config, ApplyConfigFromPageAsync);
        SettingsPanel.Initialize(config, ApplyConfigFromPageAsync);
        controller.SnapshotChanged += OnSnapshotChanged;
        _telemetrySession.TelemetryChanged += OnTelemetryChanged;
        Closed += OnClosed;
        AppWindow.Resize(new global::Windows.Graphics.SizeInt32(1180, 760));
        try { SystemBackdrop = new MicaBackdrop(); } catch { }
        SelectSection("flow");
    }

    public async Task ShowAsync(bool showSettings = false)
    {
        if (!_sessionStarted)
        {
            _sessionStarted = true;
            await _telemetrySession.StartAsync();
        }
        if (showSettings) SelectSection("settings");
        else SelectSection("flow");
        Activate();
    }

    private void OnSnapshotChanged(object? sender, ControllerSnapshot snapshot) => _dispatcher.TryEnqueue(() =>
    {
        ViewModel.Update(snapshot, _telemetrySession.Latest);
        FlowField.ApplySnapshot(snapshot, _reducedMotion);
    });

    private void OnTelemetryChanged(object? sender, DashboardTelemetry telemetry) => _dispatcher.TryEnqueue(() => ViewModel.Update(_controller.Snapshot, telemetry));

    private async Task ApplyConfigFromPageAsync(PowerFlowConfig config)
    {
        await _applyConfig(config);
        _config = config;
        _reducedMotion = ResolveReducedMotion(config);
        FlowField.ApplySnapshot(_controller.Snapshot, _reducedMotion);
        RulesPanel.RefreshConfig(config);
        SettingsPanel.RefreshConfig(config);
    }

    private static bool ResolveReducedMotion(PowerFlowConfig config)
    {
        if (config.ReducedMotionOverride is bool forced) return forced;
        try { return !new global::Windows.UI.ViewManagement.UISettings().AnimationsEnabled; }
        catch { return false; }
    }

    private void OnNavigationChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        var tag = (args.SelectedItemContainer?.Tag as string) ?? "flow";
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

    private async void OnClosed(object sender, WindowEventArgs args)
    {
        if (_closed) return;
        _closed = true;
        _controller.SnapshotChanged -= OnSnapshotChanged;
        _telemetrySession.TelemetryChanged -= OnTelemetryChanged;
        await _telemetrySession.DisposeAsync();
    }
}
