namespace PowerFlow.App.Dashboard;

public enum ShellGeometryIntent
{
    TrayOpen,
    Expand,
    Shrink,
    Workspace,
    FullScreen,
    UserResize,
    UserMove,
    Navigation
}

public static class ShellGeometryAuthority
{
    public static bool ShouldChangeBounds(ShellGeometryIntent intent)
        => intent != ShellGeometryIntent.Navigation;
}