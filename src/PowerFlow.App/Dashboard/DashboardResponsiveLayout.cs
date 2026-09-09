namespace PowerFlow.App.Dashboard;

public sealed record DashboardLayoutProfile(
    DashboardPresentationMode Mode,
    bool ShowCompactTelemetry,
    bool ShowTelemetryCard,
    bool ShowContextRail,
    bool ShowFullContext,
    double StateFontSize,
    double MetricFontSize,
    double PanelHorizontalPadding,
    double PanelVerticalPadding,
    double HeaderColumnSpacing,
    double ReasonMaxWidth,
    double GraphMinHeight,
    double TrajectoryRowSpacing,
    double HoverLensMaxWidth,
    double ContextColumnSpacing);

public static class DashboardResponsiveLayout
{
    public static DashboardLayoutProfile Resolve(int width, int height, bool fullScreenPresenter)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);

        var mode = fullScreenPresenter || (width >= 1450 && height >= 840)
            ? DashboardPresentationMode.FullScreen
            : width >= 900 && height >= 560
                ? DashboardPresentationMode.Expanded
                : DashboardPresentationMode.Compressed;

        var widthProgress = Math.Clamp((width - 900d) / 700d, 0, 1);
        var heightProgress = Math.Clamp((height - 560d) / 500d, 0, 1);
        var fluid = Math.Min(widthProgress, heightProgress);

        if (mode == DashboardPresentationMode.Compressed)
        {
            return new DashboardLayoutProfile(
                mode, true, false, false, false,
                18, 14, 10, 7, 8,
                Math.Clamp(width * 0.32, 190, 280),
                Math.Clamp(height - 270, 150, 205),
                5, 250, 10);
        }

        if (mode == DashboardPresentationMode.Expanded)
        {
            return new DashboardLayoutProfile(
                mode, false, true, true, false,
                19 + 2 * fluid,
                15 + fluid,
                13 + 5 * fluid,
                9 + 3 * fluid,
                10 + 6 * fluid,
                320 + 220 * widthProgress,
                Math.Clamp(height - 390, 220, 430),
                6 + 2 * fluid,
                300 + 70 * widthProgress,
                14 + 10 * widthProgress);
        }

        return new DashboardLayoutProfile(
            mode, false, true, true, true,
            23, 18, 22, 15, 20,
            Math.Clamp(width * 0.34, 520, 760),
            Math.Clamp(height - 430, 450, 650),
            10, 410, 28);
    }
}