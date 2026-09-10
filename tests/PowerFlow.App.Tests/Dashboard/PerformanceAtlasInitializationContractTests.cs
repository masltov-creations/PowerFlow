using Xunit;

namespace PowerFlow.App.Tests.Dashboard;

public sealed class PerformanceAtlasInitializationContractTests
{
    [Fact]
    public void Atlas_DoesNotRenderFromXamlEventsUntilInitializeComponentCompletes()
    {
        var code = Read("src", "PowerFlow.App", "Dashboard", "PerformanceAtlasControl.xaml.cs");
        Assert.Contains("private bool _initialized;", code, StringComparison.Ordinal);
        Assert.Contains("_suppressDimensionEvents = true;", code, StringComparison.Ordinal);
        Assert.Contains("InitializeComponent();", code, StringComparison.Ordinal);
        Assert.Contains("_initialized = true;", code, StringComparison.Ordinal);
        Assert.Contains("if (!_initialized", code, StringComparison.Ordinal);
    }

    private static string Read(params string[] parts)
    {
        var dir = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(dir, "PowerFlow.sln")))
            dir = Directory.GetParent(dir)?.FullName ?? throw new DirectoryNotFoundException();
        return File.ReadAllText(Path.Combine(new[] { dir }.Concat(parts).ToArray()));
    }
}
