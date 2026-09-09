using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace PowerFlow.App.Dashboard;

public sealed partial class OperationalContextControl : UserControl
{
    public OperationalContextControl()
    {
        InitializeComponent();
        ApplyPresentation();
    }

    public event EventHandler? RulesRequested;
    public event EventHandler? SettingsRequested;

    public SecondaryPresentation Presentation
    {
        get => (SecondaryPresentation)GetValue(PresentationProperty);
        set => SetValue(PresentationProperty, value);
    }

    public static readonly DependencyProperty PresentationProperty =
        DependencyProperty.Register(nameof(Presentation), typeof(SecondaryPresentation), typeof(OperationalContextControl),
            new PropertyMetadata(SecondaryPresentation.Hidden, OnPresentationChanged));

    private static void OnPresentationChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((OperationalContextControl)d).ApplyPresentation();

    private void ApplyPresentation()
    {
        if (Root is null) return;
        Root.Visibility = Presentation == SecondaryPresentation.Hidden ? Visibility.Collapsed : Visibility.Visible;
        SummaryLayer.Visibility = Presentation == SecondaryPresentation.Summary ? Visibility.Visible : Visibility.Collapsed;
        FullLayer.Visibility = Presentation == SecondaryPresentation.Full ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnRulesClicked(object sender, RoutedEventArgs e) => RulesRequested?.Invoke(this, EventArgs.Empty);
    private void OnSettingsClicked(object sender, RoutedEventArgs e) => SettingsRequested?.Invoke(this, EventArgs.Empty);
}
