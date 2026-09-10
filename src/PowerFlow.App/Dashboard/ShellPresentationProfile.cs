namespace PowerFlow.App.Dashboard;

public enum NavigationPresentation { None, Overlay, Rail }
public enum HeaderPresentation { Minimal, Compact, System }
public enum TimelinePresentation { Glance, Compact, Expanded, Full }
public enum GovernorControlPresentation { Summary, Bias, Contextual, Deep }

public sealed record ShellGeometry(
    double NavigationWidth,
    double ContentPadding,
    double Gap,
    double HeaderHeight,
    double ControlBandHeight);

public sealed record ShellPresentationProfile(
    PowerFlowShellState State,
    NavigationPresentation Navigation,
    HeaderPresentation Header,
    TimelinePresentation Timeline,
    GovernorControlPresentation GovernorControls,
    ShellGeometry Geometry);