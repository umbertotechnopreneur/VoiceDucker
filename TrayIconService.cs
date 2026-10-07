using System.ComponentModel;
using System.Runtime.InteropServices;

namespace VoiceDucker;

internal sealed class TrayIconService : IDisposable
{
    private const uint AddIcon = 0;
    private const uint ModifyIcon = 1;
    private const uint DeleteIcon = 2;
    private const uint SetIconVersion = 4;
    private const uint IconMessage = 1;
    private const uint IconImage = 2;
    private const uint IconTip = 4;
    private const uint IconShowTip = 0x80;
    private const uint IconVersion4 = 4;
    private const uint CallbackMessage = 0x8000 + 0x350;
    private const uint IconSelectMessage = 0x0400;
    private const uint IconKeySelectMessage = 0x0401;
    private const uint ContextMenuMessage = 0x007B;
    private const uint LoadFromFile = 0x0010;
    private const uint ImageIcon = 1;
    private const uint MenuString = 0;
    private const uint MenuReturnCommand = 0x0100;
    private const uint MenuNoNotify = 0x0080;
    private const uint MenuRightButton = 0x0002;
    private const uint MenuWorkArea = 0x10000;
    private static readonly UIntPtr SubclassId = new(1);
    private static readonly SubclassProcDelegate SubclassProcedure = WindowSubclassProcedure;

    private readonly IntPtr _windowHandle;
    private readonly Func<bool> _isDuckingEnabled;
    private readonly IntPtr _iconHandle;
    private readonly uint _taskbarCreatedMessage;
    private GCHandle _selfHandle;
    private string _toolTip = "VoiceDucker";
    private bool _iconRegistered;
    private bool _disposed;

    public event Action? ToggleRequested;
    public event Action? AboutRequested;
    public event Action? ExitRequested;
    public event Action<Exception>? Error;

    public TrayIconService(IntPtr windowHandle, string iconPath, Func<bool> isDuckingEnabled)
    {
        if (windowHandle == IntPtr.Zero)
        {
            throw new ArgumentException("A window handle is required.", nameof(windowHandle));
        }
        if (!File.Exists(iconPath))
        {
            throw new FileNotFoundException("The tray icon is missing.", iconPath);
        }

        _windowHandle = windowHandle;
        _isDuckingEnabled = isDuckingEnabled;
        _taskbarCreatedMessage = RegisterWindowMessage("TaskbarCreated");
        if (_taskbarCreatedMessage == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Taskbar registration failed.");
        }

        _iconHandle = LoadImage(IntPtr.Zero, iconPath, ImageIcon, 0, 0, LoadFromFile);
        if (_iconHandle == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "The tray icon could not be loaded.");
        }

        _selfHandle = GCHandle.Alloc(this);
        if (!SetWindowSubclass(_windowHandle, SubclassProcedure, SubclassId,
            GCHandle.ToIntPtr(_selfHandle)))
        {
            Dispose();
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Tray callbacks could not be installed.");
        }

        try
        {
            EnsureIconRegistered();
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public void HideWindow()
    {
        EnsureIconRegistered();
        _ = ShowWindow(_windowHandle, 0);
        if (IsWindowVisible(_windowHandle))
        {
            throw new Win32Exception("The main window could not be hidden.");
        }
    }

    public void ShowWindow()
    {
        _ = ShowWindow(_windowHandle, 1);
        _ = SetForegroundWindow(_windowHandle);
    }

    public void SetStatus(string status)
    {
        var next = $"VoiceDucker - {status}";
        if (_disposed || next == _toolTip)
        {
            return;
        }
        _toolTip = next;
        EnsureIconRegistered();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;

        if (_iconRegistered)
        {
            var data = CreateIconData();
            _ = ShellNotifyIcon(DeleteIcon, ref data);
            _iconRegistered = false;
        }
        _ = RemoveWindowSubclass(_windowHandle, SubclassProcedure, SubclassId);
        if (_selfHandle.IsAllocated)
        {
            _selfHandle.Free();
        }
        if (_iconHandle != IntPtr.Zero)
        {
            _ = DestroyIcon(_iconHandle);
        }
    }

    private void EnsureIconRegistered()
    {
        var data = CreateIconData();
        if (_iconRegistered && ShellNotifyIcon(ModifyIcon, ref data))
        {
            return;
        }
        _iconRegistered = false;
        if (!ShellNotifyIcon(AddIcon, ref data))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "The tray icon could not be added.");
        }
        _iconRegistered = true;
        if (!ShellNotifyIcon(SetIconVersion, ref data))
        {
            var error = Marshal.GetLastWin32Error();
            _ = ShellNotifyIcon(DeleteIcon, ref data);
            _iconRegistered = false;
            throw new Win32Exception(error, "The tray icon could not be configured.");
        }
    }

    private void ShowContextMenu()
    {
        var menu = CreatePopupMenu();
        if (menu == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "The tray menu could not be created.");
        }

        try
        {
            var windowLabel = IsWindowVisible(_windowHandle) ? "Minimize to tray" : "Show window";
            var toggleLabel = _isDuckingEnabled() ? "Disable microphone ducking" : "Enable microphone ducking";
            AddMenuItem(menu, 1, windowLabel);
            AddMenuItem(menu, 2, toggleLabel);
            AddMenuItem(menu, 3, "About VoiceDucker");
            AddMenuItem(menu, 4, "VoiceDucker on GitHub");
            AddMenuItem(menu, 5, "VibeWare on GitHub");
            AddMenuItem(menu, 6, "Close app");

            if (!GetCursorPos(out var point))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "The tray menu position is unavailable.");
            }
            _ = SetForegroundWindow(_windowHandle);
            var selection = TrackPopupMenuEx(menu,
                MenuReturnCommand | MenuNoNotify | MenuRightButton | MenuWorkArea,
                point.X, point.Y, _windowHandle, IntPtr.Zero);
            switch (selection)
            {
                case 1:
                    if (IsWindowVisible(_windowHandle)) HideWindow(); else ShowWindow();
                    break;
                case 2: ToggleRequested?.Invoke(); break;
                case 3: AboutRequested?.Invoke(); break;
                case 4: OpenLink("https://github.com/umbertotechnopreneur/VoiceDucker"); break;
                case 5: OpenLink("https://github.com/umbertotechnopreneur/VibeWare"); break;
                case 6: ExitRequested?.Invoke(); break;
            }
        }
        finally
        {
            _ = DestroyMenu(menu);
            _ = PostMessage(_windowHandle, 0, IntPtr.Zero, IntPtr.Zero);
        }
    }

    private static void AddMenuItem(IntPtr menu, uint id, string label)
    {
        if (!AppendMenu(menu, MenuString, new UIntPtr(id), label))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "The tray menu could not be populated.");
        }
    }

    private static void OpenLink(string url)
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url)
        {
            UseShellExecute = true
        });
    }

    private IconData CreateIconData() => new()
    {
        Size = Marshal.SizeOf<IconData>(),
        WindowHandle = _windowHandle,
        Id = 1,
        Flags = IconMessage | IconImage | IconTip | IconShowTip,
        CallbackMessage = CallbackMessage,
        IconHandle = _iconHandle,
        ToolTip = _toolTip,
        Info = string.Empty,
        InfoTitle = string.Empty,
        Version = IconVersion4
    };

    private static IntPtr WindowSubclassProcedure(IntPtr windowHandle, uint message,
        UIntPtr wParam, IntPtr lParam, UIntPtr subclassId, IntPtr referenceData)
    {
        if (referenceData != IntPtr.Zero &&
            GCHandle.FromIntPtr(referenceData).Target is TrayIconService service &&
            !service._disposed)
        {
            try
            {
                if (message == service._taskbarCreatedMessage)
                {
                    service.EnsureIconRegistered();
                }
                else if (message == CallbackMessage)
                {
                    var activation = (uint)lParam.ToInt64() & 0xFFFF;
                    if (activation is IconSelectMessage or IconKeySelectMessage)
                    {
                        service.ShowWindow();
                    }
                    else if (activation == ContextMenuMessage)
                    {
                        service.ShowContextMenu();
                    }
                }
            }
            catch (Exception exception)
            {
                service.ShowWindow();
                service.Error?.Invoke(exception);
            }
        }
        return DefSubclassProc(windowHandle, message, wParam, lParam);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct IconData
    {
        public int Size;
        public IntPtr WindowHandle;
        public uint Id;
        public uint Flags;
        public uint CallbackMessage;
        public IntPtr IconHandle;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string ToolTip;
        public uint State;
        public uint StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
        public uint TimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string InfoTitle;
        public uint InfoFlags;
        public Guid GuidItem;
        public IntPtr BalloonIconHandle;
        public uint Version { readonly get => TimeoutOrVersion; set => TimeoutOrVersion = value; }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point { public int X; public int Y; }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate IntPtr SubclassProcDelegate(IntPtr windowHandle, uint message,
        UIntPtr wParam, IntPtr lParam, UIntPtr subclassId, IntPtr referenceData);

    [DllImport("user32.dll", EntryPoint = "RegisterWindowMessageW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint RegisterWindowMessage(string message);
    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShellNotifyIcon(uint message, ref IconData data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadImage(IntPtr instance, string path, uint type, int width, int height, uint flags);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr icon);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr windowHandle);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr windowHandle, int command);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr windowHandle);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll", EntryPoint = "AppendMenuW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AppendMenu(IntPtr menu, uint flags, UIntPtr id, string label);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint TrackPopupMenuEx(IntPtr menu, uint flags, int x, int y,
        IntPtr ownerWindow, IntPtr parameters);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyMenu(IntPtr menu);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(IntPtr windowHandle, uint message,
        IntPtr wParam, IntPtr lParam);
    [DllImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowSubclass(IntPtr windowHandle, SubclassProcDelegate procedure,
        UIntPtr subclassId, IntPtr referenceData);
    [DllImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RemoveWindowSubclass(IntPtr windowHandle,
        SubclassProcDelegate procedure, UIntPtr subclassId);
    [DllImport("comctl32.dll")]
    private static extern IntPtr DefSubclassProc(IntPtr windowHandle, uint message,
        UIntPtr wParam, IntPtr lParam);
}
