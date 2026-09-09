using PowerFlow.App.Tray;
using Xunit;

namespace PowerFlow.App.Tests.Shell;

public sealed class TrayInteractionIntentTests
{
    [Theory]
    [InlineData(0x0200u, TrayInteractionKind.Hover)]
    [InlineData(0x0406u, TrayInteractionKind.Hover)]
    [InlineData(0x0202u, TrayInteractionKind.SingleClick)]
    [InlineData(0x0203u, TrayInteractionKind.DoubleClick)]
    public void Project_MapsTrayMouseMessages(uint message, TrayInteractionKind expected)
        => Assert.Equal(expected, TrayInteractionIntent.Project(message));

    [Fact]
    public void Project_IgnoresUnrelatedMouseMessages()
        => Assert.Null(TrayInteractionIntent.Project(0x0201u));

    [Fact]
    public void TrayHost_HandlesSingleLeftClickAsFirstClassInteraction()
    {
        var trayHost = File.ReadAllText(Path.Combine(RepoRoot(), "src", "PowerFlow.App", "Tray", "TrayIconHost.cs"));
        Assert.Contains("WmLButtonUp", trayHost, StringComparison.Ordinal);
        Assert.Contains("InteractionRequested", trayHost, StringComparison.Ordinal);
        Assert.Contains("TrayInteractionKind.SingleClick", trayHost, StringComparison.Ordinal);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "PowerFlow.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException();
    }
}