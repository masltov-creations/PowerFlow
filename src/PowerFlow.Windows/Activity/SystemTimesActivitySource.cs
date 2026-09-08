using System.Diagnostics;
using System.Runtime.InteropServices;

namespace PowerFlow.Windows.Activity;

public readonly record struct SystemTimesSnapshot(ulong Idle, ulong Kernel, ulong User);

public interface ISystemTimesNative
{
    SystemTimesSnapshot Read();
}

public sealed class SystemTimesActivitySource : IActivitySource
{
    private readonly ISystemTimesNative _native;
    private SystemTimesSnapshot? _previous;

    public SystemTimesActivitySource() : this(new SystemTimesNative()) { }
    public SystemTimesActivitySource(ISystemTimesNative native) => _native = native;

    public ActivitySample Sample(DateTimeOffset at)
    {
        var sw = Stopwatch.StartNew();
        var current = _native.Read();
        var previous = _previous;
        _previous = current;
        if (previous is null)
            return new ActivitySample(0, at, true, sw.Elapsed);

        var p = previous.Value;
        if (current.Idle < p.Idle || current.Kernel < p.Kernel || current.User < p.User)
            return new ActivitySample(0, at, false, sw.Elapsed);

        var idle = current.Idle - p.Idle;
        var kernel = current.Kernel - p.Kernel;
        var user = current.User - p.User;
        var total = kernel + user;
        if (total == 0 || idle > total)
            return new ActivitySample(0, at, false, sw.Elapsed);

        var busy = total - idle;
        var percent = Math.Clamp(100d * busy / total, 0, 100);
        return new ActivitySample(percent, at, true, sw.Elapsed);
    }

    private sealed class SystemTimesNative : ISystemTimesNative
    {
        public SystemTimesSnapshot Read()
        {
            if (!GetSystemTimes(out var idle, out var kernel, out var user))
                throw new InvalidOperationException($"GetSystemTimes failed: {Marshal.GetLastWin32Error()}");
            return new SystemTimesSnapshot(ToUInt64(idle), ToUInt64(kernel), ToUInt64(user));
        }

        private static ulong ToUInt64(NativeFileTime time) => ((ulong)time.High << 32) | time.Low;

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeFileTime { public uint Low; public uint High; }

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetSystemTimes(out NativeFileTime idleTime, out NativeFileTime kernelTime, out NativeFileTime userTime);
    }
}
