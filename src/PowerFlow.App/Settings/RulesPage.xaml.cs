using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PowerFlow.Core.Rules;
using PowerFlow.Windows.Foreground;

namespace PowerFlow.App.Settings;

public sealed partial class RulesPage : Page
{
    private PowerFlowConfig _config = PowerFlowConfig.Default;
    private Func<PowerFlowConfig, Task>? _apply;
    private readonly ForegroundWindowProbe _foreground = new();

    public RulesPage() => InitializeComponent();

    public void Initialize(PowerFlowConfig config, Func<PowerFlowConfig, Task> apply) { _apply = apply; RefreshConfig(config); }
    public void RefreshConfig(PowerFlowConfig config) { _config = config; RulesRepeater.ItemsSource = config.AppRules.ToArray(); }

    private async void OnRememberGame(object sender, RoutedEventArgs e) => await RememberForegroundAsync(AppRuleMode.Performance);
    private async void OnRememberBalanced(object sender, RoutedEventArgs e) => await RememberForegroundAsync(AppRuleMode.Balanced);

    private async Task RememberForegroundAsync(AppRuleMode mode)
    {
        var foreground = _foreground.Read();
        if (foreground is null || string.IsNullOrWhiteSpace(foreground.ExecutablePath)) { StatusText.Text = "Could not resolve the foreground process."; return; }
        var path = foreground.ExecutablePath;
        var rules = _config.AppRules.Where(x => !string.Equals(x.ExecutablePath, path, StringComparison.OrdinalIgnoreCase)).ToList();
        rules.Add(new AppRule(path, mode, Path.GetFileNameWithoutExtension(path), true));
        await ApplyAsync(_config with { AppRules = rules });
        StatusText.Text = $"Saved {Path.GetFileName(path)} as {mode}.";
    }

    private async void OnSetPerformanceFromCard(object sender, RoutedEventArgs e) => await ChangeRuleFromCardAsync(sender, AppRuleMode.Performance);
    private async void OnSetBalancedFromCard(object sender, RoutedEventArgs e) => await ChangeRuleFromCardAsync(sender, AppRuleMode.Balanced);
    private async void OnRemoveFromCard(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not AppRule selected) return;
        var rules = _config.AppRules.Where(x => !string.Equals(x.ExecutablePath, selected.ExecutablePath, StringComparison.OrdinalIgnoreCase)).ToArray();
        await ApplyAsync(_config with { AppRules = rules });
        StatusText.Text = "Rule removed.";
    }

    private async Task ChangeRuleFromCardAsync(object sender, AppRuleMode mode)
    {
        if ((sender as FrameworkElement)?.Tag is not AppRule selected) return;
        var rules = _config.AppRules.Select(x => string.Equals(x.ExecutablePath, selected.ExecutablePath, StringComparison.OrdinalIgnoreCase) ? x with { Mode = mode } : x).ToArray();
        await ApplyAsync(_config with { AppRules = rules });
        StatusText.Text = $"{selected.DisplayName ?? Path.GetFileName(selected.ExecutablePath)} → {mode}.";
    }

    private async Task ApplyAsync(PowerFlowConfig updated)
    {
        if (_apply is null) return;
        await _apply(updated);
        RefreshConfig(updated);
    }
}
