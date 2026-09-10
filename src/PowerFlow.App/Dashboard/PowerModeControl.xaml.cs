using System.Numerics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;

namespace PowerFlow.App.Dashboard;

public enum PowerModeChoice { PowerSaver, Balanced, Performance, Auto }

public sealed class PowerModeRequestedEventArgs(PowerModeChoice choice) : EventArgs
{
    public PowerModeChoice Choice { get; } = choice;
}

public sealed partial class PowerModeControl : UserControl
{
    public PowerModeControl()
    {
        InitializeComponent();
        ApplyPresentation();
    }

    public event EventHandler<PowerModeRequestedEventArgs>? ModeRequested;

    public ModePresentation Presentation
    {
        get => (ModePresentation)GetValue(PresentationProperty);
        set => SetValue(PresentationProperty, value);
    }

    public static readonly DependencyProperty PresentationProperty =
        DependencyProperty.Register(nameof(Presentation), typeof(ModePresentation), typeof(PowerModeControl),
            new PropertyMetadata(ModePresentation.Cards, OnPresentationChanged));

    private static void OnPresentationChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((PowerModeControl)d).ApplyPresentation();

    private UIElement Layer(ModePresentation p) => p switch
    {
        ModePresentation.CurrentChip => CurrentChipLayer,
        ModePresentation.Segmented => SegmentedLayer,
        _ => CardsLayer
    };

    private UIElement[] Layers() => [CurrentChipLayer, SegmentedLayer, CardsLayer];
    private void ApplyPresentation() => ResetLayers(Presentation);

    public void ApplyMorph(ModePresentation from, ModePresentation to, double progress, bool reducedMotion)
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
        ElementCompositionPreview.GetElementVisual(source).Offset = new Vector3(-travel * (float)t, 0, 0);
        ElementCompositionPreview.GetElementVisual(target).Offset = new Vector3(travel * (float)(1d - t), 0, 0);
    }

    private void ResetLayers(ModePresentation selected)
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

    private void OnSaverClicked(object sender, RoutedEventArgs e) => Request(PowerModeChoice.PowerSaver);
    private void OnBalancedClicked(object sender, RoutedEventArgs e) => Request(PowerModeChoice.Balanced);
    private void OnPerformanceClicked(object sender, RoutedEventArgs e) => Request(PowerModeChoice.Performance);
    private void OnAutoClicked(object sender, RoutedEventArgs e) => Request(PowerModeChoice.Auto);
    private void Request(PowerModeChoice choice) => ModeRequested?.Invoke(this, new PowerModeRequestedEventArgs(choice));
}
