using System.Numerics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;

namespace PowerFlow.App.Dashboard;

public sealed partial class MainWindow
{
    private double _motionFromDisclosure;
    private double _motionToDisclosure;

    private void ApplyDisclosureProgress(double disclosure, bool settled)
    {
        var progress = Math.Clamp(disclosure, 0d, 1d);
        var context = ShellDisclosurePolicy.ContextProgress(progress);
        var footer = ShellDisclosurePolicy.FooterProgress(progress);

        var showContext = !settled || context > .001d;
        SelectedActorPanel.Visibility = showContext ? Visibility.Visible : Visibility.Collapsed;
        SelectedActorPanel.Opacity = context;
        SelectedActorPanel.IsHitTestVisible = context >= .72d;
        ActorContextColumn.Width = context <= .001d
            ? new GridLength(0)
            : new GridLength(Math.Max(.02d, .95d * context), GridUnitType.Star);
        var actorVisual = ElementCompositionPreview.GetElementVisual(SelectedActorPanel);
        actorVisual.Offset = new Vector3((float)(6d * (1d - context)), 0f, 0f);

        GovernorDetailPanel.Visibility = showContext ? Visibility.Visible : Visibility.Collapsed;
        GovernorDetailPanel.Opacity = context;
        GovernorDetailPanel.MaxHeight = 46d * context;
        var governorVisual = ElementCompositionPreview.GetElementVisual(GovernorDetailPanel);
        governorVisual.Offset = new Vector3(0f, (float)(4d * (1d - context)), 0f);

        var showFooter = !settled || footer > .001d;
        StatusFooter.Visibility = showFooter ? Visibility.Visible : Visibility.Collapsed;
        StatusFooter.Opacity = footer;
        StatusFooter.IsHitTestVisible = footer >= .72d;
        FooterRowDefinition.Height = new GridLength(24d * footer);
        var footerVisual = ElementCompositionPreview.GetElementVisual(StatusFooter);
        footerVisual.Offset = new Vector3(0f, (float)(4d * (1d - footer)), 0f);
    }
}