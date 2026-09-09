using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace PowerFlow.App.Dashboard;

public sealed partial class ShellHeaderControl : UserControl
{
    public ShellHeaderControl()
    {
        InitializeComponent();
        ApplyPresentation();
        ApplyPreviewMode();
    }

    public HeaderPresentation Presentation
    {
        get => (HeaderPresentation)GetValue(PresentationProperty);
        set => SetValue(PresentationProperty, value);
    }

    public static readonly DependencyProperty PresentationProperty =
        DependencyProperty.Register(
            nameof(Presentation),
            typeof(HeaderPresentation),
            typeof(ShellHeaderControl),
            new PropertyMetadata(HeaderPresentation.Compact, OnPresentationChanged));

    public bool IsPreviewMode
    {
        get => (bool)GetValue(IsPreviewModeProperty);
        set => SetValue(IsPreviewModeProperty, value);
    }

    public static readonly DependencyProperty IsPreviewModeProperty =
        DependencyProperty.Register(
            nameof(IsPreviewMode),
            typeof(bool),
            typeof(ShellHeaderControl),
            new PropertyMetadata(false, OnPreviewModeChanged));

    private static void OnPresentationChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((ShellHeaderControl)d).ApplyPresentation();

    private static void OnPreviewModeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((ShellHeaderControl)d).ApplyPreviewMode();

    private void ApplyPresentation()
    {
        if (MinimalHeader is null) return;
        MinimalHeader.Visibility = Presentation == HeaderPresentation.Minimal ? Visibility.Visible : Visibility.Collapsed;
        CompactHeader.Visibility = Presentation == HeaderPresentation.Compact ? Visibility.Visible : Visibility.Collapsed;
        SystemHeader.Visibility = Presentation == HeaderPresentation.System ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ApplyPreviewMode()
    {
        if (PreviewBadge is null) return;
        PreviewBadge.Visibility = IsPreviewMode ? Visibility.Visible : Visibility.Collapsed;
    }
}
