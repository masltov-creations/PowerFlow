namespace PowerFlow.Windows.Power;

public static class PowerPlanIds
{
    public static readonly Guid PowerSaver = Guid.Parse("a1841308-3541-4fab-bc81-f71556f20b4a");
    public static readonly Guid Balanced = Guid.Parse("381b4222-f694-41f0-9685-ff5bb260df2e");
    public static readonly Guid HighPerformance = Guid.Parse("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");
}

public sealed record PowerPlanInfo(Guid Id, string Name);
public sealed record PowerPlanSwitchResult(bool Success, Guid RequestedId, Guid? ActiveId, string? Error);
