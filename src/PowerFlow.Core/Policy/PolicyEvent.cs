namespace PowerFlow.Core.Policy;

public abstract record PolicyEvent(DateTimeOffset At);
public sealed record CpuSample(DateTimeOffset At, double CpuPercent) : PolicyEvent(At);
public sealed record GameStarted(DateTimeOffset At, string ProcessKey) : PolicyEvent(At);
public sealed record GameExited(DateTimeOffset At, string ProcessKey, bool AllTrackedGameProcessesExited) : PolicyEvent(At);
public sealed record ManualPerformanceRequested(DateTimeOffset At) : PolicyEvent(At);
public sealed record ManualPerformanceReleased(DateTimeOffset At) : PolicyEvent(At);
public sealed record ExplicitBalancedActivated(DateTimeOffset At, string RuleName) : PolicyEvent(At);
public sealed record ExplicitBalancedCleared(DateTimeOffset At, string RuleName) : PolicyEvent(At);
public sealed record CooldownExpired(DateTimeOffset At) : PolicyEvent(At);
