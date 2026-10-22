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

using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Foundation;
using Windows.Graphics;

namespace VoiceDucker;

/// <summary>
/// Measures XAML content, sizes its native window and positions it within the owner display.
/// </summary>
internal static class WindowContentSizing
{
    #region Public methods

    /// <summary>
    /// Measures the content at the display scale, resizes the client area and centers or preserves the window position.
    /// </summary>
    public static void Fit(Window window, FrameworkElement content, double preferredWidth,
        AppWindow? owner = null, bool preservePosition = false, double additionalWidth = 0)
    {
        var scale = Math.Max(0.1, content.XamlRoot?.RasterizationScale ?? 1);
        // Owner position, window size and work area all use physical desktop
        // pixels. A newly created child may still belong to another monitor.
        var workArea = DisplayArea.GetFromWindowId(owner?.Id ?? window.AppWindow.Id,
            DisplayAreaFallback.Primary).WorkArea;
        var edge = (int)Math.Ceiling(20 * scale);
        var requestedClientWidth = (int)Math.Ceiling((preferredWidth + additionalWidth) * scale);
        var availableClientWidth = workArea.Width - 2 * edge;
        var clientWidth = Math.Max(1, Math.Min(requestedClientWidth, availableClientWidth));

        // Reserve space for a vertical scrollbar if the display cannot fit all content.
        var contentWidth = clientWidth / scale * preferredWidth / (preferredWidth + additionalWidth);
        content.Measure(new Size(Math.Max(1, contentWidth - 20), double.PositiveInfinity));
        var desiredHeight = (int)Math.Ceiling((content.DesiredSize.Height + 8) * scale);
        var chromeHeight = Math.Max(0, window.AppWindow.Size.Height - window.AppWindow.ClientSize.Height);
        var maxClientHeight = Math.Max(1, workArea.Height - 2 * edge - chromeHeight);
        var clientHeight = Math.Min(desiredHeight, maxClientHeight);

        window.AppWindow.ResizeClient(new SizeInt32(clientWidth, clientHeight));

        var bounds = window.AppWindow.Size;
        int preferredX;
        int preferredY;

        if (preservePosition)
        {
            preferredX = window.AppWindow.Position.X;
            preferredY = window.AppWindow.Position.Y;
        }
        else if (owner is null)
        {
            preferredX = workArea.X + (workArea.Width - bounds.Width) / 2;
            preferredY = workArea.Y + (workArea.Height - bounds.Height) / 2;
        }
        else
        {
            preferredX = owner.Position.X + (owner.Size.Width - bounds.Width) / 2;
            preferredY = owner.Position.Y + (owner.Size.Height - bounds.Height) / 2;
        }

        var rightmostPosition = Math.Max(workArea.X, workArea.X + workArea.Width - bounds.Width);
        var bottommostPosition = Math.Max(workArea.Y, workArea.Y + workArea.Height - bounds.Height);
        var finalX = Math.Clamp(preferredX, workArea.X, rightmostPosition);
        var finalY = Math.Clamp(preferredY, workArea.Y, bottommostPosition);

        window.AppWindow.Move(new PointInt32(finalX, finalY));
    }

    #endregion
}
