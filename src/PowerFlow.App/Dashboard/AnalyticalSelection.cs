using PowerFlow.Core.Envelope;

namespace PowerFlow.App.Dashboard;

public sealed record AnalyticalSelection(
    IReadOnlyList<int> ObservationIndices,
    DateTimeOffset? Start,
    DateTimeOffset? End,
    string? Actor)
{
    public static AnalyticalSelection Empty { get; } = new(Array.Empty<int>(), null, null, null);

    public static AnalyticalSelection FromObservationIndices(
        IReadOnlyList<OperatingObservation> history,
        IEnumerable<int>? observationIndices)
    {
        ArgumentNullException.ThrowIfNull(history);
        if (observationIndices is null) return Empty;

        var indices = observationIndices
            .Where(index => index >= 0 && index < history.Count)
            .Distinct()
            .OrderBy(index => index)
            .ToArray();
        if (indices.Length == 0) return Empty;

        var observations = indices.Select(index => history[index]).ToArray();
        var actors = observations
            .Select(observation => observation.Actor)
            .Where(actor => !string.IsNullOrWhiteSpace(actor))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new AnalyticalSelection(
            indices,
            observations.Min(observation => observation.At),
            observations.Max(observation => observation.At),
            actors.Length == 1 ? actors[0] : null);
    }
}