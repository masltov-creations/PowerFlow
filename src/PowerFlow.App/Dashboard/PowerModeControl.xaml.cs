using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

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
        DependencyProperty.Register(
            nameof(Presentation),
            typeof(ModePresentation),
            typeof(PowerModeControl),
            new PropertyMetadata(ModePresentation.Cards, OnPresentationChanged));

    private static void OnPresentationChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((PowerModeControl)d).ApplyPresentation();

    private void ApplyPresentation()
    {
        if (CardsLayer is null) return;
        CurrentChipLayer.Visibility = Presentation == ModePresentation.CurrentChip ? Visibility.Visible : Visibility.Collapsed;
        SegmentedLayer.Visibility = Presentation == ModePresentation.Segmented ? Visibility.Visible : Visibility.Collapsed;
        CardsLayer.Visibility = Presentation == ModePresentation.Cards ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnSaverClicked(object sender, RoutedEventArgs e) => Request(PowerModeChoice.PowerSaver);
    private void OnBalancedClicked(object sender, RoutedEventArgs e) => Request(PowerModeChoice.Balanced);
    private void OnPerformanceClicked(object sender, RoutedEventArgs e) => Request(PowerModeChoice.Performance);
    private void OnAutoClicked(object sender, RoutedEventArgs e) => Request(PowerModeChoice.Auto);
    private void Request(PowerModeChoice choice) => ModeRequested?.Invoke(this, new PowerModeRequestedEventArgs(choice));
}
