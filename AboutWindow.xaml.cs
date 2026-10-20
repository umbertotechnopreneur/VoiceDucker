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

namespace VoiceDucker;

public sealed partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));
        try
        {
            var version = Windows.ApplicationModel.Package.Current.Id.Version;
            VersionText.Text = $"Version {version.Major}.{version.Minor}.{version.Build}.{version.Revision}";
        }
        catch (InvalidOperationException)
        {
            VersionText.Text = $"Version {typeof(App).Assembly.GetName().Version?.ToString(3) ?? "unknown"}";
        }
        AboutPanel.Loaded += OnAboutPanelLoaded;
    }

    private void OnAboutPanelLoaded(object sender, RoutedEventArgs args)
    {
        AboutPanel.Loaded -= OnAboutPanelLoaded;
        WindowContentSizing.Fit(this, AboutPanel, 560);
    }

    private void Close_Click(object sender, RoutedEventArgs args) => Close();
}
