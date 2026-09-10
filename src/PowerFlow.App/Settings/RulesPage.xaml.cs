using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using PowerFlow.Core.Envelope;
using PowerFlow.Core.Rules;
using PowerFlow.Windows.Apps;
using Windows.Storage;
using Windows.Storage.FileProperties;

namespace PowerFlow.App.Settings;

public sealed partial class RulesPage : Page
{
    private sealed record AppPickerItem(RunningAppOption App, ImageSource? Icon)
    {
        public string DisplayName => App.DisplayName;
        public string ExecutablePath => App.ExecutablePath;
    }

    private sealed record RuleCardItem(AppRule Rule)
    {
        public string DisplayName => Rule.DisplayName ?? Path.GetFileNameWithoutExtension(Rule.ExecutablePath);
        public string ExecutablePath => Rule.ExecutablePath;
        public PerformanceEntitlement EffectiveEntitlement => Rule.EffectiveEntitlement;
        public string CeilingLabel => $"MAX {EffectiveEntitlement.MaximumZone}".ToUpperInvariant();
        public string TimingLabel => $"QUALIFY {EffectiveEntitlement.QualificationDuration.TotalSeconds:0.#}s · LEASE {EffectiveEntitlement.LeaseDuration.TotalSeconds:0.#}s · RELEASE {EffectiveEntitlement.ReleaseHysteresis.TotalSeconds:0.#}s";
    }

    private PowerFlowConfig _config = PowerFlowConfig.Default;
    private Func<PowerFlowConfig, Task>? _apply;
    private Func<Task<string?>>? _browseExecutable;
    private readonly RunningAppCatalog _catalog = new();
    private IReadOnlyList<AppPickerItem> _pickerItems = Array.Empty<AppPickerItem>();
    private AppRule? _editingRule;
    private bool _refreshing;

    public RulesPage() => InitializeComponent();

    public void Initialize(PowerFlowConfig config, Func<PowerFlowConfig, Task> apply, Func<Task<string?>> browseExecutable)
    {
        _apply = apply;
        _browseExecutable = browseExecutable;
        RefreshConfig(config);
    }

    public void RefreshConfig(PowerFlowConfig config)
    {
        _config = config;
        _refreshing = true;
        try
        {
            AdaptiveActuationToggle.IsOn = config.AdaptiveActuationEnabled;
            RulesRepeater.ItemsSource = config.AppRules.Select(rule => new RuleCardItem(rule)).ToArray();
        }
        finally { _refreshing = false; }
    }

    private async void OnAdaptiveActuationToggled(object sender, RoutedEventArgs e)
    {
        if (_refreshing) return;
        var enabled = AdaptiveActuationToggle.IsOn;
        await ApplyAsync(_config with { AdaptiveActuationEnabled = enabled });
        StatusText.Text = enabled
            ? "Adaptive actuation enabled; Auto still requires confidence and latch safety gates."
            : "Adaptive governor is advisory only.";
    }

    private async void OnAddAppRule(object sender, RoutedEventArgs e)
    {
        AppSearchBox.Text = "";
        AppModeBox.SelectedIndex = 0;
        _pickerItems = await BuildPickerItemsAsync(_catalog.List());
        AppPickerList.ItemsSource = _pickerItems;
        AppPickerList.SelectedItem = _pickerItems.FirstOrDefault();
        AppPickerDialog.XamlRoot = XamlRoot;
        await AppPickerDialog.ShowAsync();
    }

    private void OnAppSearchChanged(object sender, TextChangedEventArgs e)
    {
        var query = AppSearchBox.Text?.Trim() ?? "";
        AppPickerList.ItemsSource = string.IsNullOrWhiteSpace(query)
            ? _pickerItems
            : _pickerItems.Where(a => a.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase) || a.ExecutablePath.Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();
    }

    private async void OnBrowseExecutable(object sender, RoutedEventArgs e)
    {
        if (_browseExecutable is null) return;
        var path = await _browseExecutable();
        if (string.IsNullOrWhiteSpace(path)) return;
        var option = new RunningAppOption(0, Path.GetFileNameWithoutExtension(path), path);
        var item = new AppPickerItem(option, await LoadAppIconAsync(path));
        _pickerItems = new[] { item }.Concat(_pickerItems.Where(x => !string.Equals(x.ExecutablePath, path, StringComparison.OrdinalIgnoreCase))).ToArray();
        AppSearchBox.Text = "";
        AppPickerList.ItemsSource = _pickerItems;
        AppPickerList.SelectedItem = item;
    }

    private async void OnAppPickerPrimary(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if (AppPickerList.SelectedItem is not AppPickerItem selected)
        {
            args.Cancel = true;
            StatusText.Text = "Choose an app first.";
            return;
        }
        var deferral = args.GetDeferral();
        try
        {
            var mode = (AppModeBox.SelectedItem as ComboBoxItem)?.Tag as string == "Balanced" ? AppRuleMode.Balanced : AppRuleMode.Performance;
            await AddOrReplaceRuleAsync(selected.ExecutablePath, selected.DisplayName, mode);
        }
        finally { deferral.Complete(); }
    }

    private async void OnEditEntitlementFromCard(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not AppRule selected) return;
        _editingRule = selected;
        var entitlement = selected.EffectiveEntitlement;
        EntitlementAppText.Text = selected.DisplayName ?? Path.GetFileNameWithoutExtension(selected.ExecutablePath);
        MaximumZoneBox.SelectedIndex = entitlement.MaximumZone switch { EnvelopeZone.Eco => 0, EnvelopeZone.Efficient => 1, EnvelopeZone.Responsive => 2, _ => 3 };
        QualificationSecondsBox.Value = entitlement.QualificationDuration.TotalSeconds;
        LeaseSecondsBox.Value = entitlement.LeaseDuration.TotalSeconds;
        ReleaseHysteresisSecondsBox.Value = entitlement.ReleaseHysteresis.TotalSeconds;
        FollowChildrenCheck.IsChecked = entitlement.FollowChildren;
        LegacyModeText.Text = $"Legacy schema mode remains {selected.Mode}; an explicit entitlement now overrides that compatibility mapping.";
        EntitlementDialog.XamlRoot = XamlRoot;
        await EntitlementDialog.ShowAsync();
    }

    private async void OnEntitlementPrimary(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if (_editingRule is not AppRule selected) { args.Cancel = true; return; }
        var deferral = args.GetDeferral();
        try
        {
            var zone = MaximumZoneBox.SelectedIndex switch { 0 => EnvelopeZone.Eco, 1 => EnvelopeZone.Efficient, 2 => EnvelopeZone.Responsive, _ => EnvelopeZone.Boost };
            var qualification = TimeSpan.FromSeconds(NumberValue(QualificationSecondsBox, 4, min: 0));
            var lease = TimeSpan.FromSeconds(NumberValue(LeaseSecondsBox, 12, min: 1));
            var release = TimeSpan.FromSeconds(NumberValue(ReleaseHysteresisSecondsBox, 10, min: 0));
            var followChildren = FollowChildrenCheck.IsChecked != false;
            var entitlement = new PerformanceEntitlement(zone, qualification, lease, release, followChildren);
            var rules = _config.AppRules.Select(rule => string.Equals(rule.ExecutablePath, selected.ExecutablePath, StringComparison.OrdinalIgnoreCase)
                ? rule with { FollowChildren = followChildren, Entitlement = entitlement }
                : rule).ToArray();
            await ApplyAsync(_config with { AppRules = rules });
            StatusText.Text = $"Saved {EntitlementAppText.Text}: max {zone}, qualify {qualification.TotalSeconds:0.#}s, lease {lease.TotalSeconds:0.#}s.";
        }
        finally
        {
            _editingRule = null;
            deferral.Complete();
        }
    }

    private static double NumberValue(NumberBox box, double fallback, double min)
        => double.IsFinite(box.Value) ? Math.Max(min, box.Value) : fallback;

    private async void OnRemoveFromCard(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not AppRule selected) return;
        var rules = _config.AppRules.Where(x => !string.Equals(x.ExecutablePath, selected.ExecutablePath, StringComparison.OrdinalIgnoreCase)).ToArray();
        await ApplyAsync(_config with { AppRules = rules });
        StatusText.Text = "Rule removed.";
    }

    private async Task AddOrReplaceRuleAsync(string path, string displayName, AppRuleMode mode)
    {
        var rules = _config.AppRules.Where(x => !string.Equals(x.ExecutablePath, path, StringComparison.OrdinalIgnoreCase)).ToList();
        rules.Add(new AppRule(path, mode, displayName, true));
        await ApplyAsync(_config with { AppRules = rules });
        StatusText.Text = $"Saved {displayName} with legacy {mode} starting entitlement. Use Edit entitlement for semantic tuning.";
    }

    private static async Task<IReadOnlyList<AppPickerItem>> BuildPickerItemsAsync(IReadOnlyList<RunningAppOption> apps)
    {
        var items = new List<AppPickerItem>(apps.Count);
        foreach (var app in apps) items.Add(new AppPickerItem(app, await LoadAppIconAsync(app.ExecutablePath)));
        return items;
    }

    private static async Task<ImageSource?> LoadAppIconAsync(string executablePath)
    {
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(executablePath);
            using var thumbnail = await file.GetThumbnailAsync(ThumbnailMode.SingleItem, 32, ThumbnailOptions.ResizeThumbnail | ThumbnailOptions.UseCurrentScale);
            if (thumbnail is null || thumbnail.Size == 0) return null;
            var image = new BitmapImage();
            await image.SetSourceAsync(thumbnail);
            return image;
        }
        catch { return null; }
    }

    private async Task ApplyAsync(PowerFlowConfig updated)
    {
        if (_apply is null) return;
        await _apply(updated);
        RefreshConfig(updated);
    }
}
