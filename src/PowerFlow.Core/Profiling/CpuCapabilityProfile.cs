namespace PowerFlow.Core.Profiling;

public sealed record CpuCapabilityPoint(
    int WorkerCount,
    double ThroughputMops,
    double? PackageWatts,
    double? ProcessorPerformancePercent,
    double? SpeedGhz)
{
    public double? ThroughputPerWatt => PackageWatts is > 0 ? ThroughputMops / PackageWatts.Value : null;
}

public sealed record CpuCapabilityProfile(
    DateTimeOffset CapturedAt,
    string PolicyLabel,
    string PolicySignature,
    IReadOnlyList<CpuCapabilityPoint> Points,
    int BestEfficiencyWorkers,
    int KneeWorkers,
    int MaxThroughputWorkers)
{
    public string Summary => $"Efficiency {BestEfficiencyWorkers}T - knee {KneeWorkers}T - max throughput {MaxThroughputWorkers}T";
}

public static class CpuCapabilityAnalysis
{
    public static CpuCapabilityProfile Build(DateTimeOffset capturedAt, string policyLabel, string policySignature, IReadOnlyList<CpuCapabilityPoint> points)
    {
        if (points.Count == 0) throw new ArgumentException("At least one capability point is required.", nameof(points));
        var ordered = points.OrderBy(point => point.WorkerCount).ToArray();
        var max = ordered.MaxBy(point => point.ThroughputMops)!;
        var efficient = ordered.Where(point => point.ThroughputPerWatt is not null)
            .MaxBy(point => point.ThroughputPerWatt) ?? max;
        var threshold = max.ThroughputMops * .90d;
        var knee = ordered.FirstOrDefault(point => point.ThroughputMops >= threshold) ?? max;
        return new CpuCapabilityProfile(capturedAt, policyLabel, policySignature, ordered,
            efficient.WorkerCount, knee.WorkerCount, max.WorkerCount);
    }
}