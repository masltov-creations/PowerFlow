using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PowerFlow.Core.Rules;

namespace PowerFlow.App.Settings;

public sealed partial class SettingsPage : Page
{
    private PowerFlowConfig _config = PowerFlowConfig.Default;
    private Func<PowerFlowConfig, Task>? _apply;
    private Action<ThemePreference>? _themePreview;
    private bool _loading;

    public SettingsPage() => InitializeComponent();

    public void Initialize(PowerFlowConfig config, Func<PowerFlowConfig, Task> apply, Action<ThemePreference> themePreview)
    {
        _apply = apply;
        _themePreview = themePreview;
        RefreshConfig(config);
    }

    public void RefreshConfig(PowerFlowConfig config)
    {
        _loading = true;
        _config = config;
        SelectTag(ReducedMotionBox, config.ReducedMotionOverride switch { true => "On", false => "Off", null => "Auto" });
        SelectTag(ThemeBox, config.Theme.ToString());
        StartupToggle.IsOn = config.StartWithWindows;
        _loading = false;
    }

    private void OnThemeSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        _themePreview?.Invoke(ThemeOf());
    }

    private async void OnSave(object sender, RoutedEventArgs e)
    {
        if (_loading || _apply is null) return;
        var reduced = TagOf(ReducedMotionBox) switch { "On" => true, "Off" => false, _ => (bool?)null };
        var updated = _config with
        {
            StartWithWindows = StartupToggle.IsOn,
            ReducedMotionOverride = reduced,
            Theme = ThemeOf()
        };
        await _apply(updated);
        _config = updated;
        StatusText.Text = "Saved.";
    }

    private ThemePreference ThemeOf() => TagOf(ThemeBox) switch
    {
        "Light" => ThemePreference.Light,
        "Dark" => ThemePreference.Dark,
        _ => ThemePreference.System
    };

    private static string? TagOf(ComboBox box) => (box.SelectedItem as ComboBoxItem)?.Tag as string;
    private static void SelectTag(ComboBox box, string tag) => box.SelectedItem = box.Items.OfType<ComboBoxItem>().FirstOrDefault(i => string.Equals(i.Tag as string, tag, StringComparison.OrdinalIgnoreCase));
}