using System.Diagnostics;
using PowerFlow.Core.Policy;
using PowerFlow.Core.Profiling;
using PowerFlow.Windows.Activity;
using PowerFlow.Windows.Power;

namespace PowerFlow.App.Controller;

public sealed record MachineBaselineModeRoute(
    MachineBaselineMode Mode,
    string Label,
    PowerState? NativeState,
    PowerFlowOperatingMode? PowerFlowMode,
    bool IsAuto = false);

public static class MachineBaselineModeRoutes
{
    public static MachineBaselineModeRoute For(MachineBaselineMode mode) => mode switch
    {
        MachineBaselineMode.WindowsSaver => new(mode, "WINDOWS SAVER", PowerState.PowerSaver, null),
        MachineBaselineMode.WindowsBalanced => new(mode, "WINDOWS BALANCED", PowerState.Balanced, null),
        MachineBaselineMode.BalancedEfficient => new(mode, "BAL-E", null, PowerFlowOperatingMode.Balanced),
        MachineBaselineMode.BalancedPerformance => new(mode, "BAL-P", null, PowerFlowOperatingMode.BalancedPerformance),
        MachineBaselineMode.Performance => new(mode, "PERFORMANCE", null, PowerFlowOperatingMode.Performance),
        MachineBaselineMode.Auto => new(mode, "AUTO", null, null, true),
        _ => throw new ArgumentOutOfRangeException(nameof(mode))
    };
}

public sealed record PowerFlowMachineBaselineRestoreState(
    ControllerSnapshot Controller,
    PowerFlowOperatingMode? ActiveProfile,
    ProcessorPolicySnapshot ProcessorPolicy);

public sealed class PowerFlowMachineBaselineHost : IMachineBaselineHost
{
    private readonly PowerFlowController _controller;
    private readonly PowerModeProfileRuntime _profileRuntime;
    private readonly IProcessorPolicyController _processorPolicy;
    private readonly Func<DashboardTelemetrySource> _telemetryFactory;
    private readonly Func<TimeSpan> _sampleInterval;
    private readonly bool _liveWritesEnabled;

    public PowerFlowMachineBaselineHost(
        PowerFlowController controller,
        PowerModeProfileRuntime profileRuntime,
        bool liveWritesEnabled,
        Func<TimeSpan>? sampleInterval = null,
        IProcessorPolicyController? processorPolicy = null,
        Func<DashboardTelemetrySource>? telemetryFactory = null)
    {
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _profileRuntime = profileRuntime ?? throw new ArgumentNullException(nameof(profileRuntime));
        _liveWritesEnabled = liveWritesEnabled;
        _sampleInterval = sampleInterval ?? (() => TimeSpan.FromSeconds(1));
        _processorPolicy = processorPolicy ?? new WindowsProcessorPolicyController();
        _telemetryFactory = telemetryFactory ?? (() => new DashboardTelemetrySource());
    }

    public string Label(MachineBaselineMode mode) => MachineBaselineModeRoutes.For(mode).Label;

    public Task<MachineBaselineRestorePoint> CaptureRestorePointAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var state = new PowerFlowMachineBaselineRestoreState(
            _controller.Snapshot,
            _profileRuntime.CurrentProfile?.Mode,
            _processorPolicy.CaptureActive());
        return Task.FromResult(new MachineBaselineRestorePoint("PowerFlow pre-baseline state", state));
    }

    public async Task EnterModeAsync(MachineBaselineMode mode, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var route = MachineBaselineModeRoutes.For(mode);
        _profileRuntime.Restore("Baseline transition restored previous profile overlay.");

        if (route.IsAuto)
        {
            await _controller.ReleaseManualLatchAsync();
            return;
        }

        if (route.NativeState is PowerState nativeState)
        {
            await _controller.SetManualStateAsync(nativeState);
            return;
        }

        if (route.PowerFlowMode is not PowerFlowOperatingMode operatingMode)
            throw new InvalidOperationException($"No baseline route exists for {mode}.");

        var profile = PowerFlowOperatingProfiles.For(operatingMode);
        await _controller.SetManualStateAsync(profile.WindowsState);
        var status = _profileRuntime.Apply(profile, _liveWritesEnabled);
        if (_liveWritesEnabled && !status.Applied)
            throw new InvalidOperationException(status.Message);
    }

    public async Task<MachineIdleSummary> CaptureIdleAsync(TimeSpan duration, CancellationToken cancellationToken)
    {
        if (duration <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(duration));
        using var telemetry = _telemetryFactory();
        try { _ = telemetry.Read(DateTimeOffset.UtcNow); } catch { }

        var watts = new List<double>();
        var performance = new List<double>();
        var speed = new List<double>();
        var pressure = new List<double>();
        var queue = new List<double>();
        var sw = Stopwatch.StartNew();
        var interval = _sampleInterval();
        if (interval < TimeSpan.FromMilliseconds(100)) interval = TimeSpan.FromMilliseconds(100);
        try
        {
            while (sw.Elapsed < duration)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var sample = telemetry.Read(DateTimeOffset.UtcNow);
                    if (sample.PackageWatts is double w && double.IsFinite(w) && w >= 0) watts.Add(w);
                    if (sample.ProcessorPerformancePercent is double p && double.IsFinite(p) && p >= 0) performance.Add(p);
                    if (sample.AverageMhz is double mhz && double.IsFinite(mhz) && mhz > 0) speed.Add(mhz / 1000d);
                    if (sample.ProcessorQueueLength is double q && double.IsFinite(q) && q >= 0) queue.Add(q);
                }
                catch { }
                var remaining = duration - sw.Elapsed;
                if (remaining > TimeSpan.Zero) await Task.Delay(remaining < interval ? remaining : interval, cancellationToken);
            }
        }
        finally { sw.Stop(); }

        var observed = sw.Elapsed > duration ? duration : sw.Elapsed;
        var averageWatts = Average(watts);
        return new MachineIdleSummary(
            observed,
            averageWatts,
            Percentile95(watts),
            Average(performance),
            Average(speed),
            null,
            Average(queue),
            averageWatts is double value ? value * observed.TotalHours : null,
            watts.Count);
    }

    public async Task RestoreAsync(MachineBaselineRestorePoint restorePoint, CancellationToken cancellationToken)
    {
        if (restorePoint.Payload is not PowerFlowMachineBaselineRestoreState saved)
            throw new ArgumentException("Restore point is not a PowerFlow machine baseline snapshot.", nameof(restorePoint));

        _profileRuntime.Restore("Baseline completion restored the temporary profile overlay.");
        if (saved.ActiveProfile is PowerFlowOperatingMode profileMode)
        {
            var profile = PowerFlowOperatingProfiles.For(profileMode);
            await _controller.SetManualStateAsync(profile.WindowsState);
            var status = _profileRuntime.Apply(profile, _liveWritesEnabled);
            if (_liveWritesEnabled && !status.Applied) throw new InvalidOperationException(status.Message);
            return;
        }

        await _controller.SetManualStateAsync(saved.Controller.State);
        var restored = _processorPolicy.Restore(saved.ProcessorPolicy);
        if (!restored.Success)
            throw new InvalidOperationException($"Could not restore the pre-baseline processor policy: {restored.Error}");
        if (!saved.Controller.IsLatched)
            await _controller.ReleaseManualLatchAsync();
    }

    private static double? Average(IReadOnlyList<double> values) => values.Count == 0 ? null : values.Average();

    private static double? Percentile95(IReadOnlyList<double> values)
    {
        if (values.Count == 0) return null;
        var ordered = values.OrderBy(value => value).ToArray();
        var index = Math.Clamp((int)Math.Ceiling(ordered.Length * .95d) - 1, 0, ordered.Length - 1);
        return ordered[index];
    }
}