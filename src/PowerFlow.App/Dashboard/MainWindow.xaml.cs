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
    private bool _sessionStarted;
    private bool _closed;

    public DashboardViewModel ViewModel { get; } = new();

    public MainWindow(PowerFlowController controller, PowerFlowConfig config, Func<PowerFlowConfig, Task> applyConfig, bool previewMode = false)
    {
        InitializeComponent();
        Title = previewMode ? "PowerFlow - Preview" : "PowerFlow";
        PreviewModeBadge.Visibility = previewMode ? Visibility.Visible : Visibility.Collapsed;
        _controller = controller;
        _config = config;
        _applyConfig = applyConfig;
        _dispatcher = DispatcherQueue.GetForCurrentThread();
        _telemetrySession = new DashboardTelemetrySession(() => new DashboardTelemetrySource(), new PeriodicControllerTickSourceFactory(), new SystemControllerClock());
        ViewModel.Configure(config);
        Root.DataContext = ViewModel;
        ViewModel.Update(controller.Snapshot, null);
        ApplyVisualState(controller.Snapshot);
        RulesPanel.Initialize(config, ApplyConfigFromPageAsync);
        SettingsPanel.Initialize(config, ApplyConfigFromPageAsync);
        controller.SnapshotChanged += OnSnapshotChanged;
        _telemetrySession.TelemetryChanged += OnTelemetryChanged;
        Closed += OnClosed;
        AppWindow.Resize(new global::Windows.Graphics.SizeInt32(1320, 840));
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
        SelectSection(showSettings ? "settings" : "flow");
        Activate();
    }

    private void OnSnapshotChanged(object? sender, ControllerSnapshot snapshot) => _dispatcher.TryEnqueue(() =>
    {
        ViewModel.Update(snapshot, _telemetrySession.Latest);
        ApplyVisualState(snapshot);
    });

    private void OnTelemetryChanged(object? sender, DashboardTelemetry telemetry) => _dispatcher.TryEnqueue(() =>
    {
        var snapshot = _controller.Snapshot;
        ViewModel.Update(snapshot, telemetry);
        TelemetryGraph.Apply(ViewModel.Samples, _config);
        DecisionPressure.Apply(_config, snapshot);
    });

    private void ApplyVisualState(ControllerSnapshot snapshot)
    {
        StateRail.Apply(snapshot);
        RuleFlow.Apply(_config, snapshot);
        DecisionPressure.Apply(_config, snapshot);
        TelemetryGraph.Apply(ViewModel.Samples, _config);
    }

    private async Task ApplyConfigFromPageAsync(PowerFlowConfig config)
    {
        await _applyConfig(config);
        _config = config;
        ViewModel.Configure(config);
        ApplyVisualState(_controller.Snapshot);
        RulesPanel.RefreshConfig(config);
        SettingsPanel.RefreshConfig(config);
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
