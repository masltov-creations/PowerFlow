using PowerFlow.App.Telemetry;
using PowerFlow.Core.Policy;
using PowerFlow.Windows.Activity;
using Xunit;

namespace PowerFlow.App.Tests.Telemetry;

public sealed class GraduatedCapacityEntitlementTests
{
    [Fact]
    public void Build_ConvertsDemandIntoAnInspectableIdealCapacity()
    {
        var at = DateTimeOffset.UtcNow;
        var data = GraduatedCapacityEntitlementModel.Build(
            new[] { Sample(at, pressure: 30, demand: 15, delivered: 100) },
            at.AddSeconds(-1), at);

        var point = Assert.Single(data.Samples);
        Assert.Equal(60, point.TargetSaturationPercent, 3);
        Assert.Equal(25, point.IdealCapacityPercent, 3);
        Assert.Equal(25, point.RequestedCapacityPercent, 3);
        Assert.Equal(100, point.DeliveredCapacityPercent, 3);
    }

    [Fact]
    public void Build_RisingPressureEarnsMoreHeadroomThanFlatPressure()
    {
        var at = DateTimeOffset.UtcNow;
        var rising = GraduatedCapacityEntitlementModel.Build(new[]
        {
            Sample(at, 40, 30, 50),
            Sample(at.AddSeconds(1), 70, 30, 50)
        }, at, at.AddSeconds(1));
        var flat = GraduatedCapacityEntitlementModel.Build(new[]
        {
            Sample(at, 70, 30, 50),
            Sample(at.AddSeconds(1), 70, 30, 50)
        }, at, at.AddSeconds(1));

        Assert.True(rising.Samples[^1].RequestedCapacityPercent > flat.Samples[^1].RequestedCapacityPercent);
        Assert.True(rising.Samples[^1].RampPercentPerSecond > 0);
        Assert.Equal("RAMP", rising.Samples[^1].Driver);
    }

    [Fact]
    public void Build_ReleasesCapacityMoreSlowlyThanItCanRise()
    {
        var at = DateTimeOffset.UtcNow;
        var data = GraduatedCapacityEntitlementModel.Build(new[]
        {
            Sample(at, 90, 60, 100),
            Sample(at.AddSeconds(1), 20, 6, 100)
        }, at, at.AddSeconds(1));

        var first = data.Samples[0];
        var second = data.Samples[1];
        Assert.True(first.RequestedCapacityPercent - second.RequestedCapacityPercent <= GraduatedCapacityEntitlementModel.FallSlewPercentPerSecond + .001);
        Assert.True(second.RequestedCapacityPercent > second.IdealCapacityPercent);
    }

    [Fact]
    public void Build_AccumulatesBurstAgeOnlyWhilePressureStaysHigh()
    {
        var at = DateTimeOffset.UtcNow;
        var data = GraduatedCapacityEntitlementModel.Build(new[]
        {
            Sample(at, 75, 35, 50),
            Sample(at.AddSeconds(1), 76, 35, 50),
            Sample(at.AddSeconds(2), 30, 15, 50)
        }, at, at.AddSeconds(2));

        Assert.Equal(0, data.Samples[0].BurstAgeSeconds, 3);
        Assert.Equal(1, data.Samples[1].BurstAgeSeconds, 3);
        Assert.Equal(0, data.Samples[2].BurstAgeSeconds, 3);
    }

    private static ContinuitySample Sample(DateTimeOffset at, double pressure, double demand, double delivered)
    {
        var logical = new[]
        {
            new LogicalProcessorTelemetry(0, 0, false, demand, 3000, delivered),
            new LogicalProcessorTelemetry(1, 0, false, demand, 3000, delivered)
        };
        var observed = new DemandPressureTelemetry(pressure, demand, delivered, pressure, 0, 0, 1, 1, "SATURATION");
        return new ContinuitySample(
            At: at,
            CpuPercent: 0,
            PackageWatts: null,
            AverageMhz: null,
            State: PowerState.Balanced,
            Reason: "test",
            IsLatched: false,
            LatchType: null,
            ThresholdProgress: 0,
            TriggerApplication: null,
            ActiveCores: 1,
            TotalCores: 1,
            LogicalProcessors: logical,
            DemandPressure: observed);
    }
}