using Xunit;

namespace PowerFlow.App.Tests.Dashboard;

public sealed class GraphVisualHierarchyContractTests
{
    [Fact]
    public void Timeline_PolicyRendersBehindTelemetryAndNormalViewAvoidsDashedClutter()
    {
        var xaml = Read("src", "PowerFlow.App", "Dashboard", "PerformanceTimelineControl.xaml");
        var code = Read("src", "PowerFlow.App", "Dashboard", "PerformanceTimelineControl.xaml.cs");
        var policyTime = xaml.IndexOf("x:Name=\"PolicyTimeLayer\"", StringComparison.Ordinal);
        var policyValue = xaml.IndexOf("x:Name=\"PolicyValueLayer\"", StringComparison.Ordinal);
        var trace = xaml.IndexOf("x:Name=\"TraceLayer\"", StringComparison.Ordinal);
        Assert.True(policyTime >= 0 && policyTime < trace);
        Assert.True(policyValue >= 0 && policyValue < trace);
        Assert.Contains("StrokeThickness=\"2.35\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("AddText(PolicyValueLayer", code, StringComparison.Ordinal);
        Assert.DoesNotContain("AddText(PolicyTimeLayer", code, StringComparison.Ordinal);
        Assert.DoesNotContain("line.StrokeDashArray", code, StringComparison.Ordinal);
        Assert.DoesNotContain("edge.StrokeDashArray", code, StringComparison.Ordinal);
        Assert.Contains("if (_tuneMode && Math.Abs(learnedY - candidateY) > .5)", code, StringComparison.Ordinal);
        Assert.Contains("if (_tuneMode && Math.Abs(learnedX - candidateX) > .5)", code, StringComparison.Ordinal);
    }

    private static string Read(params string[] parts)
    {
        var dir = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(dir, "PowerFlow.sln")))
            dir = Directory.GetParent(dir)?.FullName ?? throw new DirectoryNotFoundException();
        return File.ReadAllText(Path.Combine(new[] { dir }.Concat(parts).ToArray()));
    }
}