using System.ComponentModel;
using System.Runtime.CompilerServices;
using PowerFlow.App.Controller;
using PowerFlow.App.Telemetry;
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
    private IReadOnlyList<TransitionRecord> _history = Array.Empty<TransitionRecord>();

    public event PropertyChangedEventHandler? PropertyChanged;

    public string StateLabel => _snapshot switch
    {
        { IsLatched: true, LatchType: "Manual", State: PowerState.PowerSaver } => "POWER SAVER LOCKED",
        { IsLatched: true, LatchType: "Manual", State: PowerState.Balanced } => "BALANCED LOCKED",
        { IsLatched: true, State: PowerState.HighPerformance } => "PERFORMANCE LOCKED",
        { State: PowerState.PowerSaver } => "POWER SAVER",
        { State: PowerState.Balanced } => "BALANCED",
        { State: PowerState.HighPerformance } => "PERFORMANCE",
        _ => "STARTING"
    };

    public string Reason => _snapshot?.Reason ?? "Initializing controller";
    public string BadgeLabel => _snapshot?.LatchType ?? (_snapshot?.CooldownRemaining is not null ? "Cooldown" : "Automatic");
    public string TriggerApplication => string.IsNullOrWhiteSpace(_snapshot?.TriggerApplication) ? "No explicit trigger" : _snapshot!.TriggerApplication!;
    public double CpuPercent => Math.Clamp(_snapshot?.CpuPercent ?? 0, 0, 100);
    public string CpuLabel => _snapshot is null ? "—" : $"{_snapshot.CpuPercent:0.0}%";
    public string WattsLabel => _telemetry?.PackageWatts is double watts ? $"{watts:0.0} W" : "—";
    public string FrequencyLabel => _telemetry?.AverageMhz is double mhz ? $"{mhz / 1000d:0.00} GHz" : "—";
    public bool IsPowerSaverSelected => _snapshot?.State == PowerState.PowerSaver;
    public bool IsBalancedSelected => _snapshot?.State == PowerState.Balanced;
    public bool IsPerformanceSelected => _snapshot?.State == PowerState.HighPerformance;
    public bool IsAutoSelected => _snapshot is null || !(_snapshot.IsLatched && string.Equals(_snapshot.LatchType, "Manual", StringComparison.OrdinalIgnoreCase));
    public bool ManualModeEnabled => !(_snapshot?.IsLatched == true && string.Equals(_snapshot.LatchType, "Game", StringComparison.OrdinalIgnoreCase));
    public string ControlBadgeLabel => _snapshot switch
    {
        { IsLatched: true, LatchType: "Manual" } => "MANUAL LOCK",
        { IsLatched: true, LatchType: "Game" } => "GAME LATCH",
        _ => "AUTO"
    };
    public string AutoModeDetail => _snapshot switch
    {
        { IsLatched: true, LatchType: "Manual" } => "Release manual lock",
        { IsLatched: true, LatchType: "Game" } => "Game latch active",
        _ => "Rules · apps · smart"
    };
    public bool IsManualLatch => _snapshot?.IsLatched == true && string.Equals(_snapshot.LatchType, "Manual", StringComparison.OrdinalIgnoreCase);
    public string ControlOwnerLabel => _snapshot switch
    {
        { IsLatched: true, LatchType: "Manual" } => "Manual lock",
        { IsLatched: true, LatchType: "Game" } => "Game latch",
        _ => "Auto"
    };
    public string ControlDetailLabel => _snapshot switch
    {
        { IsLatched: true, LatchType: "Manual" } => $"Manual hold on {StateLabel.Replace(" LOCKED", "", StringComparison.Ordinal)}",
        { IsLatched: true, LatchType: "Game", TriggerApplication: { Length: > 0 } app } => $"Held by {app}",
        { IsLatched: true, LatchType: "Game" } => "Held until the tracked game exits",
        _ => "Rules and sustained load choose the state"
    };
    public string CauseLabel => string.IsNullOrWhiteSpace(_snapshot?.TriggerApplication)
        ? Reason
        : $"{_snapshot!.TriggerApplication} - {Reason}";
    public string CooldownLabel => _snapshot?.CooldownRemaining is { } remaining && remaining > TimeSpan.Zero
        ? $"Cooldown {Math.Ceiling(remaining.TotalSeconds):0}s"
        : "No cooldown";
    public string PolicyProgressLabel => _snapshot is null
        ? "Establishing policy"
        : $"{Math.Clamp(_snapshot.ThresholdProgress, 0, 1) * 100:0}% toward decision";
    public IReadOnlyList<string> ActiveSourceLines
    {
        get
        {
            var lines = new List<string>();
            if (!string.IsNullOrWhiteSpace(_snapshot?.TriggerApplication))
                lines.Add($"Trigger: {_snapshot!.TriggerApplication}");
            foreach (var rule in RuleCards.Where(x => x.Status is "ACTIVE" or "LOCKED").Take(3))
                lines.Add($"{rule.Title}: {rule.Status}");
            if (lines.Count == 0) lines.Add("No explicit active source");
            return lines;
        }
    }
    public double FlowProgress => Math.Clamp(_snapshot?.ThresholdProgress ?? 0, 0, 1);
    public IReadOnlyList<TransitionRecord> History => _history;
    public IReadOnlyList<string> RecentEventLines => _history.Take(4).Select(FormatTransition).ToArray();
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
        RaiseAll(historyChanged: false);
    }

    public void UpdateContinuity(ControllerSnapshot snapshot, IReadOnlyList<ContinuitySample> continuity, DashboardTelemetry? telemetry)
    {
        var projectedHistory = snapshot.History.Reverse().ToArray();
        var historyChanged = projectedHistory.Length != _history.Count || !projectedHistory.SequenceEqual(_history);
        if (historyChanged) _history = projectedHistory;
        _snapshot = snapshot;
        _telemetry = telemetry;
        _samples.Clear();
        foreach (var sample in continuity.OrderBy(x => x.At))
            _samples.Add(new DashboardSample(sample.At, sample.CpuPercent, sample.PackageWatts, sample.AverageMhz, sample.State));
        _lastSampleAt = _samples.Count > 0 ? _samples[^1].At : null;
        RaiseAll(historyChanged);
    }
    public void Update(ControllerSnapshot snapshot, DashboardTelemetry? telemetry)
    {
        var projectedHistory = snapshot.History.Reverse().ToArray();
        var historyChanged = projectedHistory.Length != _history.Count || !projectedHistory.SequenceEqual(_history);
        if (historyChanged) _history = projectedHistory;

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
        RaiseAll(historyChanged);
    }

    private void RaiseAll(bool historyChanged)
    {
        OnPropertyChanged(nameof(StateLabel));
        OnPropertyChanged(nameof(Reason));
        OnPropertyChanged(nameof(BadgeLabel));
        OnPropertyChanged(nameof(TriggerApplication));
        OnPropertyChanged(nameof(CpuLabel));
        OnPropertyChanged(nameof(WattsLabel));
        OnPropertyChanged(nameof(FrequencyLabel));
        OnPropertyChanged(nameof(IsPowerSaverSelected));
        OnPropertyChanged(nameof(IsBalancedSelected));
        OnPropertyChanged(nameof(IsPerformanceSelected));
        OnPropertyChanged(nameof(IsAutoSelected));
        OnPropertyChanged(nameof(ManualModeEnabled));
        OnPropertyChanged(nameof(ControlBadgeLabel));
        OnPropertyChanged(nameof(AutoModeDetail));
        OnPropertyChanged(nameof(CpuPercent));
        OnPropertyChanged(nameof(IsManualLatch));
        OnPropertyChanged(nameof(ControlOwnerLabel));
        OnPropertyChanged(nameof(ControlDetailLabel));
        OnPropertyChanged(nameof(CauseLabel));
        OnPropertyChanged(nameof(CooldownLabel));
        OnPropertyChanged(nameof(PolicyProgressLabel));
        OnPropertyChanged(nameof(ActiveSourceLines));
        OnPropertyChanged(nameof(FlowProgress));
        if (historyChanged) OnPropertyChanged(nameof(History));
        if (historyChanged) OnPropertyChanged(nameof(RecentEventLines));
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
    private static string FormatTransition(TransitionRecord transition)
        => $"{transition.At:HH:mm}  {StateName(transition.To)} — {transition.Reason}";

    private static string StateName(PowerState state) => state switch
    {
        PowerState.PowerSaver => "Power Saver",
        PowerState.Balanced => "Balanced",
        PowerState.HighPerformance => "Performance",
        _ => state.ToString()
    };
}
