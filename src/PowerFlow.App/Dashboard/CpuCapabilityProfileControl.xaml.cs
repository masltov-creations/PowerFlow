using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PowerFlow.App.Controller;
using PowerFlow.Core.Profiling;

namespace PowerFlow.App.Dashboard;

public sealed partial class CpuCapabilityProfileControl : UserControl
{
    private sealed record ProfileChoice(CpuCapabilityProfile Profile)
    {
        public override string ToString() => $"{Profile.PolicyLabel} · {Profile.CapturedAt:MMM d HH:mm}";
    }

    private sealed record BaselineChoice(MachineBaselineComparisonRun Run)
    {
        public override string ToString() => $"{Run.CapturedAt:MMM d HH:mm} · {Run.Recommendation.RecommendedMode}";
    }

    private sealed record ProfilePointRow(CpuCapabilityPoint Point, double MaxThroughput)
    {
        public string ThreadLabel => $"{Point.WorkerCount}T";
        public double RelativeThroughputPercent => MaxThroughput <= 0 ? 0 : Point.ThroughputMops / MaxThroughput * 100d;
        public string ThroughputLabel => $"{Point.ThroughputMops:0.0} M/s";
        public string PowerLabel => Point.PackageWatts is double watts ? $"{watts:0.0} W" : "—";
        public string SpeedLabel => Point.SpeedGhz is double ghz ? $"{ghz:0.00} GHz" : "—";
        public string EfficiencyLabel => Point.ThroughputPerWatt is double efficiency ? $"{efficiency:0.00} M/W" : "—";
    }

    private sealed record IdleRow(string Label, double RelativePercent, string WattsLabel);
    private sealed record ModeRow(string Label, string IdleLabel, string SingleLabel, string KneeLabel, string MaxLabel, string EfficiencyLabel);

    private readonly CpuCapabilityProfiler _profiler = new();
    private readonly List<CpuCapabilityProfile> _profiles = [];
    private readonly List<MachineBaselineComparisonRun> _baselineRuns = [];
    private Func<CpuCapabilityProfile, Task>? _saveProfile;
    private MachineBaselineSessionRuntime? _baselineSession;
    private CancellationTokenSource? _quickCts;
    private DispatcherQueueTimer? _progressTimer;
    private bool _sessionSubscribed;

    public CpuCapabilityProfileControl()
    {
        InitializeComponent();
        Unloaded += (_, _) => StopProgressTimer();
    }

    public void Initialize(
        IReadOnlyList<CpuCapabilityProfile> profiles,
        Func<CpuCapabilityProfile, Task> saveProfile,
        IReadOnlyList<MachineBaselineComparisonRun>? baselineRuns = null,
        MachineBaselineSessionRuntime? baselineSession = null)
    {
        Detach();
        _saveProfile = saveProfile;
        _profiles.Clear();
        _profiles.AddRange(profiles.OrderByDescending(profile => profile.CapturedAt));
        _baselineRuns.Clear();
        _baselineRuns.AddRange((baselineRuns ?? Array.Empty<MachineBaselineComparisonRun>()).OrderByDescending(run => run.CapturedAt));
        _baselineSession = baselineSession;
        if (_baselineSession is not null)
        {
            _baselineSession.Changed += OnBaselineSessionChanged;
            _sessionSubscribed = true;
        }
        RunBaselineButton.IsEnabled = baselineSession is not null;
        if (baselineSession is null)
        {
            BaselineProgressText.Text = "Baseline unavailable in preview/read-only mode";
            BaselineRemainingText.Text = "—";
        }
        RefreshProfileChoices(_profiles.FirstOrDefault());
        var initialRun = baselineSession?.Snapshot.LastCompletedRun ?? _baselineRuns.FirstOrDefault();
        if (initialRun is not null) UpsertBaseline(initialRun);
        RefreshBaselineChoices(initialRun);
        if (baselineSession is not null) RenderSession(baselineSession.Snapshot);
    }

    public void Detach()
    {
        if (_sessionSubscribed && _baselineSession is not null) _baselineSession.Changed -= OnBaselineSessionChanged;
        _sessionSubscribed = false;
        StopProgressTimer();
    }

    public void CancelActive()
    {
        _quickCts?.Cancel();
        _baselineSession?.Cancel();
    }

    private void OnRunBaseline(object sender, RoutedEventArgs e)
    {
        if (_baselineSession is null || _baselineSession.Snapshot.IsRunning) return;
        _ = _baselineSession.StartAsync(MachineBaselineSchedule.Standard);
        StartProgressTimer();
    }

    private void OnCancel(object sender, RoutedEventArgs e) => CancelActive();

    private void OnBaselineSessionChanged(object? sender, MachineBaselineSessionSnapshot snapshot)
    {
        DispatcherQueue.TryEnqueue(() => RenderSession(snapshot));
    }

    private void RenderSession(MachineBaselineSessionSnapshot snapshot)
    {
        RunBaselineButton.IsEnabled = _baselineSession is not null && !snapshot.IsRunning;
        RunProfileButton.IsEnabled = !snapshot.IsRunning && _quickCts is null;
        CancelButton.IsEnabled = snapshot.IsRunning || _quickCts is not null;
        BaselineProgressText.Text = snapshot.Progress?.Message ?? snapshot.Status;
        BaselineStatusText.Text = snapshot.Error ?? (snapshot.Progress is { } p ? $"MODE {p.ModeIndex}/{p.ModeCount} / {p.Mode} / {p.Stage}" : "WINDOWS SAVER / WINDOWS BALANCED / PF SAVER / BAL-E / BAL-P / PERF / ULTRA / AUTO");
        if (snapshot.LastCompletedRun is { } run)
        {
            UpsertBaseline(run);
            RefreshBaselineChoices(run);
        }
        if (snapshot.IsRunning) StartProgressTimer(); else StopProgressTimer();
        UpdateProgressClock(snapshot);
    }

    private void StartProgressTimer()
    {
        if (_progressTimer is not null) return;
        _progressTimer = DispatcherQueue.CreateTimer();
        _progressTimer.Interval = TimeSpan.FromSeconds(1);
        _progressTimer.Tick += OnProgressTick;
        _progressTimer.Start();
    }

    private void StopProgressTimer()
    {
        if (_progressTimer is null) return;
        _progressTimer.Stop();
        _progressTimer.Tick -= OnProgressTick;
        _progressTimer = null;
    }

    private void OnProgressTick(DispatcherQueueTimer sender, object args)
    {
        if (_baselineSession is not null) UpdateProgressClock(_baselineSession.Snapshot);
    }

    private void UpdateProgressClock(MachineBaselineSessionSnapshot snapshot)
    {
        if (snapshot.StartedAt is not DateTimeOffset started || snapshot.TargetDuration is not TimeSpan target || target <= TimeSpan.Zero)
        {
            if (!snapshot.IsRunning && snapshot.LastCompletedRun is not null) BaselineProgressBar.Value = 100;
            return;
        }
        var elapsed = DateTimeOffset.UtcNow - started;
        var remaining = target - elapsed;
        if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;
        BaselineProgressBar.Value = Math.Clamp(elapsed.TotalSeconds / target.TotalSeconds * 100d, 0d, 100d);
        BaselineRemainingText.Text = snapshot.IsRunning ? $"{(int)remaining.TotalMinutes:00}:{remaining.Seconds:00} left" : snapshot.Status;
    }

    private void OnSavedBaselineChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SavedBaselineBox.SelectedItem is BaselineChoice choice) RenderBaseline(choice.Run);
    }

    private void OnComparisonMetricChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SavedBaselineBox is not null && SavedBaselineBox.SelectedItem is BaselineChoice choice) RenderBaseline(choice.Run);
    }

    private void UpsertBaseline(MachineBaselineComparisonRun run)
    {
        _baselineRuns.RemoveAll(existing => existing.Id == run.Id);
        _baselineRuns.Add(run);
        _baselineRuns.Sort((a, b) => b.CapturedAt.CompareTo(a.CapturedAt));
        if (_baselineRuns.Count > MachineBaselineHistory.MaximumRuns) _baselineRuns.RemoveRange(MachineBaselineHistory.MaximumRuns, _baselineRuns.Count - MachineBaselineHistory.MaximumRuns);
    }

    private void RefreshBaselineChoices(MachineBaselineComparisonRun? selected)
    {
        var choices = _baselineRuns.Select(run => new BaselineChoice(run)).ToArray();
        SavedBaselineBox.ItemsSource = choices;
        var choice = selected is null ? choices.FirstOrDefault() : choices.FirstOrDefault(item => item.Run.Id == selected.Id) ?? choices.FirstOrDefault();
        SavedBaselineBox.SelectedItem = choice;
        if (choice is not null) RenderBaseline(choice.Run);
    }

    private void RenderBaseline(MachineBaselineComparisonRun run)
    {
        var metric = ComparisonMetricBox.SelectedIndex switch
        {
            1 => MachineBaselineComparisonMetric.Efficiency,
            2 => MachineBaselineComparisonMetric.PackagePower,
            _ => MachineBaselineComparisonMetric.Throughput
        };
        BaselineComparisonChart.SetRun(run, metric);
        ComparisonSummaryText.Text = $"{run.CapturedAt:g} / {run.Results.Count} matched five-minute legs / {MetricDescription(metric)}";

        var recommendation = run.Recommendation;
        var recommendedLabel = LabelFor(run, recommendation.RecommendedMode);
        var saved = recommendation.IdleWattsAvoidedVsWindowsBalanced is double watts
            ? $" It used {Math.Abs(watts):0.0} W {(watts >= 0 ? "less" : "more")} idle package power than native Windows Balanced."
            : string.Empty;
        RecommendationText.Text = $"USE {recommendedLabel}";
        RecommendationReasonText.Text = recommendation.Reason + saved;
        BestSingleThreadText.Text = LabelFor(run, recommendation.BestSingleThreadMode);
        BestEfficiencyText.Text = LabelFor(run, recommendation.BestEfficiencyMode);

        var maxIdle = run.Results.Where(result => result.Idle.AveragePackageWatts is not null).Select(result => result.Idle.AveragePackageWatts!.Value).DefaultIfEmpty(1).Max();
        IdleComparisonRepeater.ItemsSource = run.Results.Select(result => new IdleRow(
            result.Label,
            result.Idle.AveragePackageWatts is double idle ? idle / Math.Max(1, maxIdle) * 100d : 0,
            result.Idle.AveragePackageWatts is double wattsValue ? $"{wattsValue:0.0} W" : "—")).ToArray();

        ModeResultsRepeater.ItemsSource = run.Results.Select(result =>
        {
            var one = result.Benchmark.Points.FirstOrDefault(point => point.WorkerCount == 1)?.ThroughputMops;
            var bestEfficiency = result.Benchmark.Points.Where(point => point.ThroughputPerWatt is not null).Select(point => point.ThroughputPerWatt!.Value).DefaultIfEmpty().Max();
            return new ModeRow(
                result.Label,
                result.Idle.AveragePackageWatts is double idle ? $"{idle:0.0}W" : "—",
                one is double single ? $"{single:0.0}" : "—",
                $"{result.Benchmark.KneeWorkers}T",
                $"{result.MaxThroughputMops:0.0}",
                bestEfficiency > 0 ? $"{bestEfficiency:0.00}" : "—");
        }).ToArray();
    }

    private static string LabelFor(MachineBaselineComparisonRun run, MachineBaselineMode mode)
        => run.Results.FirstOrDefault(result => result.Mode == mode)?.Label ?? mode.ToString();

    private static string MetricDescription(MachineBaselineComparisonMetric metric) => metric switch
    {
        MachineBaselineComparisonMetric.Efficiency => "higher means more synthetic CPU throughput per watt",
        MachineBaselineComparisonMetric.PackagePower => "lower watts for the same throughput is better",
        _ => "higher means more synthetic CPU work completed per second"
    };

    private async void OnRunProfile(object sender, RoutedEventArgs e)
    {
        if (_quickCts is not null || _baselineSession?.Snapshot.IsRunning == true) return;
        _quickCts = new CancellationTokenSource();
        RunProfileButton.IsEnabled = false;
        RunBaselineButton.IsEnabled = false;
        CancelButton.IsEnabled = true;
        var progress = new Progress<CpuCapabilityProgress>(value => ProfileProgressText.Text = $"{value.Step}/{value.TotalSteps} · {value.Message}");
        try
        {
            var profile = await _profiler.RunAsync(progress, _quickCts.Token);
            UpsertProfile(profile);
            if (_saveProfile is not null) await _saveProfile(profile);
            RefreshProfileChoices(profile);
            ProfileProgressText.Text = $"Captured {profile.PolicyLabel}.";
        }
        catch (OperationCanceledException) { ProfileProgressText.Text = "Current-mode profile cancelled."; }
        catch (Exception ex) { ProfileProgressText.Text = $"Profile failed: {ex.Message}"; }
        finally
        {
            _quickCts.Dispose();
            _quickCts = null;
            RunProfileButton.IsEnabled = _baselineSession?.Snapshot.IsRunning != true;
            RunBaselineButton.IsEnabled = _baselineSession is not null && _baselineSession.Snapshot.IsRunning != true;
            CancelButton.IsEnabled = _baselineSession?.Snapshot.IsRunning == true;
        }
    }

    private void OnSavedProfileChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SavedProfileBox.SelectedItem is ProfileChoice choice) RenderProfile(choice.Profile);
    }

    private void UpsertProfile(CpuCapabilityProfile profile)
    {
        _profiles.RemoveAll(existing => string.Equals(existing.PolicySignature, profile.PolicySignature, StringComparison.OrdinalIgnoreCase));
        _profiles.Add(profile);
        _profiles.Sort((a, b) => b.CapturedAt.CompareTo(a.CapturedAt));
    }

    private void RefreshProfileChoices(CpuCapabilityProfile? selected)
    {
        var choices = _profiles.Select(profile => new ProfileChoice(profile)).ToArray();
        SavedProfileBox.ItemsSource = choices;
        var choice = selected is null ? choices.FirstOrDefault() : choices.FirstOrDefault(item => item.Profile.PolicySignature == selected.PolicySignature) ?? choices.FirstOrDefault();
        SavedProfileBox.SelectedItem = choice;
        if (choice is not null) RenderProfile(choice.Profile);
    }

    private void RenderProfile(CpuCapabilityProfile profile)
    {
        ProfileSummaryText.Text = $"{profile.PolicyLabel}: efficiency {profile.BestEfficiencyWorkers}T · knee {profile.KneeWorkers}T · max {profile.MaxThroughputWorkers}T";
        ProfileDetailText.Text = $"Captured {profile.CapturedAt:g}. {profile.PolicySignature}";
        var max = profile.Points.Count == 0 ? 1d : profile.Points.Max(point => point.ThroughputMops);
        ProfilePointsRepeater.ItemsSource = profile.Points.Select(point => new ProfilePointRow(point, max)).ToArray();
    }
}