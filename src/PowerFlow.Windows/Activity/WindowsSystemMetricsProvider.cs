using System.Runtime.InteropServices;

namespace PowerFlow.Windows.Activity;

public sealed class WindowsSystemMetricsProvider : ISystemMetricsProvider
{
    public SystemMetricsSnapshot Read()
    {
        double? used = null;
        try
        {
            var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
            if (GlobalMemoryStatusEx(ref status) && status.TotalPhysical > 0)
                used = CalculateUsedPercent(status.TotalPhysical, status.AvailablePhysical);
        }
        catch
        {
            used = null;
        }

        string? machine = null;
        try { machine = Environment.MachineName; } catch { }
        return new SystemMetricsSnapshot(used, machine);
    }

    public static double CalculateUsedPercent(ulong total, ulong available)
        => total == 0 ? 0 : Math.Clamp((total - Math.Min(total, available)) * 100d / total, 0, 100);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);
}
