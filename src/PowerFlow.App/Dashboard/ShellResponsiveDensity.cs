namespace PowerFlow.App.Dashboard;

public enum ShellDensity
{
    Compact,
    Expanded
}

public static class ShellResponsiveDensity
{
    public const int EnterExpandedWidth = 900;
    public const int EnterExpandedHeight = 560;
    public const int ExitExpandedWidth = 860;
    public const int ExitExpandedHeight = 520;

    public const int MorphCompactWidth = 760;
    public const int MorphCompactHeight = 440;
    public const int MorphExpandedWidth = 900;
    public const int MorphExpandedHeight = 560;

    public static double MorphProgress(ShellLogicalSize size)
    {
        var widthProgress = Math.Clamp((size.Width - MorphCompactWidth) / (double)(MorphExpandedWidth - MorphCompactWidth), 0d, 1d);
        var heightProgress = Math.Clamp((size.Height - MorphCompactHeight) / (double)(MorphExpandedHeight - MorphCompactHeight), 0d, 1d);
        return Math.Min(widthProgress, heightProgress);
    }
    public static ShellDensity Resolve(ShellLogicalSize size, ShellDensity previous)
    {
        if (previous == ShellDensity.Compact)
            return size.Width >= EnterExpandedWidth && size.Height >= EnterExpandedHeight
                ? ShellDensity.Expanded
                : ShellDensity.Compact;

        return size.Width <= ExitExpandedWidth || size.Height <= ExitExpandedHeight
            ? ShellDensity.Compact
            : ShellDensity.Expanded;
    }
}
