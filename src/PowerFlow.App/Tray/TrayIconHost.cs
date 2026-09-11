using System.Runtime.InteropServices;
using PowerFlow.App.Controller;
using PowerFlow.Core.Policy;

namespace PowerFlow.App.Tray;

public sealed class TrayIconHost : IDisposable
{
    private const uint WmApp = 0x8000;
    private const uint WmTray = WmApp + 0x31;
    private const uint WmMouseMove = 0x0200;
    private const uint WmRButtonUp = 0x0205;
    private const uint WmLButtonUp = 0x0202;
    private const uint WmLButtonDblClk = 0x0203;
    private const uint WmContextMenu = 0x007B;
    private const uint WmCommand = 0x0111;
    private const uint NifMessage = 0x00000001;
    private const uint NifIcon = 0x00000002;
    private const uint NifTip = 0x00000004;
    private const uint NimAdd = 0x00000000;
    private const uint NimModify = 0x00000001;
    private const uint NimDelete = 0x00000002;
    private const uint NimSetVersion = 0x00000004;
    private const uint NotifyIconVersion4 = 4;
    private const uint NinPopupOpen = 0x0406;
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
    private PowerFlowOperatingMode? _manualMode;
    private TrayRect? _observedHoverRect;

    public TrayIconHost(ControllerSnapshot initialSnapshot, PowerFlowOperatingMode? manualMode = null)
    {
        _snapshot = initialSnapshot;
        _manualMode = manualMode;
        _windowProc = WndProc;
        _instance = GetModuleHandle(null);
        _taskbarCreatedMessage = RegisterWindowMessage("TaskbarCreated");
        CreateMessageWindow();
        AddOrUpdateIcon(add: true);
    }

    public event EventHandler<TrayCommandInvokedEventArgs>? CommandInvoked;
    public event EventHandler<TrayInteractionRequestedEventArgs>? InteractionRequested;
    public event EventHandler? HoverActivity;

    public void Update(ControllerSnapshot snapshot, PowerFlowOperatingMode? manualMode = null)
    {
        _snapshot = snapshot;
        _manualMode = manualMode;
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
            var mouseMessage = unchecked((uint)(lParam.ToInt64() & 0xffff));
            var interaction = TrayInteractionIntent.Project(mouseMessage);
            if (interaction == TrayInteractionKind.Hover)
            {
                if (GetCursorPos(out var hoverPoint))
                    _observedHoverRect = TrayHoverAnchorProjection.AroundPoint(hoverPoint.X, hoverPoint.Y, 40);
                HoverActivity?.Invoke(this, EventArgs.Empty);
                InteractionRequested?.Invoke(this, new TrayInteractionRequestedEventArgs(TrayInteractionKind.Hover));
            }
            else if (interaction == TrayInteractionKind.SingleClick && mouseMessage == WmLButtonUp)
            {
                InteractionRequested?.Invoke(this, new TrayInteractionRequestedEventArgs(TrayInteractionKind.SingleClick));
            }
            else if (interaction == TrayInteractionKind.DoubleClick && mouseMessage == WmLButtonDblClk)
            {
                InteractionRequested?.Invoke(this, new TrayInteractionRequestedEventArgs(TrayInteractionKind.DoubleClick));
                RaiseCommand(TrayMenuCommands.OpenDashboard);
            }
            else if (mouseMessage is WmRButtonUp or WmContextMenu)
            {
                ShowContextMenu();
            }
            return IntPtr.Zero;
        }

        return DefWindowProc(hwnd, msg, wParam, lParam);
    }


    public bool IsPointerOverIcon() => TryGetHoverAnchorRect(out _);

    public bool TryGetHoverAnchorRect(out TrayRect rect)
    {
        if (!GetCursorPos(out var point)) { rect = default; return false; }
        TrayRect? shellRect = TryGetIconRect(out var shell) ? shell : null;
        var resolved = TrayHoverAnchorProjection.Resolve(shellRect, _observedHoverRect, point.X, point.Y);
        if (resolved is null) { rect = default; return false; }
        rect = resolved.Value;
        return true;
    }

    public bool TryGetIconRect(out TrayRect rect)
    {
        var id = new NotifyIconIdentifier
        {
            CbSize = (uint)Marshal.SizeOf<NotifyIconIdentifier>(),
            HWnd = _window,
            UId = 1,
            GuidItem = Guid.Empty
        };
        if (Shell_NotifyIconGetRect(ref id, out var native) != 0)
        {
            rect = default;
            return false;
        }
        rect = new TrayRect(native.Left, native.Top, native.Right, native.Bottom);
        return true;
    }

    public bool TryGetWorkArea(out TrayRect workArea)
    {
        if (!TryGetHoverAnchorRect(out var icon)) { workArea = default; return false; }
        return TryGetWorkArea(icon, out workArea);
    }

    public bool TryGetWorkArea(TrayRect icon, out TrayRect workArea)
    {
        var point = new Point { X = icon.Left + icon.Width / 2, Y = icon.Top + icon.Height / 2 };
        var monitor = MonitorFromPoint(point, 2);
        if (monitor == IntPtr.Zero) { workArea = default; return false; }
        var info = new MonitorInfo { CbSize = (uint)Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info)) { workArea = default; return false; }
        workArea = new TrayRect(info.Work.Left, info.Work.Top, info.Work.Right, info.Work.Bottom);
        return true;
    }
    private void ShowContextMenu()
    {
        var model = TrayMenuCommands.Build(_snapshot, _manualMode);
        var menu = CreatePopupMenu();
        if (menu == IntPtr.Zero) return;
        try
        {
            AppendMenu(menu, MfString | MfDisabled | MfGrayed, 0, model.StatusText);
            AppendMenu(menu, MfSeparator, 0, null);
            AppendMenu(menu, MfString, TrayMenuCommands.OpenDashboard, "Open PowerFlow");
            AppendMenu(menu, MfSeparator, 0, null);
            AppendMenu(menu, MfString | Check(model.AutoChecked), TrayMenuCommands.Auto, "Auto");
            AppendMenu(menu, MfString | Check(model.SaverChecked), TrayMenuCommands.PowerSaver, "Saver");
            AppendMenu(menu, MfString | Check(model.BalancedChecked), TrayMenuCommands.Balanced, "Balanced Efficient");
            AppendMenu(menu, MfString | Check(model.BalancedPerformanceChecked), TrayMenuCommands.BalancedPerformance, "Balanced Performance");
            AppendMenu(menu, MfString | Check(model.PerformanceChecked), TrayMenuCommands.HighPerformance, "Performance");
            AppendMenu(menu, MfString | Check(model.UltraChecked), TrayMenuCommands.Ultra, "Ultra");
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
        var operation = add || !_iconAdded ? NimAdd : NimModify;
        if (Shell_NotifyIcon(operation, ref data))
        {
            _iconAdded = true;
            if (operation == NimAdd)
            {
                var versionData = CreateNotifyData();
                versionData.UTimeoutOrVersion = NotifyIconVersion4;
                Shell_NotifyIcon(NimSetVersion, ref versionData);
            }
        }
    }

    private NotifyIconData CreateNotifyData()
    {
        var model = TrayMenuCommands.Build(_snapshot, _manualMode);
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
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "PowerFlow.ico");
        if (File.Exists(iconPath))
        {
            var custom = LoadImage(IntPtr.Zero, iconPath, 1, 0, 0, 0x00000010 | 0x00000040 | 0x00008000);
            if (custom != IntPtr.Zero) return custom;
        }
        var resourceId = snapshot.IsLatched ? 32518 : snapshot.State switch
        {
            PowerState.PowerSaver => 32516,
            PowerState.Balanced => 32512,
            PowerState.HighPerformance => 32515,
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
    [DllImport("shell32.dll")] private static extern int Shell_NotifyIconGetRect(ref NotifyIconIdentifier identifier, out NativeRect iconLocation);
    [DllImport("user32.dll")] private static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool AppendMenu(IntPtr menu, uint flags, int id, string? text);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DestroyMenu(IntPtr menu);
    [DllImport("user32.dll")] private static extern uint TrackPopupMenu(IntPtr menu, uint flags, int x, int y, int reserved, IntPtr hwnd, IntPtr rect);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(Point point, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern IntPtr LoadIcon(IntPtr instance, IntPtr iconName);
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left; public int Top; public int Right; public int Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct NotifyIconIdentifier
    {
        public uint CbSize;
        public IntPtr HWnd;
        public uint UId;
        public Guid GuidItem;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public uint CbSize;
        public NativeRect Monitor;
        public NativeRect Work;
        public uint Flags;
    }
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr LoadImage(IntPtr instance, string name, uint type, int cx, int cy, uint loadFlags);
}
