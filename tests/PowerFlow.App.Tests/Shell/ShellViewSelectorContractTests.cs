using Xunit;

namespace PowerFlow.App.Tests.Shell;

public sealed class ShellViewSelectorContractTests
{
    [Fact]
    public void Shell_ExposesHoverCompactExpandedSelectorAndKeepsHoverHeaderInteractive()
    {
        var xaml = Read("src", "PowerFlow.App", "Dashboard", "MainWindow.xaml");
        var code = Read("src", "PowerFlow.App", "Dashboard", "MainWindow.xaml.cs");
        Assert.Contains("x:Name=\"ViewModeButton\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"HOVER\" Click=\"OnHoverViewClicked\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"COMPACT\" Click=\"OnCompactViewClicked\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"EXPANDED\" Click=\"OnExpandedViewClicked\"", xaml, StringComparison.Ordinal);
        Assert.Contains("interactiveGlance", code, StringComparison.Ordinal);
        Assert.Contains("GlanceTapTarget.Visibility", code, StringComparison.Ordinal);
    }

    [Fact]
    public void CompactAndHover_AutoSizeScaledTextInsteadOfClippingFixedBands()
    {
        var code = Read("src", "PowerFlow.App", "Dashboard", "MainWindow.xaml.cs");
        Assert.Contains("HeaderPresentation.Minimal or HeaderPresentation.Compact", code, StringComparison.Ordinal);
        Assert.Contains("GovernorControlPresentation.Bias", code, StringComparison.Ordinal);
        Assert.Contains("TimelineRowDefinition.MinHeight = state == PowerFlowShellState.Glance ? 92d : 156d", code, StringComparison.Ordinal);
        Assert.Contains("ShellResizeStateProjection.ClampMinimum", code, StringComparison.Ordinal);
    }

    private static string Read(params string[] parts)
    {
        var dir = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(dir, "PowerFlow.sln")))
            dir = Directory.GetParent(dir)?.FullName ?? throw new DirectoryNotFoundException("PowerFlow repo root not found");
        return File.ReadAllText(Path.Combine(new[] { dir }.Concat(parts).ToArray()));
    }
}