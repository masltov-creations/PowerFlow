using System.Numerics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;

namespace PowerFlow.App.Dashboard;

public sealed partial class ControlContextControl : UserControl
{
    public ControlContextControl()
    {
        InitializeComponent();
        ApplyPresentation();
    }

    public event EventHandler? ReleaseManualRequested;

    public ControlContextPresentation Presentation
    {
        get => (ControlContextPresentation)GetValue(PresentationProperty);
        set => SetValue(PresentationProperty, value);
    }

    public static readonly DependencyProperty PresentationProperty =
        DependencyProperty.Register(nameof(Presentation), typeof(ControlContextPresentation), typeof(ControlContextControl),
            new PropertyMetadata(ControlContextPresentation.CauseLine, OnPresentationChanged));

    private static void OnPresentationChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((ControlContextControl)d).ApplyPresentation();

    private UIElement Layer(ControlContextPresentation p) => p switch
    {
        ControlContextPresentation.CauseLine => CauseLineLayer,
        ControlContextPresentation.Rail => ContextRailLayer,
        _ => ContextModulesLayer
    };

    private UIElement[] Layers() => [CauseLineLayer, ContextRailLayer, ContextModulesLayer];
    private void ApplyPresentation() => ResetLayers(Presentation);

    public void ApplyMorph(ControlContextPresentation from, ControlContextPresentation to, double progress, bool reducedMotion)
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
        MorphLayers(Layer(from), Layer(to), t, 5f);
    }

    private void MorphLayers(UIElement source, UIElement target, double t, float travel)
    {
        foreach (var layer in Layers())
        {
            layer.Visibility = Visibility.Collapsed;
            layer.Opacity = 1d;
            layer.IsHitTestVisible = false;
            ElementCompositionPreview.GetElementVisual(layer).Offset = Vector3.Zero;
        }
        source.Visibility = Visibility.Visible;
        target.Visibility = Visibility.Visible;
        source.Opacity = 1d - t;
        target.Opacity = t;
        ElementCompositionPreview.GetElementVisual(source).Offset = new Vector3(0, -travel * (float)t, 0);
        ElementCompositionPreview.GetElementVisual(target).Offset = new Vector3(0, travel * (float)(1d - t), 0);
    }

    private void ResetLayers(ControlContextPresentation selected)
    {
        var chosen = Layer(selected);
        foreach (var layer in Layers())
        {
            layer.Visibility = ReferenceEquals(layer, chosen) ? Visibility.Visible : Visibility.Collapsed;
            layer.Opacity = 1d;
            layer.IsHitTestVisible = ReferenceEquals(layer, chosen);
            ElementCompositionPreview.GetElementVisual(layer).Offset = Vector3.Zero;
        }
    }

    private void OnReleaseManualClicked(object sender, RoutedEventArgs e)
        => ReleaseManualRequested?.Invoke(this, EventArgs.Empty);
}
