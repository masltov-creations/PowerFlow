namespace PowerFlow.Windows.Games;

public sealed record GameProcess(
    int ProcessId,
    string ExecutablePath,
    int? ParentProcessId,
    DateTimeOffset StartTime,
    string RuleSource);

public sealed record ProcessStartEvent(
    int ProcessId,
    int? ParentProcessId,
    string ExecutablePath,
    DateTimeOffset StartTime);

public sealed class GameDetectedEventArgs(GameProcess process, string reason) : EventArgs
{
    public GameProcess Process { get; } = process;
    public string Reason { get; } = reason;
}
