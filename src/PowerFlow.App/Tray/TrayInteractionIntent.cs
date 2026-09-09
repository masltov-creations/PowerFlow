namespace PowerFlow.App.Tray;

public enum TrayInteractionKind
{
    Hover,
    SingleClick,
    DoubleClick
}

public static class TrayInteractionIntent
{
    public const uint MouseMove = 0x0200;
    public const uint LeftButtonUp = 0x0202;
    public const uint LeftButtonDoubleClick = 0x0203;
    public const uint PopupOpen = 0x0406;

    public static TrayInteractionKind? Project(uint message) => message switch
    {
        MouseMove or PopupOpen => TrayInteractionKind.Hover,
        LeftButtonUp => TrayInteractionKind.SingleClick,
        LeftButtonDoubleClick => TrayInteractionKind.DoubleClick,
        _ => null
    };
}

public sealed class TrayInteractionRequestedEventArgs(TrayInteractionKind kind) : EventArgs
{
    public TrayInteractionKind Kind { get; } = kind;
}