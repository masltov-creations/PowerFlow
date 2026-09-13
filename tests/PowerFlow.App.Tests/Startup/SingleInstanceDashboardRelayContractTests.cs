using Xunit;

namespace PowerFlow.App.Tests.Startup;

public sealed class SingleInstanceDashboardRelayContractTests
{
    [Fact]
    public void AppForwardsDashboardRequestToPrimaryInsteadOfSilentlyExiting()
    {
        var source = File.ReadAllText(RepoFile("src", "PowerFlow.App", "App.xaml.cs"));

        Assert.Contains("SingleInstanceSignal.Listen", source, StringComparison.Ordinal);
        Assert.Contains("SingleInstanceSignal.TrySignal", source, StringComparison.Ordinal);
        Assert.Contains("DrainDashboardOpenRequestAsync", source, StringComparison.Ordinal);
        Assert.Contains("LaunchIntent.ShouldOpenDashboard", source, StringComparison.Ordinal);
        Assert.Contains("ShutdownSignalName", source, StringComparison.Ordinal);
        Assert.Contains("LaunchIntent.ShouldShutdown", source, StringComparison.Ordinal);
        Assert.Contains("OnShutdownSignal", source, StringComparison.Ordinal);
    }

    private static string RepoFile(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "PowerFlow.sln"))) dir = dir.Parent;
        return Path.Combine(new[] { dir?.FullName ?? throw new DirectoryNotFoundException() }.Concat(parts).ToArray());
    }
}
