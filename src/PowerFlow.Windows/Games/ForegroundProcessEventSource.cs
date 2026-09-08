using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace PowerFlow.Windows.Games;

/// <summary>
/// Emits process candidates only when the foreground window changes. There is no periodic process scan.
/// A one-time SnapshotExisting call is available for startup recovery.
/// </summary>
public sealed class ForegroundProcessEventSource : IProcessEventSource
{
    private readonly IForegroundEventHook _hook;
    private readonly IWindowProcessResolver _resolver;

    public ForegroundProcessEventSource() : this(new WinEventForegroundHook(), new WindowProcessResolver()) { }

    public ForegroundProcessEventSource(IForegroundEventHook hook, IWindowProcessResolver resolver)
    {
        _hook = hook;
        _resolver = resolver;
        _hook.ForegroundChanged += OnForegroundChanged;
    }

    public event EventHandler<ProcessStartEvent>? ProcessStarted;
    public bool IsRunning => _hook.IsRunning;
    public IReadOnlyList<ProcessStartEvent> SnapshotExisting() => _resolver.SnapshotExisting();
    public void Start() => _hook.Start();
    public void Stop() => _hook.Stop();

    private void OnForegroundChanged(object? sender, IntPtr hwnd)
    {
        var candidate = _resolver.Resolve(hwnd);
        if (candidate is not null) ProcessStarted?.Invoke(this, candidate);
    }

    public void Dispose()
    {
        _hook.ForegroundChanged -= OnForegroundChanged;
        _hook.Dispose();
    }
}

public interface IForegroundEventHook : IDisposable
{
    event EventHandler<IntPtr>? ForegroundChanged;
    bool IsRunning { get; }
    void Start();
    void Stop();
}

public interface IWindowProcessResolver
{
    ProcessStartEvent? Resolve(IntPtr hwnd);
    IReadOnlyList<ProcessStartEvent> SnapshotExisting();
}

internal sealed class WinEventForegroundHook : IForegroundEventHook
{
    private const uint EventSystemForeground = 0x0003;
    private const uint WineventOutOfContext = 0x0000;
    private const uint WineventSkipOwnProcess = 0x0002;
    private readonly WinEventDelegate _callback;
    private IntPtr _hook;

    public WinEventForegroundHook() => _callback = OnWinEvent;
    public event EventHandler<IntPtr>? ForegroundChanged;
    public bool IsRunning => _hook != IntPtr.Zero;

    public void Start()
    {
        if (IsRunning) return;
        _hook = SetWinEventHook(EventSystemForeground, EventSystemForeground, IntPtr.Zero, _callback, 0, 0, WineventOutOfContext | WineventSkipOwnProcess);
        if (_hook == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to register foreground event hook.");
    }

    public void Stop()
    {
        var hook = Interlocked.Exchange(ref _hook, IntPtr.Zero);
        if (hook != IntPtr.Zero) UnhookWinEvent(hook);
    }

    private void OnWinEvent(IntPtr hook, uint eventType, IntPtr hwnd, int objectId, int childId, uint eventThread, uint eventTime)
    {
        if (hwnd != IntPtr.Zero) ForegroundChanged?.Invoke(this, hwnd);
    }

    public void Dispose() => Stop();

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void WinEventDelegate(IntPtr hook, uint eventType, IntPtr hwnd, int objectId, int childId, uint eventThread, uint eventTime);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr eventHookDll, WinEventDelegate callback, uint processId, uint threadId, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWinEvent(IntPtr hook);
}

internal sealed class WindowProcessResolver : IWindowProcessResolver
{
    public ProcessStartEvent? Resolve(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return null;
        GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == 0 || pid > int.MaxValue) return null;
        var parent = TryGetParentProcessId((int)pid);
        return ReadProcess((int)pid, parent);
    }

    public IReadOnlyList<ProcessStartEvent> SnapshotExisting()
    {
        var parentMap = ReadParentMap();
        var result = new List<ProcessStartEvent>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                var parent = parentMap.TryGetValue(process.Id, out var value) ? value : null;
                var item = ReadProcess(process, parent);
                if (item is not null) result.Add(item);
            }
        }
        return result;
    }

    private static ProcessStartEvent? ReadProcess(int pid, int? parent)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return ReadProcess(process, parent);
        }
        catch { return null; }
    }

    private static ProcessStartEvent? ReadProcess(Process process, int? parent)
    {
        try
        {
            var path = process.MainModule?.FileName ?? process.ProcessName;
            var started = new DateTimeOffset(process.StartTime.ToUniversalTime(), TimeSpan.Zero);
            return new ProcessStartEvent(process.Id, parent, path, started);
        }
        catch { return null; }
    }

    private static int? TryGetParentProcessId(int processId)
    {
        var map = ReadParentMap(processId);
        return map.TryGetValue(processId, out var parent) ? parent : null;
    }

    private static Dictionary<int, int?> ReadParentMap(int? stopAfterProcessId = null)
    {
        var result = new Dictionary<int, int?>();
        using var snapshot = CreateToolhelp32Snapshot(Th32csSnapProcess, 0);
        if (snapshot.IsInvalid) return result;
        var entry = new ProcessEntry32 { DwSize = (uint)Marshal.SizeOf<ProcessEntry32>() };
        if (!Process32First(snapshot, ref entry)) return result;
        do
        {
            if (entry.Th32ProcessId <= int.MaxValue)
            {
                var pid = (int)entry.Th32ProcessId;
                int? parent = entry.Th32ParentProcessId is > 0 and <= int.MaxValue ? (int)entry.Th32ParentProcessId : null;
                result[pid] = parent;
                if (stopAfterProcessId == pid) break;
            }
            entry.DwSize = (uint)Marshal.SizeOf<ProcessEntry32>();
        } while (Process32Next(snapshot, ref entry));
        return result;
    }

    private const uint Th32csSnapProcess = 0x00000002;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry32
    {
        public uint DwSize;
        public uint CntUsage;
        public uint Th32ProcessId;
        public IntPtr Th32DefaultHeapId;
        public uint Th32ModuleId;
        public uint CntThreads;
        public uint Th32ParentProcessId;
        public int PcPriClassBase;
        public uint DwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string SzExeFile;
    }

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeFileHandle CreateToolhelp32Snapshot(uint flags, uint processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32First(SafeFileHandle snapshot, ref ProcessEntry32 entry);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32Next(SafeFileHandle snapshot, ref ProcessEntry32 entry);
}
