using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using PowerFlow.App.Controller;
using PowerFlow.Core.Policy;
using PowerFlow.Core.Rules;
using PowerFlow.Windows.Activity;
using Windows.Storage.Pickers;

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
    private double _graphWindowSeconds = 60;

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
        ApplyTheme(config.Theme);
        ViewModel.Configure(config);
        Root.DataContext = ViewModel;
        ViewModel.Update(controller.Snapshot, null);
        ApplyVisualState(controller.Snapshot);
        RulesPanel.Initialize(config, ApplyConfigFromPageAsync, BrowseExecutableAsync);
        SettingsPanel.Initialize(config, ApplyConfigFromPageAsync, () => _controller.ListPowerPlansAsync(), ApplyTheme);
        controller.SnapshotChanged += OnSnapshotChanged;
        _telemetrySession.TelemetryChanged += OnTelemetryChanged;
        TelemetryGraph.ThresholdsPreviewed += OnThresholdsPreviewed;
        TelemetryGraph.ThresholdsCommitted += OnThresholdsCommitted;
        Closed += OnClosed;
        AppWindow.Resize(new global::Windows.Graphics.SizeInt32(960, 620));
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
        TelemetryGraph.Apply(ViewModel.Samples, _config, snapshot.History, _graphWindowSeconds);
        DecisionPressure.Apply(_config, snapshot);
    });

    private void ApplyVisualState(ControllerSnapshot snapshot)
    {
        ApplyOverrideVisual(snapshot);
        RuleFlow.Apply(_config, snapshot);
        DecisionPressure.Apply(_config, snapshot);
        TelemetryGraph.Apply(ViewModel.Samples, _config, snapshot.History, _graphWindowSeconds);
    }

    private void ApplyOverrideVisual(ControllerSnapshot snapshot)
    {
        var manual = string.Equals(snapshot.LatchType, "Manual", StringComparison.OrdinalIgnoreCase);
        var game = string.Equals(snapshot.LatchType, "Game", StringComparison.OrdinalIgnoreCase);
        AutoOverrideButton.IsChecked = !manual;
        SaverOverrideButton.IsChecked = snapshot.State == PowerState.PowerSaver;
        BalancedOverrideButton.IsChecked = snapshot.State == PowerState.Balanced;
        PerformanceOverrideButton.IsChecked = snapshot.State == PowerState.HighPerformance;
        SaverOverrideButton.IsEnabled = !game;
        BalancedOverrideButton.IsEnabled = !game;
    }

    private async void OnAutoOverride(object sender, RoutedEventArgs e)
    {
        if (string.Equals(_controller.Snapshot.LatchType, "Manual", StringComparison.OrdinalIgnoreCase))
            await _controller.ReleaseManualLatchAsync();
        ApplyOverrideVisual(_controller.Snapshot);
    }

    private async void OnSaverOverride(object sender, RoutedEventArgs e) => await _controller.SetManualStateAsync(PowerState.PowerSaver);
    private async void OnBalancedOverride(object sender, RoutedEventArgs e) => await _controller.SetManualStateAsync(PowerState.Balanced);
    private async void OnPerformanceOverride(object sender, RoutedEventArgs e) => await _controller.SetManualStateAsync(PowerState.HighPerformance);

    private void OnRange60(object sender, RoutedEventArgs e)
    {
        _graphWindowSeconds = 60;
        Range60Button.IsChecked = true;
        Range120Button.IsChecked = false;
        ApplyVisualState(_controller.Snapshot);
    }

    private void OnRange120(object sender, RoutedEventArgs e)
    {
        _graphWindowSeconds = 120;
        Range60Button.IsChecked = false;
        Range120Button.IsChecked = true;
        ApplyVisualState(_controller.Snapshot);
    }

    private void OnThresholdsPreviewed(object? sender, ThresholdsChangedEventArgs e)
    {
        _config = _config with { QuietThresholdPercent = e.QuietPercent, CpuPromotionThresholdPercent = e.PromotionPercent };
        ViewModel.Configure(_config);
        RuleFlow.Apply(_config, _controller.Snapshot);
        DecisionPressure.Apply(_config, _controller.Snapshot);
    }

    private async void OnThresholdsCommitted(object? sender, ThresholdsChangedEventArgs e)
    {
        await ApplyConfigFromPageAsync(_config with { QuietThresholdPercent = e.QuietPercent, CpuPromotionThresholdPercent = e.PromotionPercent });
    }

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
        Root.RequestedTheme = theme switch
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
        TelemetryGraph.ThresholdsPreviewed -= OnThresholdsPreviewed;
        TelemetryGraph.ThresholdsCommitted -= OnThresholdsCommitted;
        await _telemetrySession.DisposeAsync();
    }
}
