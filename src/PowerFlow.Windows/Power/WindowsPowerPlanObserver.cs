using System.ComponentModel;
using System.Runtime.InteropServices;

namespace PowerFlow.Windows.Power;

public sealed class WindowsPowerPlanObserver : IActivePowerPlanObserver
{
    private const uint WmPowerBroadcast = 0x0218;
    private const uint PbtPowerSettingChange = 0x8013;
    private const uint DeviceNotifyWindowHandle = 0;
    private static readonly IntPtr HwndMessage = new(-3);
    private static readonly Guid PowerSchemePersonality = new("245d8541-3943-4422-b025-13a784f679b7");

    private readonly string _className = $"PowerFlow.PowerPlanObserver.{Guid.NewGuid():N}";
    private readonly WindowProc _windowProc;
    private readonly IntPtr _instance;
    private IntPtr _window;
    private IntPtr _notification;
    private ushort _classAtom;

    public WindowsPowerPlanObserver()
    {
        _windowProc = WndProc;
        _instance = GetModuleHandle(null);
    }

    public event EventHandler<ActivePowerPlanChangedEventArgs>? ActivePlanChanged;

    public void Start()
    {
        if (_window != IntPtr.Zero) return;
        var wc = new WndClassEx
        {
            CbSize = (uint)Marshal.SizeOf<WndClassEx>(),
            HInstance = _instance,
            LpfnWndProc = Marshal.GetFunctionPointerForDelegate(_windowProc),
            LpszClassName = _className
        };
        _classAtom = RegisterClassEx(ref wc);
        if (_classAtom == 0) throw new InvalidOperationException($"RegisterClassEx failed: {Marshal.GetLastWin32Error()}");
        _window = CreateWindowEx(0, _className, "PowerFlow Power Plan Observer", 0, 0, 0, 0, 0, HwndMessage, IntPtr.Zero, _instance, IntPtr.Zero);
        if (_window == IntPtr.Zero)
        {
            var error = Marshal.GetLastWin32Error();
            Stop();
            throw new InvalidOperationException($"CreateWindowEx failed: {error}");
        }
        var setting = PowerSchemePersonality;
        _notification = RegisterPowerSettingNotification(_window, ref setting, DeviceNotifyWindowHandle);
        if (_notification == IntPtr.Zero)
        {
            var error = Marshal.GetLastWin32Error();
            Stop();
            throw new InvalidOperationException($"RegisterPowerSettingNotification failed: {error}");
        }
    }

    public void Stop()
    {
        if (_notification != IntPtr.Zero)
        {
            UnregisterPowerSettingNotification(_notification);
            _notification = IntPtr.Zero;
        }
        if (_window != IntPtr.Zero)
        {
            DestroyWindow(_window);
            _window = IntPtr.Zero;
        }
        if (_classAtom != 0)
        {
            UnregisterClass(_className, _instance);
            _classAtom = 0;
        }
        GC.KeepAlive(_windowProc);
    }

    public void Dispose() => Stop();

    private IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WmPowerBroadcast && unchecked((uint)wParam.ToInt64()) == PbtPowerSettingChange)
        {
            try { ActivePlanChanged?.Invoke(this, new ActivePowerPlanChangedEventArgs(ReadActiveScheme())); }
            catch { }
            return IntPtr.Zero;
        }
        return DefWindowProc(hwnd, msg, wParam, lParam);
    }

    private static Guid ReadActiveScheme()
    {
        var result = PowerGetActiveScheme(IntPtr.Zero, out var ptr);
        if (result != 0) throw new Win32Exception((int)result);
        try { return Marshal.PtrToStructure<Guid>(ptr); }
        finally { LocalFree(ptr); }
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate IntPtr WindowProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WndClassEx
    {
        public uint CbSize;
        public uint Style;
        public IntPtr LpfnWndProc;
        public int CbClsExtra;
        public int CbWndExtra;
        public IntPtr HInstance;
        public IntPtr HIcon;
        public IntPtr HCursor;
        public IntPtr HbrBackground;
        [MarshalAs(UnmanagedType.LPWStr)] public string? LpszMenuName;
        [MarshalAs(UnmanagedType.LPWStr)] public string? LpszClassName;
        public IntPtr HIconSm;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? moduleName);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassEx(ref WndClassEx windowClass);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(uint exStyle, string className, string windowName, uint style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);
    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DestroyWindow(IntPtr hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool UnregisterClass(string className, IntPtr instance);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr RegisterPowerSettingNotification(IntPtr recipient, ref Guid powerSettingGuid, uint flags);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool UnregisterPowerSettingNotification(IntPtr handle);
    [DllImport("powrprof.dll")]
    private static extern uint PowerGetActiveScheme(IntPtr userRootPowerKey, out IntPtr activePolicyGuid);
    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr hMem);
}
