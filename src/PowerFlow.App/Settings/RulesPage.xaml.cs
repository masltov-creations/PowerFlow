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

    private PowerFlowConfig _config = PowerFlowConfig.Default;
    private Func<PowerFlowConfig, Task>? _apply;
    private Func<Task<string?>>? _browseExecutable;
    private readonly RunningAppCatalog _catalog = new();
    private IReadOnlyList<AppPickerItem> _pickerItems = Array.Empty<AppPickerItem>();

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
        RulesRepeater.ItemsSource = config.AppRules.ToArray();
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

    private static async Task<IReadOnlyList<AppPickerItem>> BuildPickerItemsAsync(IReadOnlyList<RunningAppOption> apps)
    {
        var items = new List<AppPickerItem>(apps.Count);
        foreach (var app in apps)
            items.Add(new AppPickerItem(app, await LoadAppIconAsync(app.ExecutablePath)));
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
        catch
        {
            return null;
        }
    }

    private async Task AddOrReplaceRuleAsync(string path, string displayName, AppRuleMode mode)
    {
        var rules = _config.AppRules.Where(x => !string.Equals(x.ExecutablePath, path, StringComparison.OrdinalIgnoreCase)).ToList();
        rules.Add(new AppRule(path, mode, displayName, true));
        await ApplyAsync(_config with { AppRules = rules });
        StatusText.Text = $"Saved {displayName} as {mode}.";
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
