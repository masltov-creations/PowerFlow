using Microsoft.UI.Xaml.Controls;
using PowerFlow.App.Controller;
using PowerFlow.Core.Rules;

namespace PowerFlow.App.Dashboard;

public sealed partial class RuleFlowControl : UserControl
{
    public RuleFlowControl() => InitializeComponent();
    public void Apply(PowerFlowConfig config, ControllerSnapshot snapshot) => RulesItems.ItemsSource = DashboardRuleProjection.Create(config, snapshot).ToArray();
}
