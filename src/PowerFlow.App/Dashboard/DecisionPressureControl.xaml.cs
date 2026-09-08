using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PowerFlow.App.Controller;
using PowerFlow.Core.Rules;

namespace PowerFlow.App.Dashboard;

public sealed partial class DecisionPressureControl : UserControl
{
    private DecisionMeterState? _state;

    public DecisionPressureControl() => InitializeComponent();

    public void Apply(PowerFlowConfig config, ControllerSnapshot snapshot)
    {
        _state = DecisionMeterProjection.Create(config, snapshot);
        TitleText.Text = _state.Title;
        ValueText.Text = snapshot.IsLatched ? "LOCKED" : $"{_state.ValuePercent:0.0}% CPU";
        ExplanationText.Text = _state.Explanation;
        QuietLabel.Text = $"QUIET {_state.QuietMarkerPercent:0.#}%";
        PromotionLabel.Text = $"PROMOTE {_state.PromotionMarkerPercent:0.#}%";
        UpdateMeter();
    }

    private void OnMeterSizeChanged(object sender, SizeChangedEventArgs e) => UpdateMeter();

    private void UpdateMeter()
    {
        if (_state is null || MeterTrack.ActualWidth <= 0) return;
        var w = MeterTrack.ActualWidth;
        PressureFill.Width = Math.Clamp(_state.ValuePercent / 100d, 0, 1) * w;
        QuietMarker.Margin = new Thickness(Math.Clamp(_state.QuietMarkerPercent / 100d, 0, 1) * w - 1, 0, 0, 0);
        PromotionMarker.Margin = new Thickness(Math.Clamp(_state.PromotionMarkerPercent / 100d, 0, 1) * w - 1, 0, 0, 0);
    }
}
