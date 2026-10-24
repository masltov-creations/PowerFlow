namespace PowerFlow.App.Dashboard.HarmonicRing;

public enum HarmonicRingDisclosure
{
    Circle,
    Card,
    Panel
}

/// <summary>Pure size policy for progressively disclosing the ring presentation.</summary>
public static class HarmonicRingLayout
{
    public const double EnterCardWidth = 560;
    public const double EnterCardHeight = 360;
    public const double ExitCardWidth = 520;
    public const double ExitCardHeight = 320;

    // Match the existing shell's expanded-density thresholds.
    public const double EnterPanelWidth = 900;
    public const double EnterPanelHeight = 560;
    public const double ExitPanelWidth = 860;
    public const double ExitPanelHeight = 520;

    public static HarmonicRingDisclosure ResolveDisclosure(
        double usableWidth,
        double usableHeight,
        HarmonicRingDisclosure current)
    {
        if (!Enum.IsDefined(current)) throw new ArgumentOutOfRangeException(nameof(current));
        if (!double.IsFinite(usableWidth) || !double.IsFinite(usableHeight) || usableWidth < 0 || usableHeight < 0)
            return current;

        var canEnterCard = usableWidth >= EnterCardWidth && usableHeight >= EnterCardHeight;
        var canKeepCard = usableWidth > ExitCardWidth && usableHeight > ExitCardHeight;
        var canEnterPanel = usableWidth >= EnterPanelWidth && usableHeight >= EnterPanelHeight;
        var canKeepPanel = usableWidth > ExitPanelWidth && usableHeight > ExitPanelHeight;

        return current switch
        {
            HarmonicRingDisclosure.Circle => canEnterPanel
                ? HarmonicRingDisclosure.Panel
                : canEnterCard ? HarmonicRingDisclosure.Card : HarmonicRingDisclosure.Circle,
            HarmonicRingDisclosure.Card => canEnterPanel
                ? HarmonicRingDisclosure.Panel
                : canKeepCard ? HarmonicRingDisclosure.Card : HarmonicRingDisclosure.Circle,
            HarmonicRingDisclosure.Panel => canKeepPanel
                ? HarmonicRingDisclosure.Panel
                : canKeepCard ? HarmonicRingDisclosure.Card : HarmonicRingDisclosure.Circle,
            _ => throw new ArgumentOutOfRangeException(nameof(current))
        };
    }
}
