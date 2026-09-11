using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using PowerFlow.Core.Envelope;
using PowerFlow.Core.Rules;
using PowerFlow.Windows.Apps;
using PowerFlow.Windows.Services;
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

    private sealed record ServiceCardItem(RunningServiceInfo Service, ServicePolicyRule Policy, bool ExplicitPolicy)
    {
        public string DisplayName => Service.DisplayName;
        public string IdentityLabel => $"{Service.Name} - PID {Service.ProcessId}";
        public string ImportanceLabel => Policy.Importance.ToString().ToUpperInvariant();
        public string BoostLabel => $"BOOST {Policy.BoostEntitlement.ToString().ToUpperInvariant()}";
        public string SafetyLabel => Service.IsSharedHost
            ? $"SHARED HOST ({Service.ServicesInHost}) - ADVISORY"
            : ExplicitPolicy ? "EXPLICIT POLICY - ADVISORY" : "DEFAULT POLICY - ADVISORY";
    }

    private PowerFlowConfig _config = PowerFlowConfig.Default;
    private Func<PowerFlowConfig, Task>? _apply;
    private Func<Task<string?>>? _browseExecutable;
    private readonly RunningAppCatalog _catalog = new();
    private readonly WindowsServiceCatalog _serviceCatalog = new();
    private IReadOnlyList<RunningServiceInfo> _services = Array.Empty<RunningServiceInfo>();
    private IReadOnlyList<AppPickerItem> _pickerItems = Array.Empty<AppPickerItem>();
    private AppRule? _editingRule;
    private ServiceCardItem? _editingService;
    private bool _refreshing;

    public RulesPage() => InitializeComponent();

    public void Initialize(PowerFlowConfig config, Func<PowerFlowConfig, Task> apply, Func<Task<string?>> browseExecutable)
    {
        _apply = apply;
        _browseExecutable = browseExecutable;
        RefreshConfig(config);
        _ = RefreshServicesAsync();
    }

    public void RefreshConfig(PowerFlowConfig config)
    {
        _config = config;
        _refreshing = true;
        try
        {
            AdaptiveActuationToggle.IsOn = config.AdaptiveActuationEnabled;
            RulesRepeater.ItemsSource = config.AppRules.Select(rule => new RuleCardItem(rule)).ToArray();
            ServiceRepeater.ItemsSource = BuildServiceCards(_services, config);
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

    private async void OnRefreshServices(object sender, RoutedEventArgs e) => await RefreshServicesAsync();

    private async Task RefreshServicesAsync()
    {
        StatusText.Text = "Reading running Windows services...";
        try
        {
            _services = await Task.Run(() => _serviceCatalog.ListRunning());
            ServiceRepeater.ItemsSource = BuildServiceCards(_services, _config);
            StatusText.Text = $"Observed {_services.Count} running services. Service boost rules are advisory until identity-safe actuation is qualified.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Service inventory unavailable: {ex.Message}";
        }
    }

    private static IReadOnlyList<ServiceCardItem> BuildServiceCards(IReadOnlyList<RunningServiceInfo> services, PowerFlowConfig config)
    {
        return services.Select(service =>
        {
            var explicitRule = config.EffectiveServiceRules.FirstOrDefault(rule => string.Equals(rule.ServiceName, service.Name, StringComparison.OrdinalIgnoreCase));
            var defaults = WorkloadPolicyDefaults.ForService(service.Name, service.StartType, service.IsSharedHost);
            var policy = explicitRule ?? new ServicePolicyRule(service.Name, defaults.Importance, defaults.Boost, service.DisplayName);
            return new ServiceCardItem(service, policy, explicitRule is not null);
        }).OrderBy(item => item.Policy.Importance).ThenBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private async void OnEditServicePolicy(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not ServiceCardItem selected) return;
        _editingService = selected;
        ServicePolicyNameText.Text = selected.DisplayName;
        ServicePolicySafetyText.Text = selected.Service.IsSharedHost
            ? $"{selected.Service.Name} shares PID {selected.Service.ProcessId} with {selected.Service.ServicesInHost} services. PowerFlow will not translate this into a process-wide svchost boost."
            : $"{selected.Service.Name} currently runs in PID {selected.Service.ProcessId}. Policy is stored now; service-specific actuation remains advisory until qualified.";
        ServiceImportanceBox.SelectedIndex = selected.Policy.Importance switch
        {
            WorkloadImportance.Critical => 0, WorkloadImportance.Interactive => 1, WorkloadImportance.Important => 2, _ => 3
        };
        ServiceBoostBox.SelectedIndex = selected.Policy.BoostEntitlement switch
        {
            CpuBoostEntitlement.Allow => 0, CpuBoostEntitlement.Conditional => 1, _ => 2
        };
        ServicePolicyDialog.XamlRoot = XamlRoot;
        await ServicePolicyDialog.ShowAsync();
    }

    private async void OnServicePolicyPrimary(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if (_editingService is not ServiceCardItem selected) { args.Cancel = true; return; }
        var deferral = args.GetDeferral();
        try
        {
            var importance = ServiceImportanceBox.SelectedIndex switch
            {
                0 => WorkloadImportance.Critical, 1 => WorkloadImportance.Interactive, 2 => WorkloadImportance.Important, _ => WorkloadImportance.Background
            };
            var boost = ServiceBoostBox.SelectedIndex switch
            {
                0 => CpuBoostEntitlement.Allow, 1 => CpuBoostEntitlement.Conditional, _ => CpuBoostEntitlement.Deny
            };
            var rules = _config.EffectiveServiceRules.Where(rule => !string.Equals(rule.ServiceName, selected.Service.Name, StringComparison.OrdinalIgnoreCase)).ToList();
            rules.Add(new ServicePolicyRule(selected.Service.Name, importance, boost, selected.Service.DisplayName));
            await ApplyAsync(_config with { ServiceRules = rules });
            StatusText.Text = $"Saved {selected.Service.DisplayName}: {importance}, boost {boost}. Service actuation remains advisory.";
        }
        finally
        {
            _editingService = null;
            deferral.Complete();
        }
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
