using System.Numerics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace PowerFlow.App.Dashboard;

public sealed partial class ShellHeaderControl : UserControl
{
    public ShellHeaderControl()
    {
        InitializeComponent();
        PointerReleased += OnDragPointerReleased;
        PointerCanceled += OnDragPointerCanceled;
        PointerCaptureLost += OnDragPointerCaptureLost;
        ApplyPresentation();
        ApplyPreviewMode();
    }

    public event EventHandler? DragStarted;
    public event EventHandler? DragCompleted;
    public event EventHandler? RulesRequested;
    public event EventHandler? SettingsRequested;
    public event EventHandler<PowerModeRequestedEventArgs>? ModeRequested;

    public HeaderPresentation Presentation
    {
        get => (HeaderPresentation)GetValue(PresentationProperty);
        set => SetValue(PresentationProperty, value);
    }

    public static readonly DependencyProperty PresentationProperty =
        DependencyProperty.Register(nameof(Presentation), typeof(HeaderPresentation), typeof(ShellHeaderControl),
            new PropertyMetadata(HeaderPresentation.Compact, OnPresentationChanged));


    public bool ShowBrand
    {
        get => (bool)GetValue(ShowBrandProperty);
        set => SetValue(ShowBrandProperty, value);
    }

    public static readonly DependencyProperty ShowBrandProperty =
        DependencyProperty.Register(nameof(ShowBrand), typeof(bool), typeof(ShellHeaderControl),
            new PropertyMetadata(true, OnShowBrandChanged));

    private static void OnShowBrandChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((ShellHeaderControl)d).ApplyBrandVisibility();
    public bool IsPreviewMode
    {
        get => (bool)GetValue(IsPreviewModeProperty);
        set => SetValue(IsPreviewModeProperty, value);
    }

    public static readonly DependencyProperty IsPreviewModeProperty =
        DependencyProperty.Register(nameof(IsPreviewMode), typeof(bool), typeof(ShellHeaderControl),
            new PropertyMetadata(false, OnPreviewModeChanged));

    private static void OnPresentationChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((ShellHeaderControl)d).ApplyPresentation();

    private static void OnPreviewModeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((ShellHeaderControl)d).ApplyPreviewMode();

    private UIElement Layer(HeaderPresentation p) => p switch
    {
        HeaderPresentation.Minimal => MinimalHeader,
        HeaderPresentation.Compact => CompactHeader,
        _ => SystemHeader
    };

    private UIElement[] Layers() => [MinimalHeader, CompactHeader, SystemHeader];

    private void ApplyPresentation() => ResetLayers(Presentation);

    public void ApplyMorph(HeaderPresentation from, HeaderPresentation to, double progress, bool reducedMotion)
    {
        var t = Math.Clamp(progress, 0d, 1d);
        if (reducedMotion || from == to || t >= 1d)
        {
            Presentation = to;
            ResetLayers(to);
            return;
        }
        if (t <= 0d)
        {
            ResetLayers(from);
            return;
        }
        MorphLayers(Layer(from), Layer(to), t, 4f);
    }

    private void MorphLayers(UIElement source, UIElement target, double t, float travel)
    {
        foreach (var layer in Layers())
        {
            layer.Visibility = Visibility.Collapsed;
            layer.Opacity = 1d;
            layer.IsHitTestVisible = false;
            SetLayoutTranslation(layer, 0, 0);
        }
        source.Visibility = Visibility.Visible;
        target.Visibility = Visibility.Visible;
        source.Opacity = 1d - t;
        target.Opacity = t;
        SetLayoutTranslation(source, 0, -travel * t);
        SetLayoutTranslation(target, 0, travel * (1d - t));
    }

    private static void SetLayoutTranslation(UIElement element, double x, double y)
    {
        if (element.RenderTransform is not TranslateTransform transform)
        {
            transform = new TranslateTransform();
            element.RenderTransform = transform;
        }
        transform.X = x;
        transform.Y = y;
    }
    private void ResetLayers(HeaderPresentation selected)
    {
        var chosen = Layer(selected);
        foreach (var layer in Layers())
        {
            layer.Visibility = ReferenceEquals(layer, chosen) ? Visibility.Visible : Visibility.Collapsed;
            layer.Opacity = 1d;
            layer.IsHitTestVisible = ReferenceEquals(layer, chosen);
            SetLayoutTranslation(layer, 0, 0);
        }
    }

    public void SetModeSelection(PowerModeSelection selection, bool manual, string? activationError = null)
    {
        CompactModeStrip.SetSelection(selection, manual, activationError);
        SystemModeStrip.SetSelection(selection, manual, activationError);
    }

    private uint? _dragPointerId;
    private UIElement? _dragCaptureElement;

    private void OnDragSurfacePointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not UIElement surface) return;
        var point = e.GetCurrentPoint(surface);
        if (!point.Properties.IsLeftButtonPressed || !surface.CapturePointer(e.Pointer)) return;
        _dragCaptureElement = surface;
        _dragPointerId = e.Pointer.PointerId;
        DragStarted?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }

    private void OnDragPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_dragPointerId != e.Pointer.PointerId) return;
        _dragCaptureElement?.ReleasePointerCapture(e.Pointer);
        EndDrag();
        e.Handled = true;
    }

    private void OnDragPointerCanceled(object sender, PointerRoutedEventArgs e)
    {
        if (_dragPointerId != e.Pointer.PointerId) return;
        EndDrag();
    }

    private void OnDragPointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        if (_dragPointerId == e.Pointer.PointerId) EndDrag();
    }

    private void EndDrag()
    {
        if (_dragPointerId is null) return;
        _dragPointerId = null;
        _dragCaptureElement = null;
        DragCompleted?.Invoke(this, EventArgs.Empty);
    }
    private void OnModeRequested(object sender, PowerModeRequestedEventArgs e) => ModeRequested?.Invoke(this, e);
    private void OnRulesClicked(object sender, RoutedEventArgs e) => RulesRequested?.Invoke(this, EventArgs.Empty);
    private void OnSettingsClicked(object sender, RoutedEventArgs e) => SettingsRequested?.Invoke(this, EventArgs.Empty);

    public void ApplyBrandMorph(double progress)
    {
        var opacity = Math.Clamp(progress, 0d, 1d);
        foreach (var element in new UIElement[] { MinimalBrandIcon, MinimalBrandText, CompactBrandIcon, CompactBrandText, SystemBrandIcon, SystemBrandText })
        {
            element.Visibility = opacity <= 0d ? Visibility.Collapsed : Visibility.Visible;
            element.Opacity = opacity;
        }
    }
    private void ApplyBrandVisibility()
    {
        var visibility = ShowBrand ? Visibility.Visible : Visibility.Collapsed;
        foreach (var element in new UIElement[] { MinimalBrandIcon, MinimalBrandText, CompactBrandIcon, CompactBrandText, SystemBrandIcon, SystemBrandText })
        {
            element.Visibility = visibility;
            element.Opacity = 1d;
        }
    }
    private void ApplyPreviewMode()
    {
        if (PreviewBadge is null) return;
        PreviewBadge.Visibility = IsPreviewMode ? Visibility.Visible : Visibility.Collapsed;
    }
}
