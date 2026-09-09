using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

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

    private void ApplyPresentation()
    {
        if (CauseLineLayer is null) return;
        CauseLineLayer.Visibility = Presentation == ControlContextPresentation.CauseLine ? Visibility.Visible : Visibility.Collapsed;
        ContextRailLayer.Visibility = Presentation == ControlContextPresentation.Rail ? Visibility.Visible : Visibility.Collapsed;
        ContextModulesLayer.Visibility = Presentation == ControlContextPresentation.Modules ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnReleaseManualClicked(object sender, RoutedEventArgs e)
        => ReleaseManualRequested?.Invoke(this, EventArgs.Empty);
}
