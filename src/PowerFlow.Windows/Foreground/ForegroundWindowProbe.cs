using System.Diagnostics;
using System.Runtime.InteropServices;

namespace PowerFlow.Windows.Foreground;

public sealed record ForegroundWindowSnapshot(int ProcessId, string ExecutablePath, bool IsFullscreen);

public sealed class ForegroundWindowProbe
{
    public ForegroundWindowSnapshot? Read()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return null;
        GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == 0 || !GetWindowRect(hwnd, out var window)) return null;
        var monitor = MonitorFromWindow(hwnd, 2);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info)) return null;
        var wa = info.Work;
        var windowArea = Math.Max(0, window.Right - window.Left) * (long)Math.Max(0, window.Bottom - window.Top);
        var workArea = Math.Max(1, wa.Right - wa.Left) * (long)Math.Max(1, wa.Bottom - wa.Top);
        var fullscreen = windowArea >= workArea * 0.90;
        string path = pid.ToString();
        try { using var p = Process.GetProcessById((int)pid); path = p.MainModule?.FileName ?? p.ProcessName; } catch { }
        return new ForegroundWindowSnapshot((int)pid, path, fullscreen);
    }

    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public Rect Monitor; public Rect Work; public uint Flags; }
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
}
