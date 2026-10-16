// VoiceDucker — Copyright (c) 2026 Umberto Giacobbi. MIT license.

using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Windowing;
using Windows.Graphics;

namespace VoiceDucker;

internal sealed class WindowPlacementService : IDisposable
{
    private readonly AppWindow _window;
    private readonly IntPtr _handle;
    private readonly Func<bool> _isSettingsPaneOpen;
    private readonly Action<string> _reportError;
    private readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private WindowPlacementState? _state;

    public bool IsTracking { get; private set; }
    public event Action? SizeChanged;

    public WindowPlacementService(Window window, Func<bool> isSettingsPaneOpen, Action<string> reportError)
    {
        _window = window.AppWindow;
        _handle = WinRT.Interop.WindowNative.GetWindowHandle(window);
        _isSettingsPaneOpen = isSettingsPaneOpen;
        _reportError = reportError;
        _saveTimer.Tick += OnSaveTimer;
    }

    public bool TryRestore(out bool settingsPaneOpen)
    {
        settingsPaneOpen = false;
        try
        {
            _state = WindowPlacementStore.Load();
            if (_state is null) return false;

            var monitors = GetMonitors();
            var monitor = monitors.FirstOrDefault(item => !string.IsNullOrEmpty(_state.MonitorId)
                    ? item.Id.Equals(_state.MonitorId, StringComparison.OrdinalIgnoreCase)
                    : item.Name.Equals(_state.MonitorName, StringComparison.OrdinalIgnoreCase))
                ?? monitors.First(item => item.IsPrimary);

            // Move the still-hidden window to its target monitor before asking Windows
            // for its DPI; forcing the old DPI would override the user's display settings.
            _window.MoveAndResize(new RectInt32(monitor.WorkArea.X, monitor.WorkArea.Y,
                Math.Min(_window.Size.Width, monitor.WorkArea.Width),
                Math.Min(_window.Size.Height, monitor.WorkArea.Height)));
            var bounds = _state.RestoreBounds(monitor.WorkArea, GetDpi());
            _window.MoveAndResize(new RectInt32(bounds.X, bounds.Y, bounds.Width, bounds.Height));
            if (_state.Maximized && _window.Presenter is OverlappedPresenter presenter) presenter.Maximize();
            settingsPaneOpen = _state.SettingsPaneOpen;
            return true;
        }
        catch (Exception exception)
        {
            _state = null;
            _reportError($"Window position could not be restored: {exception.Message}");
            return false;
        }
    }

    public void StartTracking()
    {
        IsTracking = true;
        _window.Changed += OnWindowChanged;
        SaveNow();
    }

    public void SaveNow()
    {
        _saveTimer.Stop();
        try
        {
            Capture();
            if (_state is not null) WindowPlacementStore.Save(_state);
        }
        catch (Exception exception)
        {
            _reportError($"Window position could not be saved: {exception.Message}");
        }
    }

    private void OnWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (args.DidSizeChange) SizeChanged?.Invoke();
        if (!args.DidSizeChange && !args.DidPositionChange && !args.DidPresenterChange) return;
        try
        {
            // Capture while the HWND is valid; Closed can arrive after it is destroyed.
            Capture();
            _saveTimer.Stop();
            _saveTimer.Start();
        }
        catch (Exception exception)
        {
            _reportError($"Window position could not be read: {exception.Message}");
        }
    }

    private void Capture()
    {
        if (_window.Presenter is not OverlappedPresenter presenter ||
            presenter.State == OverlappedPresenterState.Minimized) return;

        var monitor = ReadMonitor(MonitorFromWindow(_handle, 2)); // MONITOR_DEFAULTTONEAREST
        var dpi = GetDpi();
        var maximized = presenter.State == OverlappedPresenterState.Maximized;
        var bounds = maximized
            ? _state?.RestoreBounds(monitor.WorkArea, dpi)
            : new WindowBounds(_window.Position.X, _window.Position.Y, _window.Size.Width, _window.Size.Height);
        if (bounds is null) return;
        _state = new WindowPlacementState(bounds, monitor.WorkArea, dpi, monitor.Id, monitor.Name,
            maximized, _isSettingsPaneOpen());
    }

    private uint GetDpi()
    {
        var dpi = GetDpiForWindow(_handle);
        return dpi == 0 ? 96 : dpi;
    }

    private void OnSaveTimer(object? sender, object args)
    {
        _saveTimer.Stop();
        Persist();
    }

    private void Persist()
    {
        try
        {
            if (_state is not null) WindowPlacementStore.Save(_state);
        }
        catch (Exception exception)
        {
            _reportError($"Window position could not be saved: {exception.Message}");
        }
    }

    public void Dispose()
    {
        _saveTimer.Stop();
        _saveTimer.Tick -= OnSaveTimer;
        _window.Changed -= OnWindowChanged;
        IsTracking = false;
        Persist();
    }

    private sealed record MonitorInfo(string Id, string Name, WindowBounds WorkArea, bool IsPrimary);

    private static List<MonitorInfo> GetMonitors()
    {
        var handles = new List<IntPtr>();
        MonitorEnumProc callback = (IntPtr handle, IntPtr dc, ref NativeRect bounds, IntPtr data) =>
        {
            handles.Add(handle);
            return true;
        };
        if (!EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        return handles.Select(ReadMonitor).ToList();
    }

    private static MonitorInfo ReadMonitor(IntPtr handle)
    {
        var info = new NativeMonitorInfo { Size = Marshal.SizeOf<NativeMonitorInfo>() };
        if (!GetMonitorInfo(handle, ref info)) throw new Win32Exception(Marshal.GetLastWin32Error());
        var device = new DisplayDevice { Size = Marshal.SizeOf<DisplayDevice>() };
        // The interface ID identifies the monitor even if Windows renumbers DISPLAY1/2.
        var id = EnumDisplayDevices(info.DeviceName, 0, ref device, 1) ? device.DeviceId : string.Empty;
        var area = info.WorkArea;
        return new MonitorInfo(id, info.DeviceName,
            new WindowBounds(area.Left, area.Top, area.Right - area.Left, area.Bottom - area.Top),
            (info.Flags & 1) != 0);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeMonitorInfo
    {
        public int Size;
        public NativeRect Monitor, WorkArea;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DisplayDevice
    {
        public int Size;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceId;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string RegistryKey;
    }

    private delegate bool MonitorEnumProc(IntPtr monitor, IntPtr dc, ref NativeRect bounds, IntPtr data);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayMonitors(IntPtr dc, IntPtr clip, MonitorEnumProc callback, IntPtr data);

    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref NativeMonitorInfo info);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayDevices(string device, uint index, ref DisplayDevice info, uint flags);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr window);
}
