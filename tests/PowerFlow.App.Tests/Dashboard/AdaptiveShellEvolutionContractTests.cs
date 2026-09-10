using Xunit;

namespace PowerFlow.App.Tests.Dashboard;

public sealed class AdaptiveShellEvolutionContractTests
{
    [Fact]
    public void Shell_UsesOnePerformanceTimelineAsThePrimaryInstrument()
    {
        var xaml = Read("src", "PowerFlow.App", "Dashboard", "MainWindow.xaml");
        var code = Read("src", "PowerFlow.App", "Dashboard", "MainWindow.xaml.cs");

        Assert.Contains("<dash:PerformanceTimelineControl x:Name=\"PerformanceTimeline\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"AdaptiveControlRegion\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"MachineEnvelopePanel\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SelectedActorPanel\"", xaml, StringComparison.Ordinal);
        Assert.Contains("MACHINE ENVELOPE", xaml, StringComparison.Ordinal);
        Assert.Contains("SELECTED ACTOR", xaml, StringComparison.Ordinal);
        Assert.Contains("PerformanceTimeline.Apply", code, StringComparison.Ordinal);
        Assert.Contains("EnvelopeCalibration.Calibrate", code, StringComparison.Ordinal);

        Assert.DoesNotContain("<dash:TrajectoryControl x:Name=\"Trajectory\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("<dash:PowerModeControl x:Name=\"PowerModeBandHost\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("<dash:LiveStatsControl x:Name=\"LiveStatsHost\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void ExpandedNavigation_UsesGovernorMentalModelRatherThanGenericDashboardHierarchy()
    {
        var xaml = Read("src", "PowerFlow.App", "Dashboard", "MainWindow.xaml");

        foreach (var label in new[] { "Content=\"Live\"", "Content=\"Apps\"", "Content=\"Model\"", "Content=\"Tune\"" })
            Assert.Contains(label, xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Content=\"Dashboard\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Content=\"Rules / Apps\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void SemanticProfile_DefinesTimelineAndGovernorDepthAcrossEveryVisibleShellState()
    {
        var profile = Read("src", "PowerFlow.App", "Dashboard", "ShellPresentationProfile.cs");
        var layout = Read("src", "PowerFlow.App", "Dashboard", "PowerFlowShellLayout.cs");

        Assert.Contains("TimelinePresentation", profile, StringComparison.Ordinal);
        Assert.Contains("GovernorControlPresentation", profile, StringComparison.Ordinal);
        Assert.Contains("TimelinePresentation.Glance", layout, StringComparison.Ordinal);
        Assert.Contains("TimelinePresentation.Compact", layout, StringComparison.Ordinal);
        Assert.Contains("TimelinePresentation.Expanded", layout, StringComparison.Ordinal);
        Assert.Contains("TimelinePresentation.Full", layout, StringComparison.Ordinal);
        Assert.Contains("GovernorControlPresentation.Summary", layout, StringComparison.Ordinal);
        Assert.Contains("GovernorControlPresentation.Bias", layout, StringComparison.Ordinal);
        Assert.Contains("GovernorControlPresentation.Contextual", layout, StringComparison.Ordinal);
        Assert.Contains("GovernorControlPresentation.Deep", layout, StringComparison.Ordinal);
    }

    [Fact]
    public void ShellProfile_AndMainWindow_DoNotCarryRetiredCockpitPresentationSeams()
    {
        var profile = Read("src", "PowerFlow.App", "Dashboard", "ShellPresentationProfile.cs");
        var xaml = Read("src", "PowerFlow.App", "Dashboard", "MainWindow.xaml");
        var code = Read("src", "PowerFlow.App", "Dashboard", "MainWindow.xaml.cs");

        foreach (var retired in new[] { "ModePresentation", "StatsPresentation", "TrajectoryPresentation", "ControlContextPresentation", "SecondaryPresentation" })
            Assert.DoesNotContain(retired, profile, StringComparison.Ordinal);
        Assert.DoesNotContain("dash:ControlContextControl", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("dash:OperationalContextControl", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("ControlContextBandHost", code, StringComparison.Ordinal);
        Assert.DoesNotContain("SecondaryOperationalRow", code, StringComparison.Ordinal);
        Assert.DoesNotContain("ApplySecondaryMorph", code, StringComparison.Ordinal);
    }
    [Fact]
    public void TimelineControl_AdaptsItsDensityWithoutCreatingAnotherSamplerOrWindow()
    {
        var xaml = Read("src", "PowerFlow.App", "Dashboard", "PerformanceTimelineControl.xaml");
        var code = Read("src", "PowerFlow.App", "Dashboard", "PerformanceTimelineControl.xaml.cs");

        Assert.Contains("x:Name=\"GlanceSummary\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"LaneLabelColumn\"", xaml, StringComparison.Ordinal);
        Assert.Contains("TimelinePresentation Presentation", code, StringComparison.Ordinal);
        Assert.Contains("SetPresentation", code, StringComparison.Ordinal);
        Assert.DoesNotContain("DispatcherTimer", code, StringComparison.Ordinal);
        Assert.DoesNotContain("new Window", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Shell_PreservesExistingSingleWindowTransitionMechanics()
    {
        var code = Read("src", "PowerFlow.App", "Dashboard", "MainWindow.xaml.cs");

        Assert.Contains("TransitionToAsync", code, StringComparison.Ordinal);
        Assert.Contains("AppWindow.MoveAndResize", code, StringComparison.Ordinal);
        Assert.Contains("AppWindow.SetPresenter(AppWindowPresenterKind.FullScreen)", code, StringComparison.Ordinal);
        Assert.Contains("ShellActivationMode.TransientNoActivate", code, StringComparison.Ordinal);
        Assert.Contains("GlanceTapTarget", code, StringComparison.Ordinal);
    }

    private static string Read(params string[] parts)
    {
        var dir = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(dir, "PowerFlow.sln")))
            dir = Directory.GetParent(dir)?.FullName ?? throw new DirectoryNotFoundException();
        return File.ReadAllText(Path.Combine(new[] { dir }.Concat(parts).ToArray()));
    }
}
