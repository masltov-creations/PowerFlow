using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PowerFlow.Core.Policy;
using PowerFlow.Core.Rules;

namespace PowerFlow.App.Settings;

public sealed partial class SettingsPage : Page
{
    private PowerFlowConfig _config = PowerFlowConfig.Default;
    private Func<PowerFlowConfig, Task>? _apply;
    private bool _loading;

    public SettingsPage() => InitializeComponent();
    public void Initialize(PowerFlowConfig config, Func<PowerFlowConfig, Task> apply) { _apply = apply; RefreshConfig(config); }

    public void RefreshConfig(PowerFlowConfig config)
    {
        _loading = true;
        _config = config;
        SelectTag(RestingStateBox, config.RestingState == PowerState.Balanced ? "Balanced" : "PowerSaver");
        PromotionThresholdBox.Value = config.CpuPromotionThresholdPercent;
        PromotionWindowBox.Value = config.CpuPromotionWindow.TotalSeconds;
        QuietThresholdBox.Value = config.QuietThresholdPercent;
        QuietWindowBox.Value = config.QuietWindow.TotalSeconds;
        CooldownBox.Value = config.PostGameCooldown.TotalSeconds;
        SelectTag(ReducedMotionBox, config.ReducedMotionOverride switch { true => "On", false => "Off", null => "Auto" });
        StartupToggle.IsOn = config.StartWithWindows;
        SafeRestToggle.IsOn = config.RestingState == PowerState.Balanced;
        SaverPlanBox.Text = config.PowerSaverPlanId?.ToString() ?? "";
        BalancedPlanBox.Text = config.BalancedPlanId?.ToString() ?? "";
        PerformancePlanBox.Text = config.HighPerformancePlanId?.ToString() ?? "";
        _loading = false;
    }

    private async void OnSave(object sender, RoutedEventArgs e)
    {
        if (_loading || _apply is null) return;
        if (!TryGuid(SaverPlanBox.Text, out var saver) || !TryGuid(BalancedPlanBox.Text, out var balanced) || !TryGuid(PerformancePlanBox.Text, out var performance))
        { StatusText.Text = "One of the plan GUIDs is invalid."; return; }
        var resting = SafeRestToggle.IsOn || TagOf(RestingStateBox) == "Balanced" ? PowerState.Balanced : PowerState.PowerSaver;
        var reduced = TagOf(ReducedMotionBox) switch { "On" => true, "Off" => false, _ => (bool?)null };
        var updated = _config with
        {
            RestingState = resting,
            CpuPromotionThresholdPercent = PromotionThresholdBox.Value,
            CpuPromotionWindow = TimeSpan.FromSeconds(PromotionWindowBox.Value),
            QuietThresholdPercent = QuietThresholdBox.Value,
            QuietWindow = TimeSpan.FromSeconds(QuietWindowBox.Value),
            PostGameCooldown = TimeSpan.FromSeconds(CooldownBox.Value),
            PowerSaverPlanId = saver,
            BalancedPlanId = balanced,
            HighPerformancePlanId = performance,
            StartWithWindows = StartupToggle.IsOn,
            ReducedMotionOverride = reduced
        };
        await _apply(updated);
        _config = updated;
        StatusText.Text = "Saved.";
    }

    private static string? TagOf(ComboBox box) => (box.SelectedItem as ComboBoxItem)?.Tag as string;
    private static void SelectTag(ComboBox box, string tag) => box.SelectedItem = box.Items.OfType<ComboBoxItem>().FirstOrDefault(i => string.Equals(i.Tag as string, tag, StringComparison.OrdinalIgnoreCase));
    private static bool TryGuid(string? text, out Guid? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(text)) return true;
        if (!Guid.TryParse(text.Trim(), out var parsed)) return false;
        value = parsed;
        return true;
    }
}
