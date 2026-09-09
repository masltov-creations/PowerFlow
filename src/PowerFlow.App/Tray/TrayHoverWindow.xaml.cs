using System.Numerics;
using System.Runtime.InteropServices;
using Microsoft.UI.Composition;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Shapes;
using XamlPath = Microsoft.UI.Xaml.Shapes.Path;
using PowerFlow.App.Controller;
using PowerFlow.App.Dashboard;
using PowerFlow.App.Telemetry;
using PowerFlow.Core.Policy;
using PowerFlow.Core.Rules;
using PowerFlow.Windows.Activity;
using Windows.Foundation;
using Windows.Graphics;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace PowerFlow.App.Tray;

public sealed partial class TrayHoverWindow : Window, IAsyncDisposable
{
    private const int GWL_EXSTYLE = -20;
    private const long WS_EX_TOOLWINDOW = 0x00000080L;
    private const long WS_EX_NOACTIVATE = 0x08000000L;
    private const int SW_HIDE = 0;
    private const int SW_SHOWNOACTIVATE = 4;
    private const int PopupWidth = 380;
    private const int PopupHeight = 236;

    private readonly PowerFlowController _controller;
    private readonly TelemetryContinuityRecorder _recorder;
    private TelemetryVisibilityLease? _visibilityLease;
    private readonly DispatcherQueue _dispatcher;
    private readonly IntPtr _hwnd;
    private readonly List<(DateTimeOffset At, double Cpu)> _samples = [];
    private PowerFlowConfig _config;
    private DashboardTelemetry? _telemetry;
    private bool _disposed;

    public TrayHoverWindow(PowerFlowController controller, TelemetryContinuityRecorder recorder, PowerFlowConfig config)
    {
        InitializeComponent();
        _controller = controller;
        _recorder = recorder;
        _config = config;
        _dispatcher = DispatcherQueue.GetForCurrentThread();
        _recorder.ContinuityChanged += OnContinuityChanged;
        _controller.SnapshotChanged += OnSnapshotChanged;
        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        ApplyNonActivatingToolWindowStyle();
        ConfigurePresenter();
        ApplyTheme();
        UpdateUi(_controller.Snapshot);
        Root.Opacity = 0;
    }

    public event EventHandler? OpenDashboardRequested;
    public bool IsVisible { get; private set; }

    public TrayRect PopupBounds
    {
        get
        {
            var p = AppWindow.Position;
            var s = AppWindow.Size;
            return new TrayRect(p.X, p.Y, p.X + s.Width, p.Y + s.Height);
        }
    }

    public void UpdateConfig(PowerFlowConfig config)
    {
        _config = config;
        ApplyTheme();
        UpdateUi(_controller.Snapshot);
    }

    public Task ShowAsync(TrayRect iconRect, TrayRect workArea)
    {
        if (_disposed) return Task.CompletedTask;
        var placement = TrayPopupPlacement.AboveIcon(iconRect, workArea, PopupWidth, PopupHeight, 10);
        AppWindow.MoveAndResize(new RectInt32(placement.Left, placement.Top, PopupWidth, PopupHeight));
        _telemetry = _recorder.LatestRichTelemetry;
        UpdateUi(_controller.Snapshot);
        _visibilityLease ??= _recorder.AcquireVisibility();
        ShowWindow(_hwnd, SW_SHOWNOACTIVATE);
        IsVisible = true;
        AnimateIn();
        return Task.CompletedTask;
    }

    public Task HideAsync()
    {
        if (_disposed || !IsVisible) return Task.CompletedTask;
        IsVisible = false;
        ShowWindow(_hwnd, SW_HIDE);
        ReleaseVisibility();
        return Task.CompletedTask;
    }

    public bool ContainsCursor()
    {
        if (!IsVisible || !GetCursorPos(out var point)) return false;
        return PopupBounds.Contains(point.X, point.Y);
    }

    private void ConfigurePresenter()
    {
        AppWindow.IsShownInSwitchers = false;
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.SetBorderAndTitleBar(false, false);
        }
        try { SystemBackdrop = new MicaBackdrop(); } catch { }
    }

    private void ApplyNonActivatingToolWindowStyle()
    {
        var style = GetWindowLongPtr(_hwnd, GWL_EXSTYLE).ToInt64();
        SetWindowLongPtr(_hwnd, GWL_EXSTYLE, new IntPtr(style | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE));
    }

    private void ApplyTheme()
    {
        Root.RequestedTheme = _config.Theme switch
        {
            ThemePreference.Light => ElementTheme.Light,
            ThemePreference.Dark => ElementTheme.Dark,
            _ => ElementTheme.Default
        };
    }

    private void OnSnapshotChanged(object? sender, ControllerSnapshot snapshot) => _dispatcher.TryEnqueue(() => UpdateUi(snapshot));

    private void OnContinuityChanged(object? sender, EventArgs e) => _dispatcher.TryEnqueue(() =>
    {
        if (!IsVisible) return;
        _telemetry = _recorder.LatestRichTelemetry;
        _samples.Clear();
        foreach (var sample in _recorder.History.TakeLast(30)) _samples.Add((sample.At, sample.CpuPercent));
        UpdateUi(_controller.Snapshot);
        DrawSparkline();
    });

    private void ReleaseVisibility()
    {
        _visibilityLease?.Dispose();
        _visibilityLease = null;
    }
    private void UpdateUi(ControllerSnapshot snapshot)
    {
        StateText.Text = snapshot.State switch
        {
            PowerState.PowerSaver => "POWER SAVER",
            PowerState.Balanced => "BALANCED",
            PowerState.HighPerformance => "PERFORMANCE",
            _ => snapshot.State.ToString().ToUpperInvariant()
        };
        ModeBadge.Text = snapshot.LatchType switch
        {
            "Manual" => "MANUAL LOCK",
            "Game" => "GAME LOCK",
            _ => "AUTO"
        };
        CpuText.Text = $"{snapshot.CpuPercent:0.0}%";
        WattsText.Text = _telemetry?.PackageWatts is double watts ? $"{watts:0.0} W" : "—";
        FrequencyText.Text = _telemetry?.AverageMhz is double mhz ? $"{mhz / 1000d:0.00}" : "—";
        NextAction.Text = snapshot switch
        {
            { IsLatched: true, LatchType: "Manual" } => $"{StateText.Text} held until AUTO",
            { IsLatched: true, LatchType: "Game" } => "Held until tracked game exits",
            { State: PowerState.PowerSaver } => $"Balanced above {_config.CpuPromotionThresholdPercent:0.#}% for {_config.CpuPromotionWindow.TotalSeconds:0.#}s",
            { State: PowerState.Balanced } => $"Saver below {_config.QuietThresholdPercent:0.#}% for {_config.QuietWindow.TotalSeconds:0.#}s",
            _ => "Open PowerFlow for policy details"
        };
    }

    private void AnimateIn()
    {
        Root.Opacity = 1;
        var windowsAnimationsEnabled = true;
        try { windowsAnimationsEnabled = new UISettings().AnimationsEnabled; } catch { }
        if (!TrayMotionPreference.ShouldAnimate(_config.ReducedMotionOverride, windowsAnimationsEnabled)) return;
        var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(Root);
        var compositor = visual.Compositor;
        var opacity = compositor.CreateScalarKeyFrameAnimation();
        opacity.Duration = TimeSpan.FromMilliseconds(150);
        opacity.InsertKeyFrame(0, 0.15f);
        opacity.InsertKeyFrame(1, 1f);
        visual.StartAnimation("Opacity", opacity);
        var offset = compositor.CreateVector3KeyFrameAnimation();
        offset.Duration = TimeSpan.FromMilliseconds(170);
        offset.InsertKeyFrame(0, new Vector3(0, 8, 0));
        offset.InsertKeyFrame(1, Vector3.Zero);
        visual.StartAnimation("Offset", offset);
    }

    private void OnSparklineSizeChanged(object sender, SizeChangedEventArgs e) => DrawSparkline();

    private void DrawSparkline()
    {
        TraySparkline.Children.Clear();
        var width = TraySparkline.ActualWidth;
        var height = TraySparkline.ActualHeight;
        if (width <= 0 || height <= 0 || _samples.Count < 2) return;

        var points = _samples.Select((sample, i) => new Point(
            i * width / Math.Max(1, _samples.Count - 1),
            height - (Math.Clamp(sample.Cpu, 0, 100) / 100d * (height - 6)) - 3)).ToArray();

        var figure = new PathFigure { StartPoint = points[0] };
        for (var i = 1; i < points.Length; i++)
        {
            var a = points[i - 1];
            var b = points[i];
            var dx = (b.X - a.X) / 3d;
            figure.Segments.Add(new BezierSegment
            {
                Point1 = new Point(a.X + dx, a.Y),
                Point2 = new Point(b.X - dx, b.Y),
                Point3 = b
            });
        }
        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        TraySparkline.Children.Add(new XamlPath
        {
            Data = geometry,
            Stroke = new SolidColorBrush(Color.FromArgb(255, 65, 214, 197)),
            StrokeThickness = 2.2,
            StrokeLineJoin = PenLineJoin.Round,
            Opacity = 0.95
        });

        var end = points[^1];
        var dot = new Ellipse
        {
            Width = 7,
            Height = 7,
            Fill = new SolidColorBrush(Color.FromArgb(255, 65, 214, 197))
        };
        Canvas.SetLeft(dot, end.X - 3.5);
        Canvas.SetTop(dot, end.Y - 3.5);
        TraySparkline.Children.Add(dot);
    }

    private void OnRootTapped(object sender, TappedRoutedEventArgs e) => OpenDashboardRequested?.Invoke(this, EventArgs.Empty);

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _controller.SnapshotChanged -= OnSnapshotChanged;
        _recorder.ContinuityChanged -= OnContinuityChanged;
        if (IsVisible) await HideAsync();
        _disposed = true;
        Close();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PointNative { public int X; public int Y; }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ShowWindow(IntPtr hwnd, int command);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetCursorPos(out PointNative point);
}
