using System.Numerics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;

namespace PowerFlow.App.Dashboard;

public sealed partial class ShellHeaderControl : UserControl
{
    public ShellHeaderControl()
    {
        InitializeComponent();
        ApplyPresentation();
        ApplyPreviewMode();
    }

    public event EventHandler? RulesRequested;
    public event EventHandler? SettingsRequested;

    public HeaderPresentation Presentation
    {
        get => (HeaderPresentation)GetValue(PresentationProperty);
        set => SetValue(PresentationProperty, value);
    }

    public static readonly DependencyProperty PresentationProperty =
        DependencyProperty.Register(nameof(Presentation), typeof(HeaderPresentation), typeof(ShellHeaderControl),
            new PropertyMetadata(HeaderPresentation.Compact, OnPresentationChanged));

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
            ElementCompositionPreview.GetElementVisual(layer).Offset = Vector3.Zero;
        }
        source.Visibility = Visibility.Visible;
        target.Visibility = Visibility.Visible;
        source.Opacity = 1d - t;
        target.Opacity = t;
        ElementCompositionPreview.GetElementVisual(source).Offset = new Vector3(0, -travel * (float)t, 0);
        ElementCompositionPreview.GetElementVisual(target).Offset = new Vector3(0, travel * (float)(1d - t), 0);
    }

    private void ResetLayers(HeaderPresentation selected)
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

    private void OnRulesClicked(object sender, RoutedEventArgs e) => RulesRequested?.Invoke(this, EventArgs.Empty);
    private void OnSettingsClicked(object sender, RoutedEventArgs e) => SettingsRequested?.Invoke(this, EventArgs.Empty);

    private void ApplyPreviewMode()
    {
        if (PreviewBadge is null) return;
        PreviewBadge.Visibility = IsPreviewMode ? Visibility.Visible : Visibility.Collapsed;
    }
}
