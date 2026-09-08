using PowerFlow.App.Controller;
using PowerFlow.Windows.Activity;

namespace PowerFlow.App.Dashboard;

public sealed class DashboardTelemetrySession : IAsyncDisposable
{
    private readonly Func<IDashboardTelemetrySource> _sourceFactory;
    private readonly IControllerTickSourceFactory _tickFactory;
    private readonly IControllerClock _clock;
    private CancellationTokenSource? _cts;
    private IDashboardTelemetrySource? _source;
    private Task? _runner;
    private TaskCompletionSource _sampleSignal = NewSignal();

    public DashboardTelemetrySession(Func<IDashboardTelemetrySource> sourceFactory, IControllerTickSourceFactory tickFactory, IControllerClock clock)
    {
        _sourceFactory = sourceFactory;
        _tickFactory = tickFactory;
        _clock = clock;
    }

    public bool IsRunning => _runner is { IsCompleted: false };
    public DashboardTelemetry? Latest { get; private set; }
    public event EventHandler<DashboardTelemetry>? TelemetryChanged;

    public Task StartAsync()
    {
        if (IsRunning) return Task.CompletedTask;
        _source = _sourceFactory();
        _cts = new CancellationTokenSource();
        _sampleSignal = NewSignal();
        var tick = _tickFactory.Create(TimeSpan.FromSeconds(1));
        _runner = RunAsync(tick, _cts.Token);
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        var cts = Interlocked.Exchange(ref _cts, null);
        if (cts is null) return;
        cts.Cancel();
        var runner = Interlocked.Exchange(ref _runner, null);
        if (runner is not null)
        {
            try { await runner.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }
        cts.Dispose();
        Interlocked.Exchange(ref _source, null)?.Dispose();
    }

    public Task FlushAsync() => Latest is not null ? Task.CompletedTask : _sampleSignal.Task;

    private async Task RunAsync(IControllerTickSource tick, CancellationToken token)
    {
        await using (tick.ConfigureAwait(false))
        {
            while (await tick.WaitForNextTickAsync(token).ConfigureAwait(false))
            {
                token.ThrowIfCancellationRequested();
                var reading = _source?.Read(_clock.UtcNow);
                if (reading is null) continue;
                Latest = reading;
                _sampleSignal.TrySetResult();
                TelemetryChanged?.Invoke(this, reading);
            }
        }
    }

    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
