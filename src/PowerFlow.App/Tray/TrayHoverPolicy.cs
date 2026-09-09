namespace PowerFlow.App.Tray;

public enum TrayHoverAction
{
    None,
    Show,
    Hide,
    Cancel
}

public sealed class TrayHoverPolicy
{
    private readonly TimeSpan _showDelay;
    private DateTimeOffset? _hoverStartedAt;

    public TrayHoverPolicy(TimeSpan showDelay)
    {
        if (showDelay < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(showDelay));
        _showDelay = showDelay;
    }

    public bool IsVisible { get; private set; }

    public void BeginHover(DateTimeOffset at)
    {
        if (!IsVisible && _hoverStartedAt is null) _hoverStartedAt = at;
    }

    public TrayHoverAction Evaluate(DateTimeOffset at, bool pointerOverIcon, bool pointerOverPopup)
    {
        if (IsVisible)
        {
            if (pointerOverIcon || pointerOverPopup) return TrayHoverAction.None;
            IsVisible = false;
            _hoverStartedAt = null;
            return TrayHoverAction.Hide;
        }

        if (_hoverStartedAt is null) return TrayHoverAction.None;
        if (!pointerOverIcon)
        {
            _hoverStartedAt = null;
            return TrayHoverAction.Cancel;
        }

        if (at - _hoverStartedAt.Value < _showDelay) return TrayHoverAction.None;
        IsVisible = true;
        _hoverStartedAt = null;
        return TrayHoverAction.Show;
    }
}

public readonly record struct TrayRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Math.Max(0, Right - Left);
    public int Height => Math.Max(0, Bottom - Top);
    public bool Contains(int x, int y) => x >= Left && x < Right && y >= Top && y < Bottom;
}

public static class TrayHoverAnchorProjection
{
    public static TrayRect AroundPoint(int x, int y, int size)
    {
        if (size <= 0) throw new ArgumentOutOfRangeException(nameof(size));
        var left = x - (size / 2);
        var top = y - (size / 2);
        return new TrayRect(left, top, left + size, top + size);
    }

    public static TrayRect? Resolve(TrayRect? shellRect, TrayRect? observedRect, int pointerX, int pointerY)
    {
        if (shellRect is { } shell && shell.Contains(pointerX, pointerY)) return shell;
        if (observedRect is { } observed && observed.Contains(pointerX, pointerY)) return observed;
        return null;
    }
}
public static class TrayPopupPlacement
{
    public static TrayRect AboveIcon(TrayRect icon, TrayRect workArea, int width, int height, int gap)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        var centerX = icon.Left + (icon.Width / 2);
        var left = centerX - (width / 2);
        left = Math.Clamp(left, workArea.Left, Math.Max(workArea.Left, workArea.Right - width));
        var top = icon.Top - height - gap;
        top = Math.Clamp(top, workArea.Top, Math.Max(workArea.Top, workArea.Bottom - height));
        return new TrayRect(left, top, left + width, top + height);
    }
}

public static class TrayMotionPreference
{
    public static bool ShouldAnimate(bool? reducedMotionOverride, bool windowsAnimationsEnabled) => reducedMotionOverride switch
    {
        true => false,
        false => true,
        null => windowsAnimationsEnabled
    };
}
