namespace PowerFlow.App.Dashboard;

public enum DisclosureKind
{
    None,
    Now,
    Trajectory,
    QuietRail,
    PromoteRail,
    Transition,
    Mode
}

public sealed record DisclosureState(DisclosureKind Kind, string? Key = null)
{
    public static DisclosureState None { get; } = new(DisclosureKind.None);

    public DisclosureState Toggle(DisclosureKind kind, string? key = null) =>
        Kind == kind && string.Equals(Key, key, StringComparison.Ordinal)
            ? None
            : new DisclosureState(kind, key);
}
