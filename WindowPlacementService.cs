// VoiceDucker — Copyright (c) 2026 Umberto Giacobbi. MIT license.

using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Windowing;
using Windows.Graphics;

namespace VoiceDucker;

/// <summary>
/// Restores layout against the current desktop and debounces subsequent layout saves.
/// Monitor and DPI fallbacks handle hardware changes, not older application versions.
/// </summary>
internal sealed class WindowPlacementService : IDisposable
{
    #region Private fields

    private readonly AppWindow _window;
    private readonly IntPtr _handle;
    private readonly Func<bool> _isSettingsPaneOpen;
    private readonly Action<string> _reportError;
    private readonly Func<WindowPlacementState?> _load;
    private readonly Action<WindowPlacementState> _save;

    private readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };

    private WindowPlacementState? _state;

    #endregion

    #region Public state and events

    public bool IsTracking { get; private set; }
    public event Action? SizeChanged;

    #endregion

    #region Initialization

    /// <summary>
    /// Connects window geometry and persistence adapters without starting change tracking.
    /// </summary>
    public WindowPlacementService(Window window, Func<bool> isSettingsPaneOpen, Action<string> reportError,
        Func<WindowPlacementState?> load, Action<WindowPlacementState> save)
    {
        _window = window.AppWindow;
        _handle = WinRT.Interop.WindowNative.GetWindowHandle(window);
        _isSettingsPaneOpen = isSettingsPaneOpen;
        _reportError = reportError;
        _load = load;
        _save = save;
        _saveTimer.Tick += OnSaveTimer;
    }

    #endregion

    #region Public methods

    /// <summary>
    /// Selects the saved display when available and restores bounds using that display’s current DPI.
    /// </summary>
    public bool TryRestore(out bool settingsPaneOpen)
    {
        settingsPaneOpen = false;
        try
        {
            _state = _load();
            if (_state is null)
            {
                return false;
            }

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
            if (_state.Maximized && _window.Presenter is OverlappedPresenter presenter)
            {
                presenter.Maximize();
            }
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

    /// <summary>
    /// Subscribes to geometry changes and captures the initial placement.
    /// </summary>
    public void StartTracking()
    {
        IsTracking = true;
        _window.Changed += OnWindowChanged;
        SaveNow();
    }

    /// <summary>
    /// Cancels the debounce delay and immediately captures and saves usable bounds.
    /// </summary>
    public void SaveNow()
    {
        _saveTimer.Stop();
        try
        {
            Capture();
            if (_state is not null)
            {
                _save(_state);
            }
        }
        catch (Exception exception)
        {
            _reportError($"Window position could not be saved: {exception.Message}");
        }
    }

    #endregion

    #region Implementation

    /// <summary>
    /// Captures geometry while the native handle is valid and restarts the delayed save.
    /// </summary>
    private void OnWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (args.DidSizeChange)
        {
            SizeChanged?.Invoke();
        }
        if (!args.DidSizeChange && !args.DidPositionChange && !args.DidPresenterChange)
        {
            return;
        }
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

    /// <summary>
    /// Captures normal bounds while the HWND is valid. Minimized bounds are never saved.
    /// A maximized window retains the normal rectangle needed for a later Restore action.
    /// </summary>
    private void Capture()
    {
        if (_window.Presenter is not OverlappedPresenter presenter ||
            presenter.State == OverlappedPresenterState.Minimized)
        {
            return;
        }

        var monitor = ReadMonitor(MonitorFromWindow(_handle, 2)); // MONITOR_DEFAULTTONEAREST
        var dpi = GetDpi();
        var maximized = presenter.State == OverlappedPresenterState.Maximized;
        var bounds = maximized
            ? _state?.RestoreBounds(monitor.WorkArea, dpi)
            : new WindowBounds(_window.Position.X, _window.Position.Y, _window.Size.Width, _window.Size.Height);
        if (bounds is null)
        {
            return;
        }
        _state = new WindowPlacementState(bounds, monitor.WorkArea, dpi, monitor.Id, monitor.Name,
            maximized, _isSettingsPaneOpen());
    }

    /// <summary>
    /// Reads the current window DPI, falling back to standard Windows scaling if unavailable.
    /// </summary>
    private uint GetDpi()
    {
        var dpi = GetDpiForWindow(_handle);
        return dpi == 0 ? 96 : dpi;
    }

    /// <summary>
    /// Ends the debounce interval and persists the latest captured placement.
    /// </summary>
    private void OnSaveTimer(object? sender, object args)
    {
        _saveTimer.Stop();
        Persist();
    }

    /// <summary>
    /// Saves the latest valid placement and sends storage failures to the application model.
    /// </summary>
    private void Persist()
    {
        try
        {
            if (_state is not null)
            {
                _save(_state);
            }
        }
        catch (Exception exception)
        {
            _reportError($"Window position could not be saved: {exception.Message}");
        }
    }

    #endregion

    #region Public methods

    /// <summary>
    /// Stops delayed saves, detaches geometry handlers and persists the final captured placement.
    /// </summary>
    public void Dispose()
    {
        _saveTimer.Stop();
        _saveTimer.Tick -= OnSaveTimer;
        _window.Changed -= OnWindowChanged;
        IsTracking = false;
        Persist();
    }

    #endregion

    #region Native interop

    /// <summary>
    /// Keeps stable display identity alongside its current work area and primary-display flag.
    /// </summary>
    private sealed record MonitorInfo(string Id, string Name, WindowBounds WorkArea, bool IsPrimary);

    #endregion

    #region Implementation

    /// <summary>
    /// Enumerates monitor handles and resolves each one to display identity and work-area information.
    /// </summary>
    private static List<MonitorInfo> GetMonitors()
    {
        var handles = new List<IntPtr>();
        MonitorEnumProc callback = (IntPtr handle, IntPtr dc, ref NativeRect bounds, IntPtr data) =>
        {
            handles.Add(handle);
            return true;
        };
        if (!EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        return handles.Select(ReadMonitor).ToList();
    }

    /// <summary>
    /// Reads stable monitor identity and physical work-area bounds from Windows.
    /// </summary>
    private static MonitorInfo ReadMonitor(IntPtr handle)
    {
        var info = new NativeMonitorInfo { Size = Marshal.SizeOf<NativeMonitorInfo>() };
        if (!GetMonitorInfo(handle, ref info))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        var device = new DisplayDevice { Size = Marshal.SizeOf<DisplayDevice>() };
        // The interface ID identifies the monitor even if Windows renumbers DISPLAY1/2.
        var id = EnumDisplayDevices(info.DeviceName, 0, ref device, 1) ? device.DeviceId : string.Empty;
        var area = info.WorkArea;
        return new MonitorInfo(id, info.DeviceName,
            new WindowBounds(area.Left, area.Top, area.Right - area.Left, area.Bottom - area.Top),
            (info.Flags & 1) != 0);
    }

    #endregion

    #region Native interop

    /// <summary>
    /// Matches a native rectangle in physical desktop coordinates.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    /// <summary>
    /// Carries monitor bounds, work area and display device name from Windows.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeMonitorInfo
    {
        public int Size;
        public NativeRect Monitor, WorkArea;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
    }

    /// <summary>
    /// Carries display interface identifiers supplied by Windows display enumeration.
    /// </summary>
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

    /// <summary>
    /// Receives one monitor from native display enumeration while the callback remains rooted.
    /// </summary>
    private delegate bool MonitorEnumProc(IntPtr monitor, IntPtr dc, ref NativeRect bounds, IntPtr data);

    /// <summary>
    /// Enumerates attached monitors through the supplied callback.
    /// </summary>
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayMonitors(IntPtr dc, IntPtr clip, MonitorEnumProc callback, IntPtr data);

    /// <summary>
    /// Reads the selected monitor’s device name and work area.
    /// </summary>
    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref NativeMonitorInfo info);

    /// <summary>
    /// Reads the stable display interface identifier used to match saved monitors.
    /// </summary>
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayDevices(string device, uint index, ref DisplayDevice info, uint flags);

    /// <summary>
    /// Finds the display containing or nearest to the main window.
    /// </summary>
    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);

    /// <summary>
    /// Reads the scaling applied by Windows to the current native window.
    /// </summary>
    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr window);

    #endregion
}
