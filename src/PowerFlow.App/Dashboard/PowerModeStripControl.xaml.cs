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
    Boost,
    Ultra
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

    public void SetSelection(PowerModeSelection selection, bool manual, string? activationError = null)
    {
        Selection = selection;
        IsManual = manual;
        AutoModeButton.IsChecked = selection == PowerModeSelection.Auto;
        SaverModeButton.IsChecked = selection == PowerModeSelection.Eco;
        BalancedEfficientModeButton.IsChecked = selection == PowerModeSelection.Efficient;
        BalancedPerformanceModeButton.IsChecked = selection == PowerModeSelection.Responsive;
        PerformanceModeButton.IsChecked = selection == PowerModeSelection.Boost;
        UltraModeButton.IsChecked = selection == PowerModeSelection.Ultra;
        var failed = !string.IsNullOrWhiteSpace(activationError);
        ModeAuthorityText.Text = failed ? "FAILED" : manual ? "MANUAL" : "AUTO";
        AutomationProperties.SetName(ModeAuthorityText, failed ? "Power mode activation failed" : manual ? "Manual PowerFlow mode authority" : "Automatic PowerFlow mode authority");
        ToolTipService.SetToolTip(ModeAuthorityText, failed ? activationError : manual ? "PowerFlow is holding the selected operating envelope." : "PowerFlow chooses the operating envelope automatically.");
    }

    private void Request(PowerModeSelection mode)
    {
        SetSelection(Selection, IsManual);
        ModeAuthorityText.Text = "APPLYING";
        ToolTipService.SetToolTip(ModeAuthorityText, "Waiting for PowerFlow to confirm the requested operating envelope.");
        ModeRequested?.Invoke(this, new PowerModeRequestedEventArgs(mode));
    }

    private void OnAutoClicked(object sender, RoutedEventArgs e) => Request(PowerModeSelection.Auto);
    private void OnSaverClicked(object sender, RoutedEventArgs e) => Request(PowerModeSelection.Eco);
    private void OnBalancedEfficientClicked(object sender, RoutedEventArgs e) => Request(PowerModeSelection.Efficient);
    private void OnBalancedPerformanceClicked(object sender, RoutedEventArgs e) => Request(PowerModeSelection.Responsive);
    private void OnPerformanceClicked(object sender, RoutedEventArgs e) => Request(PowerModeSelection.Boost);
    private void OnUltraClicked(object sender, RoutedEventArgs e) => Request(PowerModeSelection.Ultra);
}