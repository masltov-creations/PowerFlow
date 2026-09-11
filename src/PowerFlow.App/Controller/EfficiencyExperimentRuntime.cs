using PowerFlow.App.Telemetry;

namespace PowerFlow.App.Controller;

public enum EfficiencyExperimentPhase
{
    None,
    Baseline,
    After
}

public sealed record EfficiencyTracePoint(
    double ElapsedSeconds,
    double CumulativeCpuWorkMinutes,
    double CumulativeEnergyWh);

public sealed record EfficiencyPhaseSummary(
    TimeSpan WallDuration,
    TimeSpan ObservedDuration,
    double CoveragePercent,
    double CpuWorkMinutes,
    double EnergyWh,
    double WorkPerWh,
    double AveragePackageWatts,
    double AveragePressurePercent,
    double AverageQueueLength,
    double PeakPressurePercent,
    int SampleCount,
    IReadOnlyList<EfficiencyTracePoint> Trace)
{
    public static EfficiencyPhaseSummary Empty { get; } = new(
        TimeSpan.Zero, TimeSpan.Zero, 0, 0, 0, 0, 0, 0, 0, 0, 0, Array.Empty<EfficiencyTracePoint>());
}

public sealed record EfficiencyComparisonSummary(
    double? EquivalentBaselineEnergyWh,
    double? AvoidedEnergyWh,
    double? AvoidedEnergyPercent,
    double? WorkEfficiencyImprovementPercent,
    double? WorkRateChangePercent,
    double? PressureChangePoints,
    double? QueueChange);

public sealed record EfficiencyExperimentSnapshot(
    EfficiencyExperimentPhase ActivePhase,
    TimeSpan TargetDuration,
    DateTimeOffset? PhaseStartedAt,
    bool BaselineComplete,
    bool AfterComplete,
    EfficiencyPhaseSummary Baseline,
    EfficiencyPhaseSummary After,
    EfficiencyComparisonSummary Comparison);

public static class EfficiencyComparisonProjection
{
    // Rich telemetry normally arrives every 1 s while visible and every 5 s while hidden.
    // Intervals larger than this are treated as missing coverage rather than integrating
    // across suspend, telemetry loss, or an app restart.
    public static readonly TimeSpan MaximumIntegratedGap = TimeSpan.FromSeconds(15);

    public static EfficiencyPhaseSummary Summarize(IReadOnlyList<ContinuitySample>? samples)
    {
        if (samples is null || samples.Count == 0) return EfficiencyPhaseSummary.Empty;

        var valid = samples
            .Where(IsUsable)
            .OrderBy(sample => sample.At)
            .GroupBy(sample => sample.At)
            .Select(group => group.Last())
            .ToArray();
        if (valid.Length == 0) return EfficiencyPhaseSummary.Empty;
        if (valid.Length == 1)
            return EfficiencyPhaseSummary.Empty with
            {
                SampleCount = 1,
                Trace = new[] { new EfficiencyTracePoint(0, 0, 0) }
            };

        var first = valid[0].At;
        var last = valid[^1].At;
        var wallSeconds = Math.Max(0, (last - first).TotalSeconds);
        var observedSeconds = 0d;
        var workMinutes = 0d;
        var energyWh = 0d;
        var pressureWeighted = 0d;
        var queueWeighted = 0d;
        var peakPressure = valid.Max(sample => sample.DemandPressure!.PressurePercent);
        var trace = new List<EfficiencyTracePoint> { new(0, 0, 0) };

        for (var i = 1; i < valid.Length; i++)
        {
            var previous = valid[i - 1];
            var current = valid[i];
            var dt = (current.At - previous.At).TotalSeconds;
            if (dt <= 0 || dt > MaximumIntegratedGap.TotalSeconds) continue;

            var previousDemand = previous.DemandPressure!.DemandPercent;
            var currentDemand = current.DemandPressure!.DemandPercent;
            var averageDemand = (previousDemand + currentDemand) / 2d;
            var averageWatts = (previous.PackageWatts!.Value + current.PackageWatts!.Value) / 2d;
            var averagePressure = (previous.DemandPressure.PressurePercent + current.DemandPressure.PressurePercent) / 2d;
            var averageQueue = (previous.DemandPressure.QueueLength + current.DemandPressure.QueueLength) / 2d;

            observedSeconds += dt;
            // DemandPercent is already delivered processor utility on a full-machine nominal-capacity scale.
            // Integrating demand/100 over time yields nominal full-CPU equivalent minutes.
            workMinutes += Math.Max(0, averageDemand) / 100d * dt / 60d;
            energyWh += Math.Max(0, averageWatts) * dt / 3600d;
            pressureWeighted += Math.Max(0, averagePressure) * dt;
            queueWeighted += Math.Max(0, averageQueue) * dt;
            trace.Add(new EfficiencyTracePoint(
                Math.Max(0, (current.At - first).TotalSeconds),
                workMinutes,
                energyWh));
        }

        var observedMinutes = observedSeconds / 60d;
        return new EfficiencyPhaseSummary(
            TimeSpan.FromSeconds(wallSeconds),
            TimeSpan.FromSeconds(observedSeconds),
            wallSeconds > 0 ? Math.Clamp(observedSeconds / wallSeconds * 100d, 0d, 100d) : 0d,
            workMinutes,
            energyWh,
            energyWh > 0 ? workMinutes / energyWh : 0d,
            observedSeconds > 0 ? energyWh * 3600d / observedSeconds : 0d,
            observedSeconds > 0 ? pressureWeighted / observedSeconds : 0d,
            observedSeconds > 0 ? queueWeighted / observedSeconds : 0d,
            peakPressure,
            valid.Length,
            trace);
    }

    public static EfficiencyComparisonSummary Compare(EfficiencyPhaseSummary baseline, EfficiencyPhaseSummary after)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(after);

        if (baseline.CpuWorkMinutes <= 0 || baseline.EnergyWh <= 0 || after.CpuWorkMinutes <= 0 || after.EnergyWh <= 0)
            return new(null, null, null, null, null, null, null);

        var baselineEnergyPerWork = baseline.EnergyWh / baseline.CpuWorkMinutes;
        var equivalentBaselineEnergy = baselineEnergyPerWork * after.CpuWorkMinutes;
        var avoided = equivalentBaselineEnergy - after.EnergyWh;
        var avoidedPercent = equivalentBaselineEnergy > 0 ? avoided / equivalentBaselineEnergy * 100d : (double?)null;
        var efficiencyImprovement = baseline.WorkPerWh > 0 ? (after.WorkPerWh / baseline.WorkPerWh - 1d) * 100d : (double?)null;
        var baselineRate = baseline.ObservedDuration.TotalMinutes > 0 ? baseline.CpuWorkMinutes / baseline.ObservedDuration.TotalMinutes : 0d;
        var afterRate = after.ObservedDuration.TotalMinutes > 0 ? after.CpuWorkMinutes / after.ObservedDuration.TotalMinutes : 0d;
        var workRateChange = baselineRate > 0 ? (afterRate / baselineRate - 1d) * 100d : (double?)null;

        return new(
            equivalentBaselineEnergy,
            avoided,
            avoidedPercent,
            efficiencyImprovement,
            workRateChange,
            after.AveragePressurePercent - baseline.AveragePressurePercent,
            after.AverageQueueLength - baseline.AverageQueueLength);
    }

    private static bool IsUsable(ContinuitySample sample) =>
        sample.PackageWatts is double watts && double.IsFinite(watts) && watts >= 0
        && sample.DemandPressure is { } demand
        && double.IsFinite(demand.DemandPercent)
        && double.IsFinite(demand.PressurePercent)
        && double.IsFinite(demand.QueueLength);
}

public sealed class EfficiencyExperimentRuntime
{
    public static readonly TimeSpan MaximumDuration = TimeSpan.FromHours(5);
    public static readonly TimeSpan DefaultDuration = TimeSpan.FromMinutes(5);

    private readonly object _gate = new();
    private readonly List<ContinuitySample> _baseline = [];
    private readonly List<ContinuitySample> _after = [];
    private EfficiencyExperimentPhase _activePhase;
    private TimeSpan _targetDuration = DefaultDuration;
    private DateTimeOffset? _phaseStartedAt;
    private DateTimeOffset? _lastObservedAt;
    private bool _baselineComplete;
    private bool _afterComplete;

    public EfficiencyExperimentSnapshot Snapshot
    {
        get { lock (_gate) return SnapshotLocked(); }
    }

    public EfficiencyExperimentSnapshot StartBaseline(TimeSpan duration)
    {
        ValidateDuration(duration);
        lock (_gate)
        {
            _baseline.Clear();
            _after.Clear();
            _baselineComplete = false;
            _afterComplete = false;
            BeginLocked(EfficiencyExperimentPhase.Baseline, duration);
            return SnapshotLocked();
        }
    }

    public EfficiencyExperimentSnapshot StartAfter(TimeSpan duration)
    {
        ValidateDuration(duration);
        lock (_gate)
        {
            _after.Clear();
            _afterComplete = false;
            BeginLocked(EfficiencyExperimentPhase.After, duration);
            return SnapshotLocked();
        }
    }

    public EfficiencyExperimentSnapshot Stop()
    {
        lock (_gate)
        {
            CompleteActiveLocked();
            return SnapshotLocked();
        }
    }

    public EfficiencyExperimentSnapshot Reset()
    {
        lock (_gate)
        {
            _baseline.Clear();
            _after.Clear();
            _activePhase = EfficiencyExperimentPhase.None;
            _phaseStartedAt = null;
            _lastObservedAt = null;
            _baselineComplete = false;
            _afterComplete = false;
            _targetDuration = DefaultDuration;
            return SnapshotLocked();
        }
    }

    public EfficiencyExperimentSnapshot Observe(IReadOnlyList<ContinuitySample>? history)
    {
        if (history is null || history.Count == 0) return Snapshot;
        var latest = history.LastOrDefault(sample => sample.PackageWatts.HasValue && sample.DemandPressure is not null);
        if (latest is null) return Snapshot;

        lock (_gate)
        {
            if (_activePhase == EfficiencyExperimentPhase.None) return SnapshotLocked();
            if (_lastObservedAt is DateTimeOffset seen && latest.At <= seen) return SnapshotLocked();
            _lastObservedAt = latest.At;
            _phaseStartedAt ??= latest.At;

            var target = _activePhase == EfficiencyExperimentPhase.Baseline ? _baseline : _after;
            target.Add(latest);
            if (latest.At - _phaseStartedAt.Value >= _targetDuration) CompleteActiveLocked();
            return SnapshotLocked();
        }
    }

    private void BeginLocked(EfficiencyExperimentPhase phase, TimeSpan duration)
    {
        _activePhase = phase;
        _targetDuration = duration;
        _phaseStartedAt = null;
        _lastObservedAt = null;
    }

    private void CompleteActiveLocked()
    {
        if (_activePhase == EfficiencyExperimentPhase.Baseline) _baselineComplete = _baseline.Count > 1;
        if (_activePhase == EfficiencyExperimentPhase.After) _afterComplete = _after.Count > 1;
        _activePhase = EfficiencyExperimentPhase.None;
        _phaseStartedAt = null;
        _lastObservedAt = null;
    }

    private EfficiencyExperimentSnapshot SnapshotLocked()
    {
        var baseline = EfficiencyComparisonProjection.Summarize(_baseline);
        var after = EfficiencyComparisonProjection.Summarize(_after);
        return new(
            _activePhase,
            _targetDuration,
            _phaseStartedAt,
            _baselineComplete,
            _afterComplete,
            baseline,
            after,
            EfficiencyComparisonProjection.Compare(baseline, after));
    }

    private static void ValidateDuration(TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero || duration > MaximumDuration)
            throw new ArgumentOutOfRangeException(nameof(duration), $"Duration must be greater than zero and no more than {MaximumDuration.TotalHours:0} hours.");
    }
}