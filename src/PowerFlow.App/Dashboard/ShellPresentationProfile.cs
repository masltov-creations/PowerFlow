namespace PowerFlow.App.Dashboard;

public enum NavigationPresentation { None, Overlay, Rail }
public enum HeaderPresentation { Minimal, Compact, System }
public enum ModePresentation { CurrentChip, Segmented, Cards }
public enum StatsPresentation { Inline, CompactRail, FullRail }
public enum TrajectoryPresentation { Minimal, Compact, Full }
public enum ControlContextPresentation { CauseLine, Rail, Modules }
public enum SecondaryPresentation { Hidden, Summary, Full }

public sealed record ShellGeometry(
    double NavigationWidth,
    double ContentPadding,
    double Gap,
    double HeaderHeight,
    double ModeBandHeight,
    double PrimaryGraphFraction,
    double ControlBandHeight,
    double SecondaryBandHeight);

public sealed record ShellPresentationProfile(
    PowerFlowShellState State,
    NavigationPresentation Navigation,
    HeaderPresentation Header,
    ModePresentation Modes,
    StatsPresentation Stats,
    TrajectoryPresentation Trajectory,
    ControlContextPresentation ControlContext,
    SecondaryPresentation Secondary,
    ShellGeometry Geometry);
