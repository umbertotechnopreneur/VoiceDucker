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
    private static Mutex? _instanceMutex;
    private Window? _window;

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
        _instanceMutex = new Mutex(true, @"Local\VibeWare.VoiceDucker", out var firstInstance);
        if (!firstInstance)
        {
            _instanceMutex.Dispose();
            _instanceMutex = null;
            var existingWindow = FindWindow(null, "VoiceDucker");
            if (existingWindow != IntPtr.Zero)
            {
                ShowWindow(existingWindow, ShowWindowRestore);
                SetForegroundWindow(existingWindow);
            }
            Exit();
            return;
        }

        _window = new MainWindow();
        _window.Activate();
    }

    // SW_RESTORE also shows a window that was hidden in the tray.
    private const int ShowWindowRestore = 9;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? className, string windowName);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr window, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr window);
}
