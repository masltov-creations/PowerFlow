using PowerFlow.App.Dashboard;
using PowerFlow.Core.Envelope;
using Xunit;

namespace PowerFlow.App.Tests.Dashboard;

public sealed class PerformanceAtlasProjectionTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 9, 20, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Build_BucketsDeterministicallyAndPreservesExactObservationMembership()
    {
        var observations = new[]
        {
            Obs(0, 10, 25, 2000, 2, 8),
            Obs(1, 12, 27, 2100, 2, 8),
            Obs(2, 55, 75, 4200, 6, 8),
            Obs(3, 58, 78, 4300, 7, 8),
        };

        var first = PerformanceAtlasProjection.Build(observations, PerformanceAtlasDimension.PackagePower, PerformanceAtlasDimension.EffectiveClock, bins: 4);
        var second = PerformanceAtlasProjection.Build(observations, PerformanceAtlasDimension.PackagePower, PerformanceAtlasDimension.EffectiveClock, bins: 4);

        Assert.Equal(first.XAxis, second.XAxis);
        Assert.Equal(first.YAxis, second.YAxis);
        Assert.Equal(first.IncludedObservationIndices, second.IncludedObservationIndices);
        Assert.Equal(first.Cells.Count, second.Cells.Count);
        for (var i = 0; i < first.Cells.Count; i++)
        {
            Assert.Equal(first.Cells[i].XBin, second.Cells[i].XBin);
            Assert.Equal(first.Cells[i].YBin, second.Cells[i].YBin);
            Assert.Equal(first.Cells[i].SampleCount, second.Cells[i].SampleCount);
            Assert.Equal(first.Cells[i].Density, second.Cells[i].Density, 8);
            Assert.Equal(first.Cells[i].EfficiencyValue, second.Cells[i].EfficiencyValue);
            Assert.Equal(first.Cells[i].ObservationIndices, second.Cells[i].ObservationIndices);
        }
        Assert.Equal(new[] { 0, 1, 2, 3 }, first.IncludedObservationIndices);
        Assert.Equal(4, first.XAxis.BinCount);
        Assert.Equal(4, first.YAxis.BinCount);
        Assert.Equal(2, first.Cells.Count);
        Assert.Equal(new[] { 0, 1 }, first.Cells[0].ObservationIndices);
        Assert.Equal(new[] { 2, 3 }, first.Cells[1].ObservationIndices);
    }

    [Fact]
    public void Build_ExcludesOnlyObservationsMissingASelectedDimension()
    {
        var observations = new[]
        {
            Obs(0, 20, 35, 2500, 3, 8),
            Obs(1, 30, null, 2800, 4, 8),
            Obs(2, 40, 55, null, 5, 8),
            Obs(3, 50, 65, 3400, null, 8),
        };

        var powerVsClock = PerformanceAtlasProjection.Build(observations, PerformanceAtlasDimension.PackagePower, PerformanceAtlasDimension.EffectiveClock, bins: 4);
        Assert.Equal(new[] { 0, 3 }, powerVsClock.IncludedObservationIndices);

        var pressureVsPower = PerformanceAtlasProjection.Build(observations, PerformanceAtlasDimension.CpuPressure, PerformanceAtlasDimension.PackagePower, bins: 4);
        Assert.Equal(new[] { 0, 2, 3 }, pressureVsPower.IncludedObservationIndices);
    }

    [Fact]
    public void Build_NormalizesDensityAgainstMostPopulatedCell()
    {
        var observations = new[]
        {
            Obs(0, 10, 20, 1800, 2, 8),
            Obs(1, 11, 21, 1810, 2, 8),
            Obs(2, 12, 22, 1820, 2, 8),
            Obs(3, 90, 95, 4800, 8, 8),
        };

        var atlas = PerformanceAtlasProjection.Build(observations, PerformanceAtlasDimension.PackagePower, PerformanceAtlasDimension.EffectiveClock, bins: 4);
        var dense = Assert.Single(atlas.Cells, c => c.SampleCount == 3);
        var sparse = Assert.Single(atlas.Cells, c => c.SampleCount == 1);

        Assert.Equal(1d, dense.Density, 6);
        Assert.Equal(1d / 3d, sparse.Density, 6);
        Assert.All(atlas.Cells, cell => Assert.InRange(cell.Density, 0, 1));
    }

    [Fact]
    public void Build_DoesNotInventEfficiencyWithoutEvidence()
    {
        var observations = new[]
        {
            Obs(0, 25, 40, 3000, 4, 8),
            Obs(1, 30, 45, 3200, 4, 8),
        };

        var atlas = PerformanceAtlasProjection.Build(observations, PerformanceAtlasDimension.PackagePower, PerformanceAtlasDimension.EffectiveClock, bins: 4);
        Assert.All(atlas.Cells, cell => Assert.Null(cell.EfficiencyValue));

        var stillUnknown = PerformanceAtlasProjection.Build(observations, PerformanceAtlasDimension.PackagePower, PerformanceAtlasDimension.EffectiveClock, bins: 4, efficiencySelector: _ => null);
        Assert.All(stillUnknown.Cells, cell => Assert.Null(cell.EfficiencyValue));
    }

    [Fact]
    public void Build_AveragesOnlyFiniteEfficiencyEvidenceWithinEachCell()
    {
        var observations = new[]
        {
            Obs(0, 20, 30, 2500, 3, 8),
            Obs(1, 21, 31, 2510, 3, 8),
            Obs(2, 22, 32, 2520, 3, 8),
        };
        double? Evidence(OperatingObservation o) => o.At == T0.AddSeconds(0) ? 2 : o.At == T0.AddSeconds(1) ? 4 : double.NaN;

        var atlas = PerformanceAtlasProjection.Build(observations, PerformanceAtlasDimension.PackagePower, PerformanceAtlasDimension.EffectiveClock, bins: 4, efficiencySelector: Evidence);
        var cell = Assert.Single(atlas.Cells);
        Assert.NotNull(cell.EfficiencyValue);
        Assert.Equal(3d, cell.EfficiencyValue!.Value, 6);
    }

    [Fact]
    public void Build_UsesTruthfulStableAxisDomainsAndClampsMaxIntoFinalBin()
    {
        var observations = new[]
        {
            Obs(0, 0, 1, 500, 1, 8),
            Obs(1, 100, 99, 4999, 8, 8),
        };

        var pressureVsCores = PerformanceAtlasProjection.Build(observations, PerformanceAtlasDimension.CpuPressure, PerformanceAtlasDimension.ActiveCores, bins: 5);
        Assert.Equal(0, pressureVsCores.XAxis.DomainMin);
        Assert.Equal(100, pressureVsCores.XAxis.DomainMax);
        Assert.Equal(0, pressureVsCores.YAxis.DomainMin);
        Assert.Equal(8, pressureVsCores.YAxis.DomainMax);
        Assert.Contains(pressureVsCores.Cells, c => c.XBin == 4 && c.YBin == 4 && c.ObservationIndices.SequenceEqual(new[] { 1 }));

        var powerVsClock = PerformanceAtlasProjection.Build(observations, PerformanceAtlasDimension.PackagePower, PerformanceAtlasDimension.EffectiveClock, bins: 5);
        Assert.Equal(150, powerVsClock.XAxis.DomainMax);
        Assert.Equal(150, powerVsClock.YAxis.DomainMax);
    }

    [Fact]
    public void Build_RejectsSameDimensionAndInvalidBinCounts()
    {
        var observations = new[] { Obs(0, 10, 20, 2000, 2, 8) };
        Assert.Throws<ArgumentException>(() => PerformanceAtlasProjection.Build(observations, PerformanceAtlasDimension.CpuPressure, PerformanceAtlasDimension.CpuPressure));
        Assert.Throws<ArgumentOutOfRangeException>(() => PerformanceAtlasProjection.Build(observations, PerformanceAtlasDimension.CpuPressure, PerformanceAtlasDimension.PackagePower, bins: 1));
    }

    private static OperatingObservation Obs(int second, double pressure, double? watts, double? mhz, int? active, int? total) =>
        new(T0.AddSeconds(second), pressure, watts, mhz, active, total, EnvelopeZone.Efficient, null, EnvelopeDecisionKind.None, mhz is double value ? value / 40d : null);

    [Fact]
    public void AvailableDimensions_RequiresActualUsableSamples()
    {
        var observations = new[]
        {
            Obs(0, 20, null, 2500, null, 16),
            Obs(1, 30, null, 2800, null, 16),
        };

        var available = PerformanceAtlasProjection.AvailableDimensions(observations);

        Assert.Contains(PerformanceAtlasDimension.CpuPressure, available);
        Assert.Contains(PerformanceAtlasDimension.EffectiveClock, available);
        Assert.DoesNotContain(PerformanceAtlasDimension.PackagePower, available);
        Assert.DoesNotContain(PerformanceAtlasDimension.ActiveCores, available);
    }

    [Fact]
    public void ResolveAvailablePair_PreservesValidPreferenceAndExplainsFallback()
    {
        var observations = new[]
        {
            Obs(0, 20, 30, 2500, null, 16),
            Obs(1, 40, 50, 3500, null, 16),
        };

        var valid = PerformanceAtlasProjection.ResolveAvailablePair(observations, PerformanceAtlasDimension.PackagePower, PerformanceAtlasDimension.EffectiveClock);
        Assert.Equal(PerformanceAtlasDimension.PackagePower, valid.X);
        Assert.Equal(PerformanceAtlasDimension.EffectiveClock, valid.Y);
        Assert.False(valid.Changed);

        var fallback = PerformanceAtlasProjection.ResolveAvailablePair(observations, PerformanceAtlasDimension.ActiveCores, PerformanceAtlasDimension.PackagePower);
        Assert.True(fallback.Changed);
        Assert.NotEqual(fallback.X, fallback.Y);
        Assert.Contains(fallback.X, PerformanceAtlasProjection.AvailableDimensions(observations));
        Assert.Contains(fallback.Y, PerformanceAtlasProjection.AvailableDimensions(observations));
        Assert.Contains("Cores Awake", fallback.Explanation ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ProjectObservation_MapsTruthfulCurrentValuesIntoNormalizedAtlasCoordinates()
    {
        var observations = new[]
        {
            Obs(0, 10, 20, 2000, 2, 16),
            Obs(1, 50, 50, 3500, 8, 16),
        };
        var data = PerformanceAtlasProjection.Build(observations, PerformanceAtlasDimension.CpuPressure, PerformanceAtlasDimension.PackagePower, bins: 4);

        var point = PerformanceAtlasProjection.ProjectObservation(data, observations[1]);

        Assert.NotNull(point);
        Assert.Equal(50, point!.XValue, 6);
        Assert.Equal(50, point.YValue, 6);
        Assert.InRange(point.X, 0, 1);
        Assert.InRange(point.Y, 0, 1);
    }}