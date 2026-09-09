using PowerFlow.App.Controller;
using PowerFlow.Windows.Activity;

namespace PowerFlow.App.Telemetry;

public sealed class TelemetryContinuityRecorder : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly Func<IDashboardTelemetrySource> _sourceFactory;
    private readonly IControllerTickSourceFactory _tickFactory;
    private readonly IControllerClock _clock;
    private readonly int _capacity;
    private readonly List<ContinuitySample> _history = [];
    private IDashboardTelemetrySource? _source;
    private ControllerSnapshot? _snapshot;
    private CancellationTokenSource? _runnerCts;
    private Task? _runner;
    private int _visibleLeases;
    private bool _started;
    private bool _disposed;
    private long _richSampleCount;
    private TaskCompletionSource _richSampleSignal = NewSignal();

    public TelemetryContinuityRecorder(
        Func<IDashboardTelemetrySource> sourceFactory,
        IControllerTickSourceFactory tickFactory,
        IControllerClock clock,
        int capacity = 1800)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        _sourceFactory = sourceFactory;
        _tickFactory = tickFactory;
        _clock = clock;
        _capacity = capacity;
    }

    public event EventHandler? ContinuityChanged;

    public TelemetryCadenceMode Mode { get; private set; } = TelemetryCadenceMode.Off;
    public bool IsRichTelemetryDegraded { get; private set; }
    public long RichSampleCount => Interlocked.Read(ref _richSampleCount);

    public IReadOnlyList<ContinuitySample> History
    {
        get { lock (_gate) return _history.ToArray(); }
    }

    public DashboardTelemetry? LatestRichTelemetry { get; private set; }

    public Task StartAsync()
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            if (_started) return Task.CompletedTask;
            _started = true;
            ReconfigureLocked();
        }
        return Task.CompletedTask;
    }

    public void UpdateControllerSnapshot(ControllerSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var changed = false;
        lock (_gate)
        {
            ThrowIfDisposed();
            _snapshot = snapshot;
            AppendLocked(new ContinuitySample(
                snapshot.At,
                snapshot.CpuPercent,
                null,
                null,
                snapshot.State,
                snapshot.Reason,
                snapshot.IsLatched,
                snapshot.LatchType,
                snapshot.ThresholdProgress,
                snapshot.TriggerApplication));
            changed = true;
            if (_started) ReconfigureLocked();
        }
        if (changed) ContinuityChanged?.Invoke(this, EventArgs.Empty);
    }

    public TelemetryVisibilityLease AcquireVisibility()
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            _visibleLeases++;
            if (_started) ReconfigureLocked();
        }
        return new TelemetryVisibilityLease(ReleaseVisibility);
    }

    public Task WaitForRichSampleAsync()
    {
        lock (_gate) return _richSampleCount > 0 ? Task.CompletedTask : _richSampleSignal.Task;
    }

    private void ReleaseVisibility()
    {
        lock (_gate)
        {
            if (_disposed || _visibleLeases == 0) return;
            _visibleLeases--;
            if (_started) ReconfigureLocked();
        }
    }

    private void ReconfigureLocked()
    {
        var snapshot = _snapshot;
        var desired = TelemetryCadencePolicy.SelectMode(
            _visibleLeases > 0,
            snapshot?.IsLatched == true,
            snapshot?.LatchType);
        if (desired == Mode && (desired == TelemetryCadenceMode.Off || _runner is { IsCompleted: false })) return;

        _runnerCts?.Cancel();
        _runnerCts = null;
        _runner = null;
        Mode = desired;

        var interval = TelemetryCadencePolicy.IntervalFor(desired);
        if (interval is null) return;

        _source ??= _sourceFactory();
        var cts = new CancellationTokenSource();
        _runnerCts = cts;
        var tick = _tickFactory.Create(interval.Value);
        _runner = RunAsync(tick, cts);
    }

    private async Task RunAsync(IControllerTickSource tick, CancellationTokenSource cts)
    {
        try
        {
            await using (tick.ConfigureAwait(false))
            {
                while (await tick.WaitForNextTickAsync(cts.Token).ConfigureAwait(false))
                {
                    cts.Token.ThrowIfCancellationRequested();
                    DashboardTelemetry? reading = null;
                    try
                    {
                        reading = _source?.Read(_clock.UtcNow);
                        IsRichTelemetryDegraded = false;
                    }
                    catch
                    {
                        IsRichTelemetryDegraded = true;
                    }
                    if (reading is not null) RecordRich(reading);
                }
            }
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested) { }
        finally
        {
            cts.Dispose();
        }
    }

    private void RecordRich(DashboardTelemetry telemetry)
    {
        var changed = false;
        lock (_gate)
        {
            if (_disposed) return;
            LatestRichTelemetry = telemetry;
            var snapshot = _snapshot;
            if (snapshot is not null)
            {
                AppendLocked(new ContinuitySample(
                    telemetry.At,
                    snapshot.CpuPercent,
                    telemetry.PackageWatts,
                    telemetry.AverageMhz,
                    snapshot.State,
                    snapshot.Reason,
                    snapshot.IsLatched,
                    snapshot.LatchType,
                    snapshot.ThresholdProgress,
                    snapshot.TriggerApplication));
                changed = true;
            }
            Interlocked.Increment(ref _richSampleCount);
            _richSampleSignal.TrySetResult();
        }
        if (changed) ContinuityChanged?.Invoke(this, EventArgs.Empty);
    }

    private void AppendLocked(ContinuitySample sample)
    {
        _history.Add(sample);
        while (_history.Count > _capacity) _history.RemoveAt(0);
    }

    public async ValueTask DisposeAsync()
    {
        Task? runner;
        IDashboardTelemetrySource? source;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _started = false;
            _runnerCts?.Cancel();
            runner = _runner;
            _runner = null;
            _runnerCts = null;
            source = _source;
            _source = null;
            Mode = TelemetryCadenceMode.Off;
        }
        if (runner is not null)
        {
            try { await runner.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }
        source?.Dispose();
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(TelemetryContinuityRecorder));
    }

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
