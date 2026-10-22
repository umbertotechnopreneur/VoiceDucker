/* VBWR B
 * Project: VoiceDucker
 * Repository: https://github.com/umbertotechnopreneur/VoiceDucker
 * Creator: Umberto Giacobbi | https://umbertogiacobbi.biz
 * VibeWare: Human intent, AI execution, and plenty of tokens
 * Manifesto: https://umbertogiacobbi.biz/vibeware/manifesto
 * Created with AI: See file history for implementation provenance.
 * Modified with AI: OpenAI Codex; source attribution and release preparation, 2026-10-09.
 * Human guidance: Umberto Giacobbi requested branding and the public 0.1.0 release.
 * Evidence: Git history and GitHub Actions; this header does not certify human review.
 * Copyright (c) 2026 Umberto Giacobbi
 * SPDX-License-Identifier: MIT
 * License: MIT - see LICENSE
 * VBWR E */

using Microsoft.UI.Xaml;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Windows.Graphics;

namespace VoiceDucker;

/// <summary>
/// Presents the owner's close choices in a content-sized window centered on the main window.
/// </summary>
public sealed partial class CloseChoiceWindow : Window
{
    #region Private fields

    private readonly AppWindow _owner;

    #endregion

    #region Initialization

    /// <summary>
    /// Attaches the choice window to its owner and prepares measurement on the owner's monitor.
    /// </summary>
    public CloseChoiceWindow(IntPtr ownerHandle)
    {
        InitializeComponent();
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));
        NativeWindowOwner.SetOwner(WinRT.Interop.WindowNative.GetWindowHandle(this), ownerHandle);
        _owner = AppWindow.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(ownerHandle));

        // Place the still-hidden child on the owner's monitor before XAML is
        // measured, so RasterizationScale reflects that monitor's DPI.
        var workArea = DisplayArea.GetFromWindowId(_owner.Id, DisplayAreaFallback.Primary).WorkArea;
        AppWindow.MoveAndResize(new RectInt32(workArea.X, workArea.Y,
            Math.Min(AppWindow.Size.Width, workArea.Width),
            Math.Min(AppWindow.Size.Height, workArea.Height)));
        ClosePanel.Loaded += OnClosePanelLoaded;
    }

    #endregion

    #region Public methods and events

    /// <summary>Raised after the user closes this dialog and chooses to minimize the main window.</summary>
    public event Action? MinimizeRequested;

    /// <summary>Raised after the user closes this dialog and chooses to exit the application.</summary>
    public event Action? ExitRequested;

    #endregion

    #region Implementation

    /// <summary>
    /// Fits the measured content and centers the final window size relative to its owner.
    /// </summary>
    private void OnClosePanelLoaded(object sender, RoutedEventArgs args)
    {
        ClosePanel.Loaded -= OnClosePanelLoaded;
        WindowContentSizing.Fit(this, ClosePanel, 460, _owner);
    }

    /// <summary>
    /// Dismisses the choice window while leaving the main window open.
    /// </summary>
    private void Cancel_Click(object sender, RoutedEventArgs args) => Close();

    /// <summary>
    /// Dismisses the choice window before notifying the owner to minimize.
    /// </summary>
    private void Minimize_Click(object sender, RoutedEventArgs args)
    {
        Close();
        MinimizeRequested?.Invoke();
    }

    /// <summary>
    /// Dismisses the choice window before notifying the owner to exit.
    /// </summary>
    private void CloseApp_Click(object sender, RoutedEventArgs args)
    {
        Close();
        ExitRequested?.Invoke();
    }

    #endregion
}
