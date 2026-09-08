using PowerFlow.App.Controller;
using PowerFlow.Core.Policy;

namespace PowerFlow.App.Tray;

public static class TrayMenuCommands
{
    public const int OpenDashboard = 1001;
    public const int PowerSaver = 1101;
    public const int Balanced = 1102;
    public const int HighPerformance = 1103;
    public const int ReleaseLatch = 1104;
    public const int Settings = 1201;
    public const int Exit = 1999;

    public static TrayMenuModel Build(ControllerSnapshot snapshot)
    {
        var status = snapshot.IsLatched
            ? $"Performance Locked · {snapshot.LatchType ?? "Unknown"}"
            : snapshot.State switch
            {
                PowerState.PowerSaver => "Power Saver",
                PowerState.Balanced => "Balanced",
                PowerState.HighPerformance => "High Performance",
                _ => snapshot.State.ToString()
            };

        return new TrayMenuModel(
            status,
            snapshot.State == PowerState.PowerSaver,
            snapshot.State == PowerState.Balanced,
            snapshot.State == PowerState.HighPerformance,
            snapshot.IsLatched && string.Equals(snapshot.LatchType, "Manual", StringComparison.OrdinalIgnoreCase));
    }
}

public sealed record TrayMenuModel(
    string StatusText,
    bool PowerSaverChecked,
    bool BalancedChecked,
    bool HighPerformanceChecked,
    bool ReleaseLatchEnabled);

