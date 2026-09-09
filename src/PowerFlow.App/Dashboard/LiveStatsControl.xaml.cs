using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace PowerFlow.App.Dashboard;

public sealed partial class LiveStatsControl : UserControl
{
    public LiveStatsControl()
    {
        InitializeComponent();
        ApplyPresentation();
    }

    public StatsPresentation Presentation
    {
        get => (StatsPresentation)GetValue(PresentationProperty);
        set => SetValue(PresentationProperty, value);
    }

    public static readonly DependencyProperty PresentationProperty =
        DependencyProperty.Register(nameof(Presentation), typeof(StatsPresentation), typeof(LiveStatsControl),
            new PropertyMetadata(StatsPresentation.Inline, OnPresentationChanged));

    private static void OnPresentationChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((LiveStatsControl)d).ApplyPresentation();

    private void ApplyPresentation()
    {
        if (InlineStats is null) return;
        InlineStats.Visibility = Presentation == StatsPresentation.Inline ? Visibility.Visible : Visibility.Collapsed;
        CompactStatsRail.Visibility = Presentation == StatsPresentation.CompactRail ? Visibility.Visible : Visibility.Collapsed;
        FullStatsRail.Visibility = Presentation == StatsPresentation.FullRail ? Visibility.Visible : Visibility.Collapsed;
    }
}
