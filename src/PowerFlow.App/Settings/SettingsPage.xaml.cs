using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PowerFlow.Core.Policy;
using PowerFlow.Core.Rules;
using PowerFlow.Windows.Power;

namespace PowerFlow.App.Settings;

public sealed partial class SettingsPage : Page
{
    private sealed record PlanChoice(Guid? Id, string Name);

    private PowerFlowConfig _config = PowerFlowConfig.Default;
    private Func<PowerFlowConfig, Task>? _apply;
    private Func<Task<IReadOnlyList<PowerPlanInfo>>>? _listPlans;
    private Action<ThemePreference>? _themePreview;
    private IReadOnlyList<PlanChoice> _planChoices = Array.Empty<PlanChoice>();
    private bool _loading;

    public SettingsPage() => InitializeComponent();

    public void Initialize(PowerFlowConfig config, Func<PowerFlowConfig, Task> apply, Func<Task<IReadOnlyList<PowerPlanInfo>>> listPlans, Action<ThemePreference> themePreview)
    {
        _apply = apply;
        _listPlans = listPlans;
        _themePreview = themePreview;
        RefreshConfig(config);
        _ = LoadPlansAsync();
    }

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
        SelectTag(ThemeBox, config.Theme.ToString());
        StartupToggle.IsOn = config.StartWithWindows;
        SafeRestToggle.IsOn = config.RestingState == PowerState.Balanced;
        ApplyPlanSelections(config);
        _loading = false;
    }

    private async Task LoadPlansAsync()
    {
        if (_listPlans is null) return;
        try
        {
            var plans = await _listPlans();
            _planChoices = new[] { new PlanChoice(null, "Windows default") }
                .Concat(plans.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).Select(p => new PlanChoice(p.Id, p.Name)))
                .ToArray();
            SaverPlanBox.ItemsSource = _planChoices;
            BalancedPlanBox.ItemsSource = _planChoices;
            PerformancePlanBox.ItemsSource = _planChoices;
            ApplyPlanSelections(_config);
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Could not list power plans: {ex.Message}";
        }
    }

    private void ApplyPlanSelections(PowerFlowConfig config)
    {
        if (_planChoices.Count == 0) return;
        SaverPlanBox.SelectedItem = FindPlan(config.PowerSaverPlanId);
        BalancedPlanBox.SelectedItem = FindPlan(config.BalancedPlanId);
        PerformancePlanBox.SelectedItem = FindPlan(config.HighPerformancePlanId);
    }

    private PlanChoice FindPlan(Guid? id) => _planChoices.FirstOrDefault(p => p.Id == id) ?? _planChoices[0];

    private void OnThemeSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        _themePreview?.Invoke(ThemeOf());
    }

    private async void OnSave(object sender, RoutedEventArgs e)
    {
        if (_loading || _apply is null) return;
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
            PowerSaverPlanId = SelectedPlanId(SaverPlanBox),
            BalancedPlanId = SelectedPlanId(BalancedPlanBox),
            HighPerformancePlanId = SelectedPlanId(PerformancePlanBox),
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

    private static Guid? SelectedPlanId(ComboBox box) => (box.SelectedItem as PlanChoice)?.Id;
    private static string? TagOf(ComboBox box) => (box.SelectedItem as ComboBoxItem)?.Tag as string;
    private static void SelectTag(ComboBox box, string tag) => box.SelectedItem = box.Items.OfType<ComboBoxItem>().FirstOrDefault(i => string.Equals(i.Tag as string, tag, StringComparison.OrdinalIgnoreCase));
}
