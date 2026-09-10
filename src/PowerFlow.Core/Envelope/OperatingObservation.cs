namespace PowerFlow.Core.Envelope;

public sealed record OperatingObservation
{
    public OperatingObservation(
        DateTimeOffset at,
        double cpuPressurePercent,
        double? packageWatts,
        double? effectiveClockMhz,
        int? activeCores,
        int? totalCores,
        EnvelopeZone zone,
        string? actor,
        EnvelopeDecisionKind decision)
    {
        At = at;
        CpuPressurePercent = double.IsFinite(cpuPressurePercent) ? Math.Clamp(cpuPressurePercent, 0, 100) : 0;
        PackageWatts = packageWatts is > 0 and double watts && double.IsFinite(watts) ? watts : null;
        EffectiveClockMhz = effectiveClockMhz is > 0 and double mhz && double.IsFinite(mhz) ? mhz : null;
        TotalCores = totalCores is > 0 ? totalCores : null;
        ActiveCores = activeCores is > 0
            ? TotalCores is int total ? Math.Min(activeCores.Value, total) : activeCores
            : null;
        Zone = zone;
        Actor = string.IsNullOrWhiteSpace(actor) ? null : actor;
        Decision = decision;
    }

    public DateTimeOffset At { get; }
    public double CpuPressurePercent { get; }
    public double? PackageWatts { get; }
    public double? EffectiveClockMhz { get; }
    public int? ActiveCores { get; }
    public int? TotalCores { get; }
    public EnvelopeZone Zone { get; }
    public string? Actor { get; }
    public EnvelopeDecisionKind Decision { get; }
}
