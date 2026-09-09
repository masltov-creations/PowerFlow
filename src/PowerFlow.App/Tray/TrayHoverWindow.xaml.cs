using System.Numerics;
using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using XamlPath = Microsoft.UI.Xaml.Shapes.Path;
using PowerFlow.App.Controller;
using PowerFlow.App.Dashboard;
using PowerFlow.App.Telemetry;
using PowerFlow.Core.Policy;
using PowerFlow.Core.Rules;
using Windows.Foundation;
using Windows.Graphics;
using Windows.UI.ViewManagement;

namespace PowerFlow.App.Tray;

public sealed partial class TrayHoverWindow : Window, IAsyncDisposable
{
    private const int GWL_EXSTYLE = -20;
    private const long WS_EX_TOOLWINDOW = 0x00000080L;
    private const long WS_EX_NOACTIVATE = 0x08000000L;
    private const int SW_HIDE = 0;
    private const int SW_SHOWNOACTIVATE = 4;
    private const int PopupWidth = 400;
    private const int PopupHeight = 260;

    private readonly PowerFlowController _controller;
    private readonly TelemetryContinuityRecorder _recorder;
    private TelemetryVisibilityLease? _visibilityLease;
    private readonly DispatcherQueue _dispatcher;
    private readonly IntPtr _hwnd;
    private PowerFlowConfig _config;
    private TrayMiniTrajectoryModel? _model;
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
        Root.ActualThemeChanged += (_, _) => DrawTrajectory();
        Refresh(_controller.Snapshot);
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
        Refresh(_controller.Snapshot);
    }

    public Task ShowAsync(TrayRect iconRect, TrayRect workArea)
    {
        if (_disposed) return Task.CompletedTask;
        var placement = TrayPopupPlacement.AboveIcon(iconRect, workArea, PopupWidth, PopupHeight, 10);
        AppWindow.MoveAndResize(new RectInt32(placement.Left, placement.Top, PopupWidth, PopupHeight));
        Refresh(_controller.Snapshot);
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

    private void OnSnapshotChanged(object? sender, ControllerSnapshot snapshot) => _dispatcher.TryEnqueue(() => Refresh(snapshot));

    private void OnContinuityChanged(object? sender, EventArgs e) => _dispatcher.TryEnqueue(() =>
    {
        if (IsVisible) Refresh(_controller.Snapshot);
    });

    private void Refresh(ControllerSnapshot snapshot)
    {
        _model = TrayMiniTrajectoryProjection.Create(_recorder.History, snapshot, _config, _recorder.LatestRichTelemetry);
        StateText.Text = _model.StateLabel;
        ModeBadge.Text = _model.ModeBadge;
        CpuText.Text = _model.CpuLabel;
        WattsText.Text = _model.WattsLabel;
        FrequencyText.Text = _model.FrequencyLabel;
        DirectionText.Text = _model.DirectionLabel;
        NextAction.Text = _model.NextAction;

        SaverNode.Opacity = snapshot.State == PowerState.PowerSaver ? 1 : 0.42;
        BalancedNode.Opacity = snapshot.State == PowerState.Balanced ? 1 : 0.42;
        PerformanceNode.Opacity = snapshot.State == PowerState.HighPerformance ? 1 : 0.42;
        DrawTrajectory();
    }

    private void ReleaseVisibility()
    {
        _visibilityLease?.Dispose();
        _visibilityLease = null;
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

    private void OnTrajectorySizeChanged(object sender, SizeChangedEventArgs e) => DrawTrajectory();

    private void DrawTrajectory()
    {
        TrayTrajectoryCanvas.Children.Clear();
        if (_model is null) return;
        var width = TrayTrajectoryCanvas.ActualWidth;
        var height = TrayTrajectoryCanvas.ActualHeight;
        if (width <= 1 || height <= 1) return;

        var samples = _model.Samples;
        var plotHeight = Math.Max(20, height - 13);
        var bandTop = Math.Max(0, height - 9);
        var cpuBrush = BrushResource("PowerFlowCpuBrush");
        var powerBrush = BrushResource("PowerFlowPowerBrush");
        var nowBrush = BrushResource("PowerFlowNowBrush");

        if (samples.Count > 0)
        {
            var firstAt = samples[0].At;
            var lastAt = samples[^1].At;
            var span = Math.Max(1, (lastAt - firstAt).TotalMilliseconds);
            double X(DateTimeOffset at) => Math.Clamp((at - firstAt).TotalMilliseconds / span, 0, 1) * width;

            foreach (var segment in _model.Trajectory.StateSegments)
            {
                var left = X(segment.From);
                var right = X(segment.To);
                var rect = new Rectangle
                {
                    Width = Math.Max(3, right - left),
                    Height = 6,
                    RadiusX = 2,
                    RadiusY = 2,
                    Fill = StateBrush(segment.State),
                    Opacity = 0.72
                };
                Canvas.SetLeft(rect, left);
                Canvas.SetTop(rect, bandTop);
                TrayTrajectoryCanvas.Children.Add(rect);
            }

            if (samples.Count >= 2)
            {
                AddSmoothPath(samples.Select(s => new Point(X(s.At), plotHeight - Math.Clamp(s.CpuPercent / 100d, 0, 1) * (plotHeight - 5))).ToArray(), cpuBrush, 2.2, 0.95);
                var watts = samples.Where(s => s.PackageWatts.HasValue).ToArray();
                if (watts.Length >= 2)
                {
                    var maxPower = Math.Max(100, watts.Max(s => s.PackageWatts!.Value));
                    AddSmoothPath(watts.Select(s => new Point(X(s.At), plotHeight - Math.Clamp(s.PackageWatts!.Value / maxPower, 0, 1) * (plotHeight - 5))).ToArray(), powerBrush, 1.4, 0.55);
                }
            }

            foreach (var marker in _model.Transitions)
            {
                if (marker.At < firstAt || marker.At > lastAt) continue;
                var x = X(marker.At);
                var line = new Line { X1 = x, X2 = x, Y1 = 2, Y2 = bandTop + 5, Stroke = nowBrush, StrokeThickness = 1, Opacity = marker.Success ? 0.28 : 0.70 };
                TrayTrajectoryCanvas.Children.Add(line);
            }

            var last = samples[^1];
            var nowX = X(last.At);
            var nowY = plotHeight - Math.Clamp(last.CpuPercent / 100d, 0, 1) * (plotHeight - 5);
            var halo = new Ellipse { Width = 13, Height = 13, Stroke = cpuBrush, StrokeThickness = 1.4, Opacity = 0.38 };
            Canvas.SetLeft(halo, nowX - 6.5);
            Canvas.SetTop(halo, nowY - 6.5);
            TrayTrajectoryCanvas.Children.Add(halo);
            var dot = new Ellipse { Width = 6, Height = 6, Fill = nowBrush, Stroke = cpuBrush, StrokeThickness = 1 };
            Canvas.SetLeft(dot, nowX - 3);
            Canvas.SetTop(dot, nowY - 3);
            TrayTrajectoryCanvas.Children.Add(dot);
        }
    }

    private void AddSmoothPath(IReadOnlyList<Point> points, Brush stroke, double thickness, double opacity)
    {
        if (points.Count < 2) return;
        var figure = new PathFigure { StartPoint = points[0] };
        for (var i = 1; i < points.Count; i++)
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
        TrayTrajectoryCanvas.Children.Add(new XamlPath
        {
            Data = geometry,
            Stroke = stroke,
            StrokeThickness = thickness,
            StrokeLineJoin = PenLineJoin.Round,
            Opacity = opacity
        });
    }

    private Brush StateBrush(PowerState state) => state switch
    {
        PowerState.PowerSaver => BrushResource("PowerFlowStateSaverBrush"),
        PowerState.Balanced => BrushResource("PowerFlowStateBalancedBrush"),
        PowerState.HighPerformance => BrushResource("PowerFlowStatePerformanceBrush"),
        _ => BrushResource("PowerFlowTrackBrush")
    };

    private Brush BrushResource(string key) =>
        Root.Resources[key] as Brush ?? Application.Current.Resources[key] as Brush ?? new SolidColorBrush(Microsoft.UI.Colors.Gray);

    private void OnRootTapped(object sender, TappedRoutedEventArgs e) => OpenDashboardRequested?.Invoke(this, EventArgs.Empty);

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _controller.SnapshotChanged -= OnSnapshotChanged;
        _recorder.ContinuityChanged -= OnContinuityChanged;
        if (IsVisible) await HideAsync();
        ReleaseVisibility();
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
