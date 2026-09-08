using Microsoft.UI.Xaml.Controls;
using PowerFlow.App.Controller;
using PowerFlow.Core.Policy;

namespace PowerFlow.App.Dashboard;

public sealed partial class StateRailControl : UserControl
{
    public StateRailControl() => InitializeComponent();

    public void Apply(ControllerSnapshot snapshot)
    {
        SaverCard.Opacity = snapshot.State == PowerState.PowerSaver ? 1 : 0.42;
        BalancedCard.Opacity = snapshot.State == PowerState.Balanced ? 1 : 0.42;
        PerformanceCard.Opacity = snapshot.State == PowerState.HighPerformance ? 1 : 0.42;
        SaverDot.Opacity = snapshot.State == PowerState.PowerSaver ? 1 : 0.18;
        BalancedDot.Opacity = snapshot.State == PowerState.Balanced ? 1 : 0.18;
        PerformanceDot.Opacity = snapshot.State == PowerState.HighPerformance ? 1 : 0.18;
        PerformanceSub.Text = snapshot.State == PowerState.HighPerformance && snapshot.IsLatched ? $"LOCKED - {snapshot.LatchType ?? "policy"}" : "game / manual lock";
    }
}
