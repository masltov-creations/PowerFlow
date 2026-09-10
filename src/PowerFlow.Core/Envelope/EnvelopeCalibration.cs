namespace PowerFlow.Core.Envelope;

public static class EnvelopeCalibration
{
    private static readonly OperatingEnvelope ConservativeDefault = new(25, 55, 80, null, null, null);

    public static EnvelopeCalibrationResult Calibrate(IReadOnlyList<OperatingObservation> observations)
    {
        ArgumentNullException.ThrowIfNull(observations);
        var usable = observations
            .Where(x => x.PackageWatts is > 0 && x.EffectiveClockMhz is > 0)
            .OrderBy(x => x.PackageWatts)
            .ToArray();

        if (observations.Count < 20 || usable.Length < 12)
            return new EnvelopeCalibrationResult(ConservativeDefault, EnvelopeConfidence.Low, observations.Count, usable.Length, null);

        var pressures = observations.Select(x => x.CpuPressurePercent).OrderBy(x => x).ToArray();
        var eco = Math.Clamp(Quantile(pressures, 0.30), 8, 45);
        var efficient = Math.Clamp(Quantile(pressures, 0.65), eco + 8, 75);
        var responsive = Math.Clamp(Quantile(pressures, 0.85), efficient + 8, 95);

        if (responsive <= efficient)
            responsive = Math.Min(100, efficient + 8);
        if (efficient <= eco)
            efficient = Math.Min(responsive - 1, eco + 8);

        var powers = usable.Select(x => x.PackageWatts!.Value).OrderBy(x => x).ToArray();
        var ecoPower = Quantile(powers, 0.30);
        var efficientPower = Quantile(powers, 0.65);
        var responsivePower = Quantile(powers, 0.85);

        var confidence = usable.Length >= 120 && pressures.Length >= 120
            ? EnvelopeConfidence.High
            : usable.Length >= 24
                ? EnvelopeConfidence.Medium
                : EnvelopeConfidence.Low;

        var frontier = LearnResponseEfficiencyFrontier(usable);
        var envelope = new OperatingEnvelope(eco, efficient, responsive, ecoPower, efficientPower, responsivePower);
        return new EnvelopeCalibrationResult(envelope, confidence, observations.Count, usable.Length, frontier);
    }

    private static double? LearnResponseEfficiencyFrontier(IReadOnlyList<OperatingObservation> usable)
    {
        var sustained = usable
            .Where(x => x.CpuPressurePercent >= 40 && x.PackageWatts is > 0 && x.EffectiveClockMhz is > 0)
            .Select(x => new
            {
                Observation = x,
                Proxy = x.CpuPressurePercent * x.EffectiveClockMhz!.Value / x.PackageWatts!.Value
            })
            .OrderByDescending(x => x.Proxy)
            .ToArray();
        if (sustained.Length < 12) return null;

        var topCount = Math.Max(3, sustained.Length / 4);
        var efficientPowers = sustained.Take(topCount).Select(x => x.Observation.PackageWatts!.Value).OrderBy(x => x).ToArray();
        return Quantile(efficientPowers, 0.50);
    }

    private static double Quantile(IReadOnlyList<double> sorted, double quantile)
    {
        if (sorted.Count == 0) return 0;
        if (sorted.Count == 1) return sorted[0];
        var position = Math.Clamp(quantile, 0, 1) * (sorted.Count - 1);
        var lower = (int)Math.Floor(position);
        var upper = (int)Math.Ceiling(position);
        if (lower == upper) return sorted[lower];
        var fraction = position - lower;
        return sorted[lower] + (sorted[upper] - sorted[lower]) * fraction;
    }
}
