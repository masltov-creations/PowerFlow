using Xunit;

namespace PowerFlow.App.Tests.Dashboard;

public sealed class TensionShadowOverlayTests
{
    [Fact]
    public void Timeline_HasSeparateNonInteractiveDashedShadowEnvelopeState()
    {
        var code = Read("src", "PowerFlow.App", "Dashboard", "PerformanceTimelineControl.xaml.cs");

        Assert.Contains("private OperatingEnvelope? _shadowEnvelope;", code, StringComparison.Ordinal);
        Assert.Contains("public void SetShadowPolicyContext(OperatingEnvelope? envelope)", code, StringComparison.Ordinal);
        Assert.Contains("DrawShadowEnvelope", code, StringComparison.Ordinal);
        Assert.Contains("StrokeDashArray", code, StringComparison.Ordinal);
        Assert.Contains("IsHitTestVisible = false", code, StringComparison.Ordinal);
    }

    [Fact]
    public void ShadowSetter_DoesNotMutateTimelineTruthOrCurrentPolicyState()
    {
        var code = Read("src", "PowerFlow.App", "Dashboard", "PerformanceTimelineControl.xaml.cs");
        var body = MethodBody(code, "public void SetShadowPolicyContext(OperatingEnvelope? envelope)");

        Assert.Contains("_shadowEnvelope = envelope", body, StringComparison.Ordinal);
        Assert.Contains("RequestRedraw()", body, StringComparison.Ordinal);
        Assert.DoesNotContain("_data =", body, StringComparison.Ordinal);
        Assert.DoesNotContain("_observations =", body, StringComparison.Ordinal);
        Assert.DoesNotContain("_candidateTuning =", body, StringComparison.Ordinal);
        Assert.DoesNotContain("_draggingPolicyHandle", body, StringComparison.Ordinal);
    }

    private static string MethodBody(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Missing method {signature}");
        var open = source.IndexOf('{', start);
        Assert.True(open >= 0);
        var depth = 0;
        for (var i = open; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}' && --depth == 0) return source[open..(i + 1)];
        }
        throw new Xunit.Sdk.XunitException($"Unclosed method {signature}");
    }

    private static string Read(params string[] parts)
    {
        var dir = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(dir, "PowerFlow.sln")))
            dir = Directory.GetParent(dir)?.FullName ?? throw new DirectoryNotFoundException();
        var path = Path.Combine(new[] { dir }.Concat(parts).ToArray());
        Assert.True(File.Exists(path), $"Expected source file {path}");
        return File.ReadAllText(path);
    }
}
