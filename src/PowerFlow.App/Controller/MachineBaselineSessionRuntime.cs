using PowerFlow.Core.Profiling;

namespace PowerFlow.App.Controller;

public sealed record MachineBaselineSessionSnapshot(
    bool IsRunning,
    MachineBaselineProgress? Progress,
    MachineBaselineComparisonRun? LastCompletedRun,
    string Status,
    string? Error,
    DateTimeOffset? StartedAt = null,
    TimeSpan? TargetDuration = null)
{
    public static MachineBaselineSessionSnapshot Idle { get; } = new(false, null, null, "Ready", null, null, null);
}

public sealed class MachineBaselineSessionRuntime
{
    private readonly object _gate = new();
    private readonly IMachineBaselineRunner _runner;
    private readonly Func<MachineBaselineComparisonRun, Task> _saveCompletedRun;
    private CancellationTokenSource? _cts;
    private Task? _activeTask;
    private MachineBaselineSessionSnapshot _snapshot = MachineBaselineSessionSnapshot.Idle;

    public MachineBaselineSessionRuntime(IMachineBaselineRunner runner, Func<MachineBaselineComparisonRun, Task> saveCompletedRun)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _saveCompletedRun = saveCompletedRun ?? throw new ArgumentNullException(nameof(saveCompletedRun));
    }

    public event EventHandler<MachineBaselineSessionSnapshot>? Changed;

    public MachineBaselineSessionSnapshot Snapshot
    {
        get { lock (_gate) return _snapshot; }
    }

    public Task StartAsync(MachineBaselineSchedule? schedule = null)
    {
        lock (_gate)
        {
            if (_activeTask is { IsCompleted: false }) return _activeTask;
            _cts?.Dispose();
            _cts = new CancellationTokenSource();
            _snapshot = _snapshot with { IsRunning = true, Progress = null, Status = "Starting", Error = null, StartedAt = DateTimeOffset.UtcNow, TargetDuration = (schedule ?? MachineBaselineSchedule.Standard).TotalDuration };
            PublishLocked();
            _activeTask = RunCoreAsync(schedule ?? MachineBaselineSchedule.Standard, _cts.Token);
            return _activeTask;
        }
    }

    public void Cancel()
    {
        lock (_gate) _cts?.Cancel();
    }

    public async Task CancelAndWaitAsync()
    {
        Task? active;
        lock (_gate)
        {
            _cts?.Cancel();
            active = _activeTask;
        }
        if (active is not null) await active;
    }

    private async Task RunCoreAsync(MachineBaselineSchedule schedule, CancellationToken token)
    {
        var progress = new Progress<MachineBaselineProgress>(value =>
        {
            lock (_gate)
            {
                _snapshot = _snapshot with { IsRunning = true, Progress = value, Status = value.Message, Error = null };
                PublishLocked();
            }
        });
        try
        {
            var run = await _runner.RunAsync(schedule, progress, token);
            await _saveCompletedRun(run);
            lock (_gate)
            {
                _snapshot = new(false, null, run, "Complete", null, _snapshot.StartedAt, _snapshot.TargetDuration);
                PublishLocked();
            }
        }
        catch (OperationCanceledException)
        {
            lock (_gate)
            {
                _snapshot = _snapshot with { IsRunning = false, Progress = null, Status = "Cancelled", Error = null };
                PublishLocked();
            }
        }
        catch (Exception ex)
        {
            lock (_gate)
            {
                _snapshot = _snapshot with { IsRunning = false, Progress = null, Status = "Failed", Error = ex.Message };
                PublishLocked();
            }
        }
    }

    private void PublishLocked() => Changed?.Invoke(this, _snapshot);
}
