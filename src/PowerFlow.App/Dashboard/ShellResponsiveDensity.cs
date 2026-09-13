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
