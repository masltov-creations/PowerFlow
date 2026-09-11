using PowerFlow.Core.Profiling;

namespace PowerFlow.App.Controller;

public sealed record MachineBaselineRestorePoint(string Description, object? Payload = null);

public interface IMachineBaselineHost
{
    Task<MachineBaselineRestorePoint> CaptureRestorePointAsync(CancellationToken cancellationToken);
    Task EnterModeAsync(MachineBaselineMode mode, CancellationToken cancellationToken);
    Task<MachineIdleSummary> CaptureIdleAsync(TimeSpan duration, CancellationToken cancellationToken);
    Task RestoreAsync(MachineBaselineRestorePoint restorePoint, CancellationToken cancellationToken);
    string Label(MachineBaselineMode mode);
}

public enum MachineBaselineStage
{
    Entering,
    Settling,
    Idle,
    Benchmark,
    Restoring,
    Complete
}

public sealed record MachineBaselineProgress(
    int ModeIndex,
    int ModeCount,
    MachineBaselineMode Mode,
    MachineBaselineStage Stage,
    string Message);

public interface IMachineBaselineRunner
{
    Task<MachineBaselineComparisonRun> RunAsync(
        MachineBaselineSchedule? schedule = null,
        IProgress<MachineBaselineProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
public sealed class MachineBaselineRunner : IMachineBaselineRunner
{
    private readonly IMachineBaselineHost _host;
    private readonly ICpuCapabilityProfiler _profiler;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    public MachineBaselineRunner(
        IMachineBaselineHost host,
        ICpuCapabilityProfiler profiler,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _profiler = profiler ?? throw new ArgumentNullException(nameof(profiler));
        _delay = delay ?? Task.Delay;
    }

    public async Task<MachineBaselineComparisonRun> RunAsync(
        MachineBaselineSchedule? schedule = null,
        IProgress<MachineBaselineProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        schedule ??= MachineBaselineSchedule.Standard;
        var restorePoint = await _host.CaptureRestorePointAsync(cancellationToken);
        var results = new List<MachineBaselineModeResult>(schedule.Modes.Count);
        try
        {
            for (var index = 0; index < schedule.Modes.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var mode = schedule.Modes[index];
                var label = _host.Label(mode);
                progress?.Report(new(index + 1, schedule.Modes.Count, mode, MachineBaselineStage.Entering, $"Applying {label}"));
                await _host.EnterModeAsync(mode, cancellationToken);

                progress?.Report(new(index + 1, schedule.Modes.Count, mode, MachineBaselineStage.Settling, $"Settling {label}"));
                await _delay(schedule.SettleDuration, cancellationToken);

                progress?.Report(new(index + 1, schedule.Modes.Count, mode, MachineBaselineStage.Idle, $"Measuring idle power for {label}"));
                var idle = await _host.CaptureIdleAsync(schedule.IdleDuration, cancellationToken);

                progress?.Report(new(index + 1, schedule.Modes.Count, mode, MachineBaselineStage.Benchmark, $"Profiling 1/2/4/8/16-thread throughput for {label}"));
                var profile = await _profiler.RunAsync(CpuCapabilityProfilerOptions.ForBaseline(schedule), null, cancellationToken);
                results.Add(new MachineBaselineModeResult(mode, label, profile.PolicySignature, idle, profile));
            }

            var recommendation = MachineBaselineAnalysis.Recommend(results);
            var run = new MachineBaselineComparisonRun(Guid.NewGuid(), DateTimeOffset.UtcNow, schedule, results.ToArray(), recommendation);
            progress?.Report(new(schedule.Modes.Count, schedule.Modes.Count, schedule.Modes[^1], MachineBaselineStage.Complete, "Machine baseline complete"));
            return run;
        }
        finally
        {
            var mode = schedule.Modes.Count > 0 ? schedule.Modes[Math.Min(results.Count, schedule.Modes.Count - 1)] : MachineBaselineMode.Auto;
            progress?.Report(new(Math.Min(results.Count + 1, schedule.Modes.Count), schedule.Modes.Count, mode, MachineBaselineStage.Restoring, "Restoring pre-run PowerFlow state"));
            await _host.RestoreAsync(restorePoint, CancellationToken.None);
        }
    }
}