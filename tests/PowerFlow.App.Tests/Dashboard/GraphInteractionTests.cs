using PowerFlow.App.Dashboard;
using PowerFlow.Core.Policy;
using Xunit;

namespace PowerFlow.App.Tests.Dashboard;

public sealed class GraphInteractionTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void HoverInterpolation_FollowsCursorBetweenSamplesInsteadOfSnapping()
    {
        var samples = new[]
        {
            new DashboardSample(T0, 20, 50, 3000, PowerState.PowerSaver),
            new DashboardSample(T0.AddSeconds(10), 80, 100, 4000, PowerState.PowerSaver)
        };
        var latest = samples[^1].At;
        var cursor = 55d / 60d;
        var value = TelemetryHoverProjection.Interpolate(samples, cursor, latest, 60);
        Assert.NotNull(value);
        Assert.Equal(50, value!.CpuPercent, 3);
        Assert.Equal(75, value.PackageWatts!.Value, 3);
        Assert.Equal(3500, value.AverageMhz!.Value, 3);
        Assert.Equal(T0.AddSeconds(5), value.At);
    }

    [Fact]
    public void ThresholdDragProjection_MapsPointerAndPreventsThresholdCrossing()
    {
        Assert.Equal(50, ThresholdDragProjection.PercentFromY(120, 20, 200), 3);
        Assert.Equal(34, ThresholdDragProjection.ClampQuiet(50, promotePercent: 35), 3);
        Assert.Equal(13, ThresholdDragProjection.ClampPromotion(5, quietPercent: 12), 3);
    }

    [Fact]
    public void GraphInteractionLayer_IsPersistentAndExposesThresholdCommit()
    {
        var root = FindRepoRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "PowerFlow.App", "Dashboard", "TelemetryGraphControl.xaml"));
        var code = File.ReadAllText(Path.Combine(root, "src", "PowerFlow.App", "Dashboard", "TelemetryGraphControl.xaml.cs"));
        Assert.Contains("x:Name=\"InteractionCanvas\"", xaml);
        Assert.Contains("primitives:Thumb", xaml);
        Assert.Contains("AutomationProperties.Name=\"Promotion threshold\"", xaml);
        Assert.Contains("DragDelta=\"OnPromoteDragDelta\"", xaml);
        Assert.Contains("AutomationProperties.Name=\"Quiet threshold\"", xaml);
        Assert.Contains("DragDelta=\"OnQuietDragDelta\"", xaml);
        Assert.Contains("ThresholdsCommitted", code);
    }

    [Fact]
    public void NativeThresholdThumbs_OwnPointerCaptureWithoutLegacyCanvasPressHandlers()
    {
        var root = FindRepoRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "PowerFlow.App", "Dashboard", "TelemetryGraphControl.xaml"));
        var code = File.ReadAllText(Path.Combine(root, "src", "PowerFlow.App", "Dashboard", "TelemetryGraphControl.xaml.cs"));

        Assert.DoesNotContain("PointerPressed=\"OnPointerPressed\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("PointerReleased=\"OnPointerReleased\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("CapturePointer", code, StringComparison.Ordinal);
        Assert.DoesNotContain("ReleasePointerCapture", code, StringComparison.Ordinal);
        Assert.Contains("DragDelta=\"OnPromoteDragDelta\"", xaml, StringComparison.Ordinal);
        Assert.Contains("DragDelta=\"OnQuietDragDelta\"", xaml, StringComparison.Ordinal);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "PowerFlow.sln"))) return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("PowerFlow.sln not found from test base directory.");
    }
}