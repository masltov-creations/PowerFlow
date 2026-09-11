using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace PowerFlow.Windows.Services;

public sealed record RunningServiceInfo(
    string Name,
    string DisplayName,
    uint ProcessId,
    uint StartType,
    string? ImagePath,
    int ServicesInHost)
{
    public bool IsSharedHost => ProcessId != 0 && ServicesInHost > 1;
}

public sealed class WindowsServiceCatalog
{
    private const uint ScManagerEnumerateService = 0x0004;
    private const uint ServiceWin32 = 0x00000030;
    private const uint ServiceStateAll = 0x00000003;
    private const uint ServiceRunning = 0x00000004;
    private const int ScEnumProcessInfo = 0;
    private const int ErrorMoreData = 234;

    public IReadOnlyList<RunningServiceInfo> ListRunning()
    {
        var manager = OpenSCManager(null, null, ScManagerEnumerateService);
        if (manager == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            _ = EnumServicesStatusEx(manager, ScEnumProcessInfo, ServiceWin32, ServiceStateAll, IntPtr.Zero, 0, out var needed, out _, IntPtr.Zero, null);
            var error = Marshal.GetLastWin32Error();
            if (needed == 0 && error != ErrorMoreData) return Array.Empty<RunningServiceInfo>();
            var buffer = Marshal.AllocHGlobal((int)needed);
            try
            {
                if (!EnumServicesStatusEx(manager, ScEnumProcessInfo, ServiceWin32, ServiceStateAll, buffer, needed, out _, out var returned, IntPtr.Zero, null))
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                var size = Marshal.SizeOf<EnumServiceStatusProcess>();
                var raw = new List<(string Name, string DisplayName, uint Pid, uint StartType, string? ImagePath)>((int)returned);
                for (var i = 0; i < returned; i++)
                {
                    var item = Marshal.PtrToStructure<EnumServiceStatusProcess>(IntPtr.Add(buffer, i * size));
                    if (item.Status.CurrentState != ServiceRunning) continue;
                    var name = Marshal.PtrToStringUni(item.ServiceName) ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    var display = Marshal.PtrToStringUni(item.DisplayName) ?? name;
                    var (startType, imagePath) = ReadRegistryMetadata(name);
                    raw.Add((name, display, item.Status.ProcessId, startType, imagePath));
                }
                var hostCounts = raw.Where(x => x.Pid != 0).GroupBy(x => x.Pid).ToDictionary(g => g.Key, g => g.Count());
                return raw.Select(x => new RunningServiceInfo(x.Name, x.DisplayName, x.Pid, x.StartType, x.ImagePath,
                        x.Pid != 0 && hostCounts.TryGetValue(x.Pid, out var count) ? count : 0))
                    .OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        finally { CloseServiceHandle(manager); }
    }

    private static (uint StartType, string? ImagePath) ReadRegistryMetadata(string serviceName)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{serviceName}");
            var start = key?.GetValue("Start") is int value ? unchecked((uint)value) : 3u;
            var image = key?.GetValue("ImagePath") as string;
            return (start, image);
        }
        catch { return (3u, null); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ServiceStatusProcess
    {
        public uint ServiceType;
        public uint CurrentState;
        public uint ControlsAccepted;
        public uint Win32ExitCode;
        public uint ServiceSpecificExitCode;
        public uint CheckPoint;
        public uint WaitHint;
        public uint ProcessId;
        public uint ServiceFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct EnumServiceStatusProcess
    {
        public IntPtr ServiceName;
        public IntPtr DisplayName;
        public ServiceStatusProcess Status;
    }

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr OpenSCManager(string? machineName, string? databaseName, uint desiredAccess);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumServicesStatusEx(IntPtr manager, int infoLevel, uint serviceType, uint serviceState,
        IntPtr services, uint bufferSize, out uint bytesNeeded, out uint servicesReturned, IntPtr resumeHandle, string? groupName);

    [DllImport("advapi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseServiceHandle(IntPtr handle);
}