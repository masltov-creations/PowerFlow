using System.ComponentModel;
using System.Runtime.CompilerServices;
using PowerFlow.App.Controller;
using PowerFlow.Core.Policy;
using PowerFlow.Core.Rules;
using PowerFlow.Windows.Activity;

namespace PowerFlow.App.Dashboard;

public sealed class DashboardViewModel : INotifyPropertyChanged
{
    private readonly List<DashboardSample> _samples = [];
    private ControllerSnapshot? _snapshot;
    private DashboardTelemetry? _telemetry;
    private PowerFlowConfig _config = PowerFlowConfig.Default;
    private DateTimeOffset? _lastSampleAt;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string StateLabel => _snapshot switch
    {
        { IsLatched: true, State: PowerState.HighPerformance } => "PERFORMANCE LOCKED",
        { State: PowerState.PowerSaver } => "POWER SAVER",
        { State: PowerState.Balanced } => "BALANCED",
        { State: PowerState.HighPerformance } => "PERFORMANCE",
        _ => "STARTING"
    };

    public string Reason => _snapshot?.Reason ?? "Initializing controller";
    public string BadgeLabel => _snapshot?.LatchType ?? (_snapshot?.CooldownRemaining is not null ? "Cooldown" : "Automatic");
    public string TriggerApplication => string.IsNullOrWhiteSpace(_snapshot?.TriggerApplication) ? "No explicit trigger" : _snapshot!.TriggerApplication!;
    public string CpuLabel => _snapshot is null ? "-" : $"{_snapshot.CpuPercent:0.0}%";
    public string WattsLabel => _telemetry?.PackageWatts is double watts ? $"{watts:0.0} W" : "—";
    public string FrequencyLabel => _telemetry?.AverageMhz is double mhz ? $"{mhz / 1000d:0.00} GHz" : "—";
    public double FlowProgress => Math.Clamp(_snapshot?.ThresholdProgress ?? 0, 0, 1);
    public IReadOnlyList<TransitionRecord> History => _snapshot?.History ?? Array.Empty<TransitionRecord>();
    public IReadOnlyList<DashboardSample> Samples => _samples;
    public double PromotionThresholdPercent => _config.CpuPromotionThresholdPercent;
    public double QuietThresholdPercent => _config.QuietThresholdPercent;
    public string PromotionRuleLabel => $"CPU > {_config.CpuPromotionThresholdPercent:0.#}% for {_config.CpuPromotionWindow.TotalSeconds:0.#}s";
    public string QuietRuleLabel => $"CPU < {_config.QuietThresholdPercent:0.#}% for {_config.QuietWindow.TotalSeconds:0.#}s";
    public IReadOnlyList<DashboardRuleCard> RuleCards => _snapshot is null ? Array.Empty<DashboardRuleCard>() : DashboardRuleProjection.Create(_config, _snapshot);
    public DecisionMeterState? DecisionMeter => _snapshot is null ? null : DecisionMeterProjection.Create(_config, _snapshot);

    public string NextActionLabel => _snapshot switch
    {
        { IsLatched: true, LatchType: "Game" } => "Held until game exits",
        { IsLatched: true, LatchType: "Manual" } => "Held until you release",
        { CooldownRemaining: TimeSpan t } => $"Cooling down · {Math.Max(0, (int)Math.Ceiling(t.TotalSeconds))}s",
        { State: PowerState.PowerSaver } => "Watching for sustained load",
        { State: PowerState.Balanced } => "Returns to Power Saver when quiet",
        { State: PowerState.HighPerformance } => "Performance active",
        _ => "Establishing state"
    };

    public void Configure(PowerFlowConfig config)
    {
        _config = config;
        RaiseAll();
    }

    public void Update(ControllerSnapshot snapshot, DashboardTelemetry? telemetry)
    {
        _snapshot = snapshot;
        if (telemetry is not null)
        {
            _telemetry = telemetry;
            if (_lastSampleAt != telemetry.At)
            {
                _samples.Add(new DashboardSample(telemetry.At, snapshot.CpuPercent, telemetry.PackageWatts, telemetry.AverageMhz, snapshot.State));
                _lastSampleAt = telemetry.At;
                while (_samples.Count > 120) _samples.RemoveAt(0);
            }
        }
        RaiseAll();
    }

    private void RaiseAll()
    {
        OnPropertyChanged(nameof(StateLabel));
        OnPropertyChanged(nameof(Reason));
        OnPropertyChanged(nameof(BadgeLabel));
        OnPropertyChanged(nameof(TriggerApplication));
        OnPropertyChanged(nameof(CpuLabel));
        OnPropertyChanged(nameof(WattsLabel));
        OnPropertyChanged(nameof(FrequencyLabel));
        OnPropertyChanged(nameof(FlowProgress));
        OnPropertyChanged(nameof(History));
        OnPropertyChanged(nameof(Samples));
        OnPropertyChanged(nameof(PromotionThresholdPercent));
        OnPropertyChanged(nameof(QuietThresholdPercent));
        OnPropertyChanged(nameof(PromotionRuleLabel));
        OnPropertyChanged(nameof(QuietRuleLabel));
        OnPropertyChanged(nameof(RuleCards));
        OnPropertyChanged(nameof(DecisionMeter));
        OnPropertyChanged(nameof(NextActionLabel));
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
