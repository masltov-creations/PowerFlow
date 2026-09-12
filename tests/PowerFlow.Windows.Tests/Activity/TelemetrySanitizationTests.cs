using PowerFlow.Windows.Activity;
using Xunit;

namespace PowerFlow.Windows.Tests.Activity;

public sealed class TelemetrySanitizationTests
{
    [Theory]
    [InlineData(90792.25, 90.79225)]
    [InlineData(900, 0.9)]
    [InlineData(0, 0)]
    public void EnergyMeterPower_IsAlwaysConvertedFromMilliwatts(double rawMilliwatts, double expectedWatts)
    {
        Assert.Equal(expectedWatts, DashboardTelemetrySource.NormalizeEnergyMeterMilliwatts(rawMilliwatts)!.Value, 5);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void EnergyMeterPower_RejectsInvalidRawValues(double rawMilliwatts)
    {
        Assert.Null(DashboardTelemetrySource.NormalizeEnergyMeterMilliwatts(rawMilliwatts));
    }

    [Theory]
    [InlineData(130, 0, 250, 130)]
    [InlineData(250, 0, 250, 250)]
    public void PdhRangeValidation_PreservesValuesInsideRange(double raw, double min, double max, double expected)
    {
        Assert.Equal(expected, WindowsSystemMetricsProvider.NormalizeOptionalCounter(raw, min, max));
    }

    [Theory]
    [InlineData(-0.01, 0, 250)]
    [InlineData(250.01, 0, 250)]
    [InlineData(double.NaN, 0, 250)]
    [InlineData(double.PositiveInfinity, 0, 250)]
    public void PdhRangeValidation_RejectsValuesOutsideRangeInsteadOfClamping(double raw, double min, double max)
    {
        Assert.Null(WindowsSystemMetricsProvider.NormalizeOptionalCounter(raw, min, max));
    }
}
