namespace PowerFlow.Windows.Apps;

public sealed record RunningAppCandidate(int ProcessId, string DisplayName, string? ExecutablePath);
public sealed record RunningAppOption(int ProcessId, string DisplayName, string ExecutablePath);

public static class RunningAppProjection
{
    public static IReadOnlyList<RunningAppOption> Project(IEnumerable<RunningAppCandidate> candidates)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<RunningAppOption>();
        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate.DisplayName) || string.IsNullOrWhiteSpace(candidate.ExecutablePath)) continue;
            var path = candidate.ExecutablePath.Trim();
            if (!seen.Add(path)) continue;
            result.Add(new RunningAppOption(candidate.ProcessId, candidate.DisplayName.Trim(), path));
        }
        return result.OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase).ToArray();
    }
}
