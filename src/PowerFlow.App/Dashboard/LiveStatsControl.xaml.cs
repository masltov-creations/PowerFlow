using System.Numerics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;

namespace PowerFlow.App.Dashboard;

public sealed partial class LiveStatsControl : UserControl
{
    public LiveStatsControl()
    {
        InitializeComponent();
        ApplyPresentation();
    }

    public StatsPresentation Presentation
    {
        get => (StatsPresentation)GetValue(PresentationProperty);
        set => SetValue(PresentationProperty, value);
    }

    public static readonly DependencyProperty PresentationProperty =
        DependencyProperty.Register(nameof(Presentation), typeof(StatsPresentation), typeof(LiveStatsControl),
            new PropertyMetadata(StatsPresentation.Inline, OnPresentationChanged));

    private static void OnPresentationChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((LiveStatsControl)d).ApplyPresentation();

    private UIElement Layer(StatsPresentation p) => p switch
    {
        StatsPresentation.Inline => InlineStats,
        StatsPresentation.CompactRail => CompactStatsRail,
        _ => FullStatsRail
    };

    private UIElement[] Layers() => [InlineStats, CompactStatsRail, FullStatsRail];
    private void ApplyPresentation() => ResetLayers(Presentation);

    public void ApplyMorph(StatsPresentation from, StatsPresentation to, double progress, bool reducedMotion)
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

    private void ResetLayers(StatsPresentation selected)
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
}
