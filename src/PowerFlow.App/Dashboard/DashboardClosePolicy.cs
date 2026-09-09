namespace PowerFlow.App.Dashboard;

public enum DashboardCloseDisposition
{
    HideToTray,
    Close
}

public static class DashboardClosePolicy
{
    public static DashboardCloseDisposition Decide(bool previewMode, bool explicitShutdown) =>
        previewMode || explicitShutdown ? DashboardCloseDisposition.Close : DashboardCloseDisposition.HideToTray;
}