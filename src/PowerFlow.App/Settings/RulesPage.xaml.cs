using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
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
        public string ImportanceLabel => Rule.Mode == AppRuleMode.Ultra ? "ULTRA" : Rule.EffectiveImportance.ToString().ToUpperInvariant();
        public string PolicyLabel => Rule.Mode == AppRuleMode.Ultra
            ? "Forces ULTRA while this app is active; restores the prior Auto behavior when it exits"
            : Rule.EffectiveImportance switch
        {
            AppImportance.Low => "Efficiency-biased · cannot promote Auto into Boost by itself",
            AppImportance.High => "Latency-sensitive · shorter qualification for Boost",
            _ => "Default · may earn qualified Boost after sustained demand"
        };
    }

    private PowerFlowConfig _config = PowerFlowConfig.Default;
    private Func<PowerFlowConfig, Task>? _apply;
    private Func<Task<string?>>? _browseExecutable;
    private readonly RunningAppCatalog _catalog = new();
    private IReadOnlyList<AppPickerItem> _pickerItems = Array.Empty<AppPickerItem>();
    private AppRule? _editingRule;

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
        RulesRepeater.ItemsSource = config.AppRules.Select(rule => new RuleCardItem(rule)).ToArray();
    }

    private async void OnAddAppRule(object sender, RoutedEventArgs e)
    {
        AppSearchBox.Text = "";
        NewAppImportanceBox.SelectedIndex = 1;
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
        try { await AddOrReplaceRuleAsync(selected.ExecutablePath, selected.DisplayName, NewAppImportanceBox.SelectedIndex); }
        finally { deferral.Complete(); }
    }

    private async void OnEditImportanceFromCard(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not AppRule selected) return;
        _editingRule = selected;
        AppImportanceBox.SelectedIndex = IndexFor(selected);
        ImportanceAppText.Text = selected.DisplayName ?? Path.GetFileNameWithoutExtension(selected.ExecutablePath);
        ImportanceDialog.XamlRoot = XamlRoot;
        await ImportanceDialog.ShowAsync();
    }

    private async void OnImportancePrimary(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if (_editingRule is not AppRule selected) { args.Cancel = true; return; }
        var deferral = args.GetDeferral();
        try
        {
            var selectionIndex = AppImportanceBox.SelectedIndex;
            var updated = RuleForSelection(selected.ExecutablePath, selected.DisplayName, selected.FollowChildren, selectionIndex);
            var rules = _config.AppRules.Select(rule => string.Equals(rule.ExecutablePath, selected.ExecutablePath, StringComparison.OrdinalIgnoreCase) ? updated : rule).ToArray();
            await ApplyAsync(_config with { AppRules = rules });
            StatusText.Text = $"Saved {ImportanceAppText.Text}: {(selectionIndex == 3 ? "ULTRA" : ImportanceFromIndex(selectionIndex).ToString())}.";
        }
        finally { _editingRule = null; deferral.Complete(); }
    }

    private async void OnRemoveFromCard(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not AppRule selected) return;
        var rules = _config.AppRules.Where(x => !string.Equals(x.ExecutablePath, selected.ExecutablePath, StringComparison.OrdinalIgnoreCase)).ToArray();
        await ApplyAsync(_config with { AppRules = rules });
        StatusText.Text = "Rule removed.";
    }

    private async Task AddOrReplaceRuleAsync(string path, string displayName, int selectionIndex)
    {
        var rules = _config.AppRules.Where(x => !string.Equals(x.ExecutablePath, path, StringComparison.OrdinalIgnoreCase)).ToList();
        rules.Add(RuleForSelection(path, displayName, followChildren: true, selectionIndex));
        await ApplyAsync(_config with { AppRules = rules });
        StatusText.Text = $"Saved {displayName}: {(selectionIndex == 3 ? "ULTRA" : ImportanceFromIndex(selectionIndex).ToString())}.";
    }

    private static AppRule RuleForSelection(string path, string? displayName, bool followChildren, int selectionIndex)
    {
        if (selectionIndex == 3)
            return new AppRule(path, AppRuleMode.Ultra, displayName, followChildren, Entitlement: null, Importance: null);
        var importance = ImportanceFromIndex(selectionIndex);
        var legacyMode = importance == AppImportance.Low ? AppRuleMode.Balanced : AppRuleMode.Performance;
        return new AppRule(path, legacyMode, displayName, followChildren, Entitlement: null, Importance: importance);
    }
    private static AppImportance ImportanceFromIndex(int index) => index switch
    {
        0 => AppImportance.Low,
        2 => AppImportance.High,
        _ => AppImportance.Normal
    };

    private static int IndexFor(AppRule rule) => rule.Mode == AppRuleMode.Ultra ? 3 : IndexFor(rule.EffectiveImportance);

    private static int IndexFor(AppImportance importance) => importance switch
    {
        AppImportance.Low => 0,
        AppImportance.High => 2,
        _ => 1
    };

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
