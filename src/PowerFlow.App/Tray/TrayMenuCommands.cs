using PowerFlow.App.Controller;
using PowerFlow.Core.Policy;

namespace PowerFlow.App.Tray;

public static class TrayMenuCommands
{
    public const int OpenDashboard = 1001;
    public const int Auto = 1100;
    public const int PowerSaver = 1101;
    public const int Balanced = 1102;
    public const int HighPerformance = 1103;
    public const int ReleaseLatch = 1104; // Legacy command id; routes to Auto.
    public const int Ultra = 1105;
    public const int BalancedPerformance = 1106;
    public const int Settings = 1201;
    public const int Exit = 1999;

    public static TrayMenuModel Build(ControllerSnapshot snapshot, PowerFlowOperatingMode? manualMode = null)
    {
        var stateLabel = snapshot.State switch
        {
            PowerState.PowerSaver => "Power Saver",
            PowerState.Balanced => "Balanced",
            PowerState.HighPerformance => "High Performance",
            _ => snapshot.State.ToString()
        };
        var manualAuthority = snapshot.IsLatched && string.Equals(snapshot.LatchType, "Manual", StringComparison.OrdinalIgnoreCase);
        var fallbackManualMode = manualAuthority ? snapshot.State switch
        {
            PowerState.PowerSaver => PowerFlowOperatingMode.Saver,
            PowerState.Balanced => PowerFlowOperatingMode.Balanced,
            PowerState.HighPerformance => PowerFlowOperatingMode.Ultra,
            _ => (PowerFlowOperatingMode?)null
        } : null;
        var selectedMode = manualMode ?? fallbackManualMode;
        var status = manualMode is PowerFlowOperatingMode mode
            ? $"{Friendly(mode)} - Manual"
            : snapshot.IsLatched
                ? snapshot.State == PowerState.HighPerformance
                    ? $"Performance Locked - {snapshot.LatchType ?? "Unknown"}"
                    : $"{stateLabel} Locked - {snapshot.LatchType ?? "Unknown"}"
                : stateLabel;

        return new TrayMenuModel(
            status,
            AutoChecked: !manualAuthority && manualMode is null,
            SaverChecked: selectedMode == PowerFlowOperatingMode.Saver,
            BalancedChecked: selectedMode == PowerFlowOperatingMode.Balanced,
            BalancedPerformanceChecked: selectedMode == PowerFlowOperatingMode.BalancedPerformance,
            PerformanceChecked: selectedMode == PowerFlowOperatingMode.Performance,
            UltraChecked: selectedMode == PowerFlowOperatingMode.Ultra,
            ReleaseLatchEnabled: manualAuthority);
    }

    private static string Friendly(PowerFlowOperatingMode mode) => mode switch
    {
        PowerFlowOperatingMode.Saver => "Saver",
        PowerFlowOperatingMode.Balanced => "Balanced Efficient",
        PowerFlowOperatingMode.BalancedPerformance => "Balanced Performance",
        PowerFlowOperatingMode.Performance => "Performance",
        PowerFlowOperatingMode.Ultra => "Ultra",
        _ => mode.ToString()
    };
}

public sealed record TrayMenuModel(
    string StatusText,
    bool AutoChecked,
    bool SaverChecked,
    bool BalancedChecked,
    bool BalancedPerformanceChecked,
    bool PerformanceChecked,
    bool UltraChecked,
    bool ReleaseLatchEnabled);