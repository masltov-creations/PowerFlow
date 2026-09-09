using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using PowerFlow.App.Controller;
using PowerFlow.App.Telemetry;
using PowerFlow.Core.Policy;
using PowerFlow.Core.Rules;
using PowerFlow.Windows.Activity;
using Windows.Storage.Pickers;

namespace PowerFlow.App.Dashboard;

public sealed partial class MainWindow : Window
{
    private readonly PowerFlowController _controller;
    private readonly Func<PowerFlowConfig, Task> _applyConfig;
    private readonly TelemetryContinuityRecorder _recorder;
    private TelemetryVisibilityLease? _visibilityLease;
    private readonly DispatcherQueue _dispatcher;
    private readonly bool _previewMode;
    private bool _explicitShutdown;
    private PowerFlowConfig _config;
    private bool _closed;
    private double _graphWindowSeconds = 60;

    public DashboardViewModel ViewModel { get; } = new();

    public MainWindow(PowerFlowController controller, TelemetryContinuityRecorder recorder, PowerFlowConfig config, Func<PowerFlowConfig, Task> applyConfig, bool previewMode = false)
    {
        InitializeComponent();
        Title = previewMode ? "PowerFlow - Preview" : "PowerFlow";
        PreviewModeBadge.Visibility = previewMode ? Visibility.Visible : Visibility.Collapsed;
        _controller = controller;
        _recorder = recorder;
        _previewMode = previewMode;
        _config = config;
        _applyConfig = applyConfig;
        _dispatcher = DispatcherQueue.GetForCurrentThread();
        ApplyTheme(config.Theme);
        ViewModel.Configure(config);
        Root.DataContext = ViewModel;
        ViewModel.UpdateContinuity(controller.Snapshot, recorder.History, recorder.LatestRichTelemetry);
        ApplyVisualState(controller.Snapshot);
        RulesPanel.Initialize(config, ApplyConfigFromPageAsync, BrowseExecutableAsync);
        SettingsPanel.Initialize(config, ApplyConfigFromPageAsync, () => _controller.ListPowerPlansAsync(), ApplyTheme);
        controller.SnapshotChanged += OnSnapshotChanged;
        _recorder.ContinuityChanged += OnContinuityChanged;
        Trajectory.AutoRequested += OnTrajectoryAutoRequested;
        Trajectory.ManualStateRequested += OnTrajectoryManualStateRequested;
        Trajectory.RangeChanged += OnTrajectoryRangeChanged;
        Trajectory.ThresholdsPreviewed += OnThresholdsPreviewed;
        Trajectory.ThresholdsCommitted += OnThresholdsCommitted;
        AppWindow.Closing += OnAppWindowClosing;
        Closed += OnClosed;
        AppWindow.Resize(new global::Windows.Graphics.SizeInt32(960, 620));
        try { SystemBackdrop = new MicaBackdrop(); } catch { }
        SelectSection("flow");
    }

    public Task ShowAsync(bool showSettings = false)
    {
        _visibilityLease ??= _recorder.AcquireVisibility();
        ViewModel.UpdateContinuity(_controller.Snapshot, _recorder.History, _recorder.LatestRichTelemetry);
        ApplyVisualState(_controller.Snapshot);
        SelectSection(showSettings ? "settings" : "flow");
        Activate();
        return Task.CompletedTask;
    }

    public void CloseForShutdown()
    {
        _explicitShutdown = true;
        Close();
    }

    private void OnAppWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (DashboardClosePolicy.Decide(_previewMode, _explicitShutdown) == DashboardCloseDisposition.Close) return;
        args.Cancel = true;
        AppWindow.Hide();
        ReleaseDashboardVisibility();
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
        var model = TrajectoryProjection.Create(_recorder.History, snapshot, _config);
        Trajectory.Apply(model, ViewModel.Samples, _config, snapshot.History, _graphWindowSeconds);
    }

    private async void OnTrajectoryAutoRequested(object? sender, EventArgs e)
    {
        if (string.Equals(_controller.Snapshot.LatchType, "Manual", StringComparison.OrdinalIgnoreCase))
            await _controller.ReleaseManualLatchAsync();
        ApplyVisualState(_controller.Snapshot);
    }

    private async void OnTrajectoryManualStateRequested(object? sender, TrajectoryManualStateEventArgs e) => await _controller.SetManualStateAsync(e.State);

    private void OnTrajectoryRangeChanged(object? sender, TrajectoryRangeChangedEventArgs e)
    {
        _graphWindowSeconds = e.Seconds;
        ApplyVisualState(_controller.Snapshot);
    }

    private void OnThresholdsPreviewed(object? sender, ThresholdsChangedEventArgs e)
    {
        _config = _config with { QuietThresholdPercent = e.QuietPercent, CpuPromotionThresholdPercent = e.PromotionPercent };
        ViewModel.Configure(_config);
        ApplyVisualState(_controller.Snapshot);
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

    private void OnClosed(object sender, WindowEventArgs args)
    {
        if (_closed) return;
        _closed = true;
        AppWindow.Closing -= OnAppWindowClosing;
        _controller.SnapshotChanged -= OnSnapshotChanged;
        _recorder.ContinuityChanged -= OnContinuityChanged;
        Trajectory.AutoRequested -= OnTrajectoryAutoRequested;
        Trajectory.ManualStateRequested -= OnTrajectoryManualStateRequested;
        Trajectory.RangeChanged -= OnTrajectoryRangeChanged;
        Trajectory.ThresholdsPreviewed -= OnThresholdsPreviewed;
        Trajectory.ThresholdsCommitted -= OnThresholdsCommitted;
        ReleaseDashboardVisibility();
    }
}
