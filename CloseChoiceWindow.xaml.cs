/* VBWR B
 * Project: VoiceDucker
 * Repository: https://github.com/umbertotechnopreneur/VoiceDucker
 * Creator: Umberto Giacobbi | https://umbertogiacobbi.biz
 * VibeWare: Human intent. AI implementation. Accountable human review.
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

namespace VoiceDucker;

public sealed partial class CloseChoiceWindow : Window
{
    private readonly AppWindow _owner;
    public event Action? MinimizeRequested;
    public event Action? ExitRequested;

    public CloseChoiceWindow(IntPtr ownerHandle)
    {
        InitializeComponent();
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));
        NativeWindowOwner.SetOwner(WinRT.Interop.WindowNative.GetWindowHandle(this), ownerHandle);
        _owner = AppWindow.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(ownerHandle));
        ClosePanel.Loaded += OnClosePanelLoaded;
    }

    private void OnClosePanelLoaded(object sender, RoutedEventArgs args)
    {
        ClosePanel.Loaded -= OnClosePanelLoaded;
        WindowContentSizing.Fit(this, ClosePanel, 460, _owner);
    }

    private void Cancel_Click(object sender, RoutedEventArgs args) => Close();

    private void Minimize_Click(object sender, RoutedEventArgs args)
    {
        Close();
        MinimizeRequested?.Invoke();
    }

    private void CloseApp_Click(object sender, RoutedEventArgs args)
    {
        Close();
        ExitRequested?.Invoke();
    }
}
