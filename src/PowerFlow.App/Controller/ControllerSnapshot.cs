using PowerFlow.Core.Policy;

namespace PowerFlow.App.Controller;

public sealed record TransitionRecord(
    DateTimeOffset At,
    PowerState From,
    PowerState To,
    string Reason,
    bool Success);

public sealed record ControllerSnapshot(
    PowerState State,
    string Reason,
    bool IsLatched,
    string? LatchType,
    double CpuPercent,
    double ThresholdProgress,
    TimeSpan? CooldownRemaining,
    string? TriggerApplication,
    DateTimeOffset At,
    IReadOnlyList<TransitionRecord> History,
    long ActivitySampleCount,
    long DashboardTelemetrySampleCount);

public interface IControllerClock { DateTimeOffset UtcNow { get; } }
public interface IControllerDelay { Task DelayAsync(TimeSpan delay, CancellationToken token); }
public interface IControllerTickSourceFactory { IControllerTickSource Create(TimeSpan period); }
public interface IControllerTickSource : IAsyncDisposable { ValueTask<bool> WaitForNextTickAsync(CancellationToken token); }

public sealed class SystemControllerClock : IControllerClock { public DateTimeOffset UtcNow => DateTimeOffset.UtcNow; }
public sealed class SystemControllerDelay : IControllerDelay { public Task DelayAsync(TimeSpan delay, CancellationToken token) => Task.Delay(delay, token); }
public sealed class PeriodicControllerTickSourceFactory : IControllerTickSourceFactory { public IControllerTickSource Create(TimeSpan period) => new PeriodicControllerTickSource(period); }

internal sealed class PeriodicControllerTickSource(TimeSpan period) : IControllerTickSource
{
    private readonly PeriodicTimer _timer = new(period);
    public ValueTask<bool> WaitForNextTickAsync(CancellationToken token) => _timer.WaitForNextTickAsync(token);
    public ValueTask DisposeAsync() { _timer.Dispose(); return ValueTask.CompletedTask; }
}
