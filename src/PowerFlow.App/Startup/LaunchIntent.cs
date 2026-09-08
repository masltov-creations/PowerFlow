namespace PowerFlow.App.Startup;

public static class LaunchIntent
{
    public static bool IsPreview(IEnumerable<string> args) => args.Any(a => string.Equals(a, "--preview", StringComparison.OrdinalIgnoreCase));
    public static bool ShouldOpenDashboard(IEnumerable<string> args) => IsPreview(args) || args.Any(a => string.Equals(a, "--dashboard", StringComparison.OrdinalIgnoreCase));
    public static bool ShouldCreateTray(IEnumerable<string> args) => !IsPreview(args);
    public static bool ShouldExitWithDashboard(IEnumerable<string> args) => IsPreview(args);
}