using System.Runtime.InteropServices;

namespace PowerFlow.App.Tray;

public sealed class TrayIconFrameCache : IDisposable
{
    private const uint ImageIcon = 1;
    private const uint LrLoadFromFile = 0x00000010;
    private const uint LrDefaultSize = 0x00000040;
    private readonly IntPtr[] _frames = new IntPtr[5];
    private readonly HashSet<IntPtr> _owned = [];

    public TrayIconFrameCache(string baseDirectory)
    {
        var trayDirectory = Path.Combine(baseDirectory, "Assets", "Tray");
        var fallbackPath = Path.Combine(baseDirectory, "Assets", "PowerFlow.ico");
        IntPtr fallback = IntPtr.Zero;
        for (var i = 0; i < _frames.Length; i++)
        {
            var path = Path.Combine(trayDirectory, $"PowerFlow.Tray.{i}.ico");
            var icon = LoadOwned(path);
            if (icon == IntPtr.Zero)
            {
                fallback = fallback == IntPtr.Zero ? LoadOwned(fallbackPath) : fallback;
                icon = fallback;
            }
            _frames[i] = icon;
        }
    }

    public IntPtr GetFrame(int index)
        => _frames[Math.Clamp(index, 0, _frames.Length - 1)];

    public void Dispose()
    {
        foreach (var icon in _owned)
            if (icon != IntPtr.Zero) DestroyIcon(icon);
        _owned.Clear();
        Array.Clear(_frames);
    }

    private IntPtr LoadOwned(string path)
    {
        if (!File.Exists(path)) return IntPtr.Zero;
        var icon = LoadImage(IntPtr.Zero, path, ImageIcon, 0, 0, LrLoadFromFile | LrDefaultSize);
        if (icon != IntPtr.Zero) _owned.Add(icon);
        return icon;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadImage(IntPtr instance, string name, uint type, int cx, int cy, uint loadFlags);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr icon);
}