using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace PowerFlow.App.Dashboard;

public enum PowerModeSelection
{
    Auto,
    Eco,
    Efficient,
    Responsive,
    Boost
}

public sealed class PowerModeRequestedEventArgs(PowerModeSelection mode) : EventArgs
{
    public PowerModeSelection Mode { get; } = mode;
}

public sealed partial class PowerModeStripControl : UserControl
{
    public PowerModeStripControl()
    {
        InitializeComponent();
        SetSelection(PowerModeSelection.Auto, manual: false);
    }

    public event EventHandler<PowerModeRequestedEventArgs>? ModeRequested;

    public PowerModeSelection Selection { get; private set; } = PowerModeSelection.Auto;
    public bool IsManual { get; private set; }

    public void SetSelection(PowerModeSelection selection, bool manual)
    {
        Selection = selection;
        IsManual = manual;
        AutoModeButton.IsChecked = selection == PowerModeSelection.Auto;
        EcoModeButton.IsChecked = selection == PowerModeSelection.Eco;
        EfficientModeButton.IsChecked = selection == PowerModeSelection.Efficient;
        ResponsiveModeButton.IsChecked = selection == PowerModeSelection.Responsive;
        BoostModeButton.IsChecked = selection == PowerModeSelection.Boost;
        ModeAuthorityText.Text = manual ? "MANUAL" : "AUTO";
        AutomationProperties.SetName(ModeAuthorityText, manual ? "Manual power mode authority" : "Automatic power mode authority");
    }

    private void Request(PowerModeSelection mode)
    {
        SetSelection(mode, mode != PowerModeSelection.Auto);
        ModeRequested?.Invoke(this, new PowerModeRequestedEventArgs(mode));
    }

    private void OnAutoClicked(object sender, RoutedEventArgs e) => Request(PowerModeSelection.Auto);
    private void OnEcoClicked(object sender, RoutedEventArgs e) => Request(PowerModeSelection.Eco);
    private void OnEfficientClicked(object sender, RoutedEventArgs e) => Request(PowerModeSelection.Efficient);
    private void OnResponsiveClicked(object sender, RoutedEventArgs e) => Request(PowerModeSelection.Responsive);
    private void OnBoostClicked(object sender, RoutedEventArgs e) => Request(PowerModeSelection.Boost);
}
