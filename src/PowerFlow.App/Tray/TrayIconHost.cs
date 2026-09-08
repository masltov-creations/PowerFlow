using System.Runtime.InteropServices;
using PowerFlow.App.Controller;
using PowerFlow.Core.Policy;

namespace PowerFlow.App.Tray;

public sealed class TrayIconHost : IDisposable
{
    private const uint WmApp = 0x8000;
    private const uint WmTray = WmApp + 0x31;
    private const uint WmRButtonUp = 0x0205;
    private const uint WmLButtonDblClk = 0x0203;
    private const uint WmContextMenu = 0x007B;
    private const uint WmCommand = 0x0111;
    private const uint NifMessage = 0x00000001;
    private const uint NifIcon = 0x00000002;
    private const uint NifTip = 0x00000004;
    private const uint NimAdd = 0x00000000;
    private const uint NimModify = 0x00000001;
    private const uint NimDelete = 0x00000002;
    private const uint MfString = 0x00000000;
    private const uint MfDisabled = 0x00000002;
    private const uint MfGrayed = 0x00000001;
    private const uint MfChecked = 0x00000008;
    private const uint MfSeparator = 0x00000800;
    private const uint TpmRightButton = 0x0002;
    private const uint TpmReturnCmd = 0x0100;
    private const uint TpmNonotify = 0x0080;
    private static readonly IntPtr HwndMessage = new(-3);

    private readonly string _className = $"PowerFlow.Tray.{Guid.NewGuid():N}";
    private readonly WindowProc _windowProc;
    private readonly IntPtr _instance;
    private readonly uint _taskbarCreatedMessage;
    private IntPtr _window;
    private ushort _classAtom;
    private bool _iconAdded;
    private ControllerSnapshot _snapshot;

    public TrayIconHost(ControllerSnapshot initialSnapshot)
    {
        _snapshot = initialSnapshot;
        _windowProc = WndProc;
        _instance = GetModuleHandle(null);
        _taskbarCreatedMessage = RegisterWindowMessage("TaskbarCreated");
        CreateMessageWindow();
        AddOrUpdateIcon(add: true);
    }

    public event EventHandler<TrayCommandInvokedEventArgs>? CommandInvoked;

    public void Update(ControllerSnapshot snapshot)
    {
        _snapshot = snapshot;
        AddOrUpdateIcon(add: false);
    }

    public void Dispose()
    {
        if (_window != IntPtr.Zero && _iconAdded)
        {
            var data = CreateNotifyData();
            Shell_NotifyIcon(NimDelete, ref data);
            _iconAdded = false;
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

    private void CreateMessageWindow()
    {
        var wc = new WndClassEx
        {
            CbSize = (uint)Marshal.SizeOf<WndClassEx>(),
            HInstance = _instance,
            LpfnWndProc = Marshal.GetFunctionPointerForDelegate(_windowProc),
            LpszClassName = _className
        };
        _classAtom = RegisterClassEx(ref wc);
        if (_classAtom == 0) throw new InvalidOperationException($"RegisterClassEx failed: {Marshal.GetLastWin32Error()}");
        _window = CreateWindowEx(0, _className, "PowerFlow Tray Host", 0, 0, 0, 0, 0, HwndMessage, IntPtr.Zero, _instance, IntPtr.Zero);
        if (_window == IntPtr.Zero) throw new InvalidOperationException($"CreateWindowEx failed: {Marshal.GetLastWin32Error()}");
    }

    private IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == _taskbarCreatedMessage)
        {
            AddOrUpdateIcon(add: true);
            return IntPtr.Zero;
        }

        if (msg == WmCommand)
        {
            RaiseCommand(unchecked((int)(wParam.ToInt64() & 0xffff)));
            return IntPtr.Zero;
        }
        if (msg == WmTray)
        {
            var mouseMessage = unchecked((uint)lParam.ToInt64());
            if (mouseMessage == WmLButtonDblClk)
                RaiseCommand(TrayMenuCommands.OpenDashboard);
            else if (mouseMessage is WmRButtonUp or WmContextMenu)
                ShowContextMenu();
            return IntPtr.Zero;
        }

        return DefWindowProc(hwnd, msg, wParam, lParam);
    }

    private void ShowContextMenu()
    {
        var model = TrayMenuCommands.Build(_snapshot);
        var menu = CreatePopupMenu();
        if (menu == IntPtr.Zero) return;
        try
        {
            AppendMenu(menu, MfString | MfDisabled | MfGrayed, 0, model.StatusText);
            AppendMenu(menu, MfSeparator, 0, null);
            AppendMenu(menu, MfString, TrayMenuCommands.OpenDashboard, "Open PowerFlow");
            AppendMenu(menu, MfSeparator, 0, null);
            AppendMenu(menu, MfString | Check(model.PowerSaverChecked), TrayMenuCommands.PowerSaver, "Power Saver");
            AppendMenu(menu, MfString | Check(model.BalancedChecked), TrayMenuCommands.Balanced, "Balanced");
            AppendMenu(menu, MfString | Check(model.HighPerformanceChecked), TrayMenuCommands.HighPerformance, "High Performance (Lock)");
            var releaseFlags = MfString | (model.ReleaseLatchEnabled ? 0u : MfDisabled | MfGrayed);
            AppendMenu(menu, releaseFlags, TrayMenuCommands.ReleaseLatch, "Release Manual Lock");
            AppendMenu(menu, MfSeparator, 0, null);
            AppendMenu(menu, MfString, TrayMenuCommands.Settings, "Settings");
            AppendMenu(menu, MfSeparator, 0, null);
            AppendMenu(menu, MfString, TrayMenuCommands.Exit, "Exit PowerFlow");

            if (!GetCursorPos(out var point)) return;
            SetForegroundWindow(_window);
            var command = TrackPopupMenu(menu, TpmRightButton | TpmReturnCmd | TpmNonotify, point.X, point.Y, 0, _window, IntPtr.Zero);
            if (command != 0) RaiseCommand(unchecked((int)command));
            PostMessage(_window, 0, IntPtr.Zero, IntPtr.Zero);
        }
        finally
        {
            DestroyMenu(menu);
        }
    }

    private void RaiseCommand(int commandId) => CommandInvoked?.Invoke(this, new TrayCommandInvokedEventArgs(commandId));

    private void AddOrUpdateIcon(bool add)
    {
        if (_window == IntPtr.Zero) return;
        var data = CreateNotifyData();
        if (Shell_NotifyIcon(add || !_iconAdded ? NimAdd : NimModify, ref data))
            _iconAdded = true;
    }

    private NotifyIconData CreateNotifyData()
    {
        var model = TrayMenuCommands.Build(_snapshot);
        return new NotifyIconData
        {
            CbSize = (uint)Marshal.SizeOf<NotifyIconData>(),
            HWnd = _window,
            UId = 1,
            UFlags = NifMessage | NifIcon | NifTip,
            UCallbackMessage = WmTray,
            HIcon = LoadStateIcon(_snapshot),
            SzTip = TrimTooltip($"PowerFlow · {model.StatusText}")
        };
    }

    private static uint Check(bool value) => value ? MfChecked : 0;

    private static string TrimTooltip(string value) => value.Length <= 127 ? value : value[..127];

    private static IntPtr LoadStateIcon(ControllerSnapshot snapshot)
    {
        // Shared system icons: no allocation/destruction overhead. Custom vector icons replace these in the visual-polish task.
        var resourceId = snapshot.IsLatched ? 32518 : snapshot.State switch
        {
            PowerState.PowerSaver => 32516,       // IDI_INFORMATION
            PowerState.Balanced => 32512,         // IDI_APPLICATION
            PowerState.HighPerformance => 32515,  // IDI_WARNING
            _ => 32512
        };
        return LoadIcon(IntPtr.Zero, new IntPtr(resourceId));
    }

    public sealed class TrayCommandInvokedEventArgs(int commandId) : EventArgs
    {
        public int CommandId { get; } = commandId;
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

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public uint CbSize;
        public IntPtr HWnd;
        public uint UId;
        public uint UFlags;
        public uint UCallbackMessage;
        public IntPtr HIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string SzTip;
        public uint DwState;
        public uint DwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string SzInfo;
        public uint UTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string SzInfoTitle;
        public uint DwInfoFlags;
        public Guid GuidItem;
        public IntPtr HBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point { public int X; public int Y; }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? moduleName);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern ushort RegisterClassEx(ref WndClassEx wndClass);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool UnregisterClass(string className, IntPtr instance);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr CreateWindowEx(uint exStyle, string className, string windowName, uint style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DestroyWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern IntPtr DefWindowProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessage(string value);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool Shell_NotifyIcon(uint message, ref NotifyIconData data);
    [DllImport("user32.dll")] private static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool AppendMenu(IntPtr menu, uint flags, int id, string? text);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DestroyMenu(IntPtr menu);
    [DllImport("user32.dll")] private static extern uint TrackPopupMenu(IntPtr menu, uint flags, int x, int y, int reserved, IntPtr hwnd, IntPtr rect);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern IntPtr LoadIcon(IntPtr instance, IntPtr iconName);
}
