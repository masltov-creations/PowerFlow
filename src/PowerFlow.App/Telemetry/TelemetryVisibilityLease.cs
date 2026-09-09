namespace PowerFlow.App.Telemetry;

public sealed class TelemetryVisibilityLease : IDisposable
{
    private Action? _release;

    internal TelemetryVisibilityLease(Action release) => _release = release;

    public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
}
