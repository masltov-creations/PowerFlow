using PowerFlow.App.Telemetry;
using PowerFlow.Core.Rules;
using PowerFlow.Windows.Power;

namespace PowerFlow.App.Controller;

public enum GraduatedCoreActuatorState
{
    Disabled,
    Armed,
    Holding,
    Applied,
    Suspended,
    Faulted
}

public sealed record GraduatedCoreActuatorStatus(
    GraduatedCoreActuatorState State,
    bool LiveWritesEnabled,
    uint? BaselineCoreFloorPercent,
    uint? CurrentCoreFloorPercent,
    uint? PlannedCoreFloorPercent,
    double? RequestedCapacityPercent,
    double? DeliveredCapacityPercent,
    string Message,
    DateTimeOffset At)
{
    public static GraduatedCoreActuatorStatus Disabled(string message, DateTimeOffset at)
        => new(GraduatedCoreActuatorState.Disabled, false, null, null, null, null, null, message, at);
}

public sealed record GraduatedCoreFloorControlInput(
    double RequestedCapacityPercent,
    double DeliveredCapacityPercent,
    double? AverageAwakePercentOfMaximumFrequency);

public sealed class GraduatedCoreFloorActuatorRuntime
{
    public const uint MaximumQualifiedCoreFloorPercent = 75;
    public static readonly TimeSpan MinimumWriteInterval = TimeSpan.FromSeconds(5);

    private readonly object _gate = new();
    private readonly IProcessorPolicyController _policy;
    private readonly Func<DateTimeOffset> _clock;
    private ProcessorPolicySnapshot? _baseline;
    private DateTimeOffset? _lastWriteAt;
    private bool _faultLatched;

    public GraduatedCoreFloorActuatorRuntime(IProcessorPolicyController? policy = null, Func<DateTimeOffset>? clock = null)
    {
        _policy = policy ?? new WindowsProcessorPolicyController();
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        Status = GraduatedCoreActuatorStatus.Disabled("Graduated core-floor actuation is disabled.", _clock());
    }

    public GraduatedCoreActuatorStatus Status { get; private set; }

    public GraduatedCoreActuatorStatus Evaluate(
        GraduatedCoreFloorControlInput? input,
        bool enabled,
        bool previewMode,
        bool coarseAdaptiveActuationEnabled,
        Guid qualifiedSchemeId)
    {
        lock (_gate)
        {
            var now = _clock();
            if (!enabled || previewMode || coarseAdaptiveActuationEnabled)
            {
                RestoreBaselineIfNeeded();
                var why = !enabled
                    ? "Graduated core-floor actuation is disabled."
                    : previewMode
                        ? "Preview mode never writes processor policy."
                        : "Suspended because coarse adaptive power-plan actuation is enabled.";
                Status = GraduatedCoreActuatorStatus.Disabled(why, now);
                return Status;
            }

            if (_faultLatched)
            {
                Status = Status with { State = GraduatedCoreActuatorState.Faulted, LiveWritesEnabled = false, At = now };
                return Status;
            }

            ProcessorPolicySnapshot current;
            try { current = _policy.CaptureActive(); }
            catch (Exception ex) { return Fault($"Processor-policy capture failed: {ex.Message}", now); }

            if (current.SchemeId != qualifiedSchemeId)
            {
                RestoreBaselineIfNeeded();
                Status = new GraduatedCoreActuatorStatus(
                    GraduatedCoreActuatorState.Suspended, false, null, current.CoreParkingMinCoresPercent, null,
                    input?.RequestedCapacityPercent, input?.DeliveredCapacityPercent,
                    "Suspended: graduated core-floor writes are qualified only on the Power Saver scheme.", now);
                return Status;
            }

            if (_baseline is null)
            {
                _baseline = current;
                Status = new GraduatedCoreActuatorStatus(
                    GraduatedCoreActuatorState.Armed, true, current.CoreParkingMinCoresPercent, current.CoreParkingMinCoresPercent,
                    current.CoreParkingMinCoresPercent, input?.RequestedCapacityPercent, input?.DeliveredCapacityPercent,
                    "Armed on Power Saver; baseline captured. Waiting for the next feedback evaluation before writing.", now);
                return Status;
            }

            if (input is null)
            {
                Status = new GraduatedCoreActuatorStatus(
                    GraduatedCoreActuatorState.Holding, true, _baseline.CoreParkingMinCoresPercent, current.CoreParkingMinCoresPercent,
                    current.CoreParkingMinCoresPercent, null, null, "Holding: rich capacity telemetry is not available.", now);
                return Status;
            }

            var plan = GraduatedProcessorPolicyPlanner.Plan(
                input.RequestedCapacityPercent,
                input.DeliveredCapacityPercent,
                input.AverageAwakePercentOfMaximumFrequency,
                current);
            var baselineFloor = _baseline.CoreParkingMinCoresPercent;
            var bounded = Math.Clamp(plan.NextCoreFloorPercent, baselineFloor, MaximumQualifiedCoreFloorPercent);

            if (bounded == current.CoreParkingMinCoresPercent)
            {
                Status = new GraduatedCoreActuatorStatus(
                    GraduatedCoreActuatorState.Holding, true, baselineFloor, current.CoreParkingMinCoresPercent, bounded,
                    input.RequestedCapacityPercent, input.DeliveredCapacityPercent, plan.Reason, now);
                return Status;
            }

            if (_lastWriteAt is DateTimeOffset last && now - last < MinimumWriteInterval)
            {
                Status = new GraduatedCoreActuatorStatus(
                    GraduatedCoreActuatorState.Holding, true, baselineFloor, current.CoreParkingMinCoresPercent, bounded,
                    input.RequestedCapacityPercent, input.DeliveredCapacityPercent,
                    $"Holding for feedback/cooldown before applying core floor {current.CoreParkingMinCoresPercent}->{bounded}.", now);
                return Status;
            }

            var result = _policy.Apply(new ProcessorPolicyPatch(CoreParkingMinCoresPercent: bounded));
            if (!result.Success)
                return Fault($"Core-floor write failed: {result.Error ?? "unknown error"}", now);
            if (result.After.SchemeId != qualifiedSchemeId
                || result.After.CoreParkingMinCoresPercent != bounded
                || result.After.EnergyPerformancePreferencePercent != current.EnergyPerformancePreferencePercent)
                return Fault("Core-floor write readback violated the qualified actuator boundary.", now);

            _lastWriteAt = now;
            Status = new GraduatedCoreActuatorStatus(
                GraduatedCoreActuatorState.Applied, true, baselineFloor, result.After.CoreParkingMinCoresPercent, bounded,
                input.RequestedCapacityPercent, input.DeliveredCapacityPercent,
                $"Applied qualified core-floor step {current.CoreParkingMinCoresPercent}->{bounded}. EPP held at {current.EnergyPerformancePreferencePercent}.", now);
            return Status;
        }
    }

    public GraduatedCoreActuatorStatus StopAndRestore(string reason = "Graduated core-floor actuator stopped and baseline restored.")
    {
        lock (_gate)
        {
            var now = _clock();
            var restored = RestoreBaselineIfNeeded();
            _faultLatched = false;
            Status = GraduatedCoreActuatorStatus.Disabled(restored ? reason : "Graduated core-floor actuator stopped; no baseline mutation was active.", now);
            return Status;
        }
    }

    private GraduatedCoreActuatorStatus Fault(string message, DateTimeOffset now)
    {
        var restored = RestoreBaselineIfNeeded();
        _faultLatched = true;
        Status = new GraduatedCoreActuatorStatus(
            GraduatedCoreActuatorState.Faulted, false, null, null, null, null, null,
            restored ? $"{message} Baseline restored; actuator fault-latched." : $"{message} Actuator fault-latched.", now);
        return Status;
    }

    private bool RestoreBaselineIfNeeded()
    {
        if (_baseline is null) return false;
        var snapshot = _baseline;
        _baseline = null;
        _lastWriteAt = null;
        var result = _policy.Restore(snapshot);
        return result.Success;
    }

    public static GraduatedCoreFloorControlInput? BuildInput(IReadOnlyList<ContinuitySample>? history)
    {
        if (history is null || history.Count == 0) return null;
        var latestAt = history.Max(sample => sample.At);
        var earliestAt = history.Min(sample => sample.At);
        var capacity = GraduatedCapacityEntitlementModel.Build(history, earliestAt, latestAt).Samples.LastOrDefault();
        if (capacity is null) return null;
        var rich = history.LastOrDefault(sample => sample.At <= latestAt && sample.LogicalProcessors is { Count: > 0 });
        double? averagePerformance = null;
        if (rich?.LogicalProcessors is { Count: > 0 } logical)
        {
            var values = logical
                .Where(thread => !thread.IsParked && thread.PercentOfMaximumFrequency is double value && double.IsFinite(value) && value > 0)
                .Select(thread => thread.PercentOfMaximumFrequency!.Value)
                .ToArray();
            if (values.Length > 0) averagePerformance = values.Average();
        }
        return new GraduatedCoreFloorControlInput(capacity.RequestedCapacityPercent, capacity.DeliveredCapacityPercent, averagePerformance);
    }
}