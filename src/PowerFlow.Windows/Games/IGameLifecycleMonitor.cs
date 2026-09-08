using PowerFlow.Core.Rules;

namespace PowerFlow.Windows.Games;

public interface IGameLifecycleMonitor : IDisposable
{
    event EventHandler<GameDetectedEventArgs>? GameDetected;
    event EventHandler<GameDetectedEventArgs>? GameProcessAdded;
    event EventHandler? GameLatchReleased;
    bool IsLatched { get; }
    int TrackedCount { get; }
    string? LatchReason { get; }
    void UpdateRules(IReadOnlyList<AppRule> rules);
    void Start();
    void Stop();
    void AddRelatedProcess(GameProcess process);
}

public interface IProcessEventSource : IDisposable
{
    event EventHandler<ProcessStartEvent>? ProcessStarted;
    bool IsRunning { get; }
    IReadOnlyList<ProcessStartEvent> SnapshotExisting();
    void Start();
    void Stop();
}

public interface IProcessHandleFactory
{
    ITrackedProcessHandle Open(GameProcess process);
}

public interface ITrackedProcessHandle : IDisposable
{
    GameProcess Process { get; }
    event EventHandler? Exited;
    void EnableExitEvents();
}


