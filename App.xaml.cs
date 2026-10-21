/* VBWR B
 * Project: VoiceDucker
 * Repository: https://github.com/umbertotechnopreneur/VoiceDucker
 * Creator: Umberto Giacobbi | https://umbertogiacobbi.biz
 * VibeWare initiative: Human intent, AI execution, and plenty of tokens
 * Manifesto: https://umbertogiacobbi.biz/vibeware/manifesto
 * Created with AI: OpenAI Codex assisted the initial implementation; Git history
 * Human guidance: Umberto Giacobbi defined the purpose and audio behavior
 * Evidence: Git history
 * Copyright (c) 2026 Umberto Giacobbi
 * License: MIT
 * VBWR E */

using Microsoft.UI.Xaml;
using System.Runtime.InteropServices;

namespace VoiceDucker;

/// <summary>
/// Owns the application lifetime and enforces a single desktop instance.
/// </summary>
public partial class App : Application
{
    private SingleInstanceService? _singleInstance;
    private MainWindow? _window;

    /// <summary>
    /// Initializes the XAML application.
    /// </summary>
    public App()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Starts the main window, or restores the existing tray window after a second launch.
    /// </summary>
    /// <param name="args">Details about the launch request and process.</param>
    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        _singleInstance = new SingleInstanceService();
        if (!_singleInstance.IsPrimary)
        {
            // Transfer foreground permission when the existing window is ready.
            // The named event, rather than the window title, delivers activation.
            var existingWindow = FindWindow(null, "VoiceDucker");
            if (existingWindow == IntPtr.Zero)
                existingWindow = FindWindow(null, "VoiceDucker - Trial Expired");
            for (var days = 1; existingWindow == IntPtr.Zero && days <= 30; days++)
                existingWindow = FindWindow(null, $"VoiceDucker - Free Trial, {days} {(days == 1 ? "day" : "days")} remaining");
            if (existingWindow == IntPtr.Zero)
            {
                // Trial editions change the window title; still reactivate the existing instance.
                using var existingProcess = System.Diagnostics.Process.GetProcessesByName("VoiceDucker")
                    .FirstOrDefault(process => process.Id != Environment.ProcessId && process.MainWindowHandle != IntPtr.Zero);
                existingWindow = existingProcess?.MainWindowHandle ?? IntPtr.Zero;
            }
            if (existingWindow != IntPtr.Zero)
            {
                GetWindowThreadProcessId(existingWindow, out var processId);
                _ = AllowSetForegroundWindow(processId);
            }
            _singleInstance.RequestActivation();
            _singleInstance.Dispose();
            _singleInstance = null;
            Exit();
            return;
        }

        var isWindowsStartup = StartupRegistration.IsWindowsStartupLaunch();
        _window = new MainWindow(isWindowsStartup);
        _window.Closed += (_, _) =>
        {
            _window = null;
            try { _singleInstance?.Dispose(); }
            finally
            {
                _singleInstance = null;
                Exit();
            }
        };
        var dispatcher = _window.DispatcherQueue;
        _singleInstance.Listen(() =>
            dispatcher.TryEnqueue(() => _window?.ShowForRepeatedLaunch()));
        _window.Activate();
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? className, string windowName);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(uint processId);
}
