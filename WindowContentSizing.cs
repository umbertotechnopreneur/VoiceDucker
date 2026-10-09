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

using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Foundation;
using Windows.Graphics;

namespace VoiceDucker;

internal static class WindowContentSizing
{
    public static void Fit(Window window, FrameworkElement content, double preferredWidth,
        AppWindow? owner = null, bool preservePosition = false)
    {
        var scale = Math.Max(0.1, content.XamlRoot?.RasterizationScale ?? 1);
        var workArea = DisplayArea.GetFromWindowId(window.AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        var edge = (int)Math.Ceiling(20 * scale);
        var clientWidth = Math.Max(1, Math.Min(
            (int)Math.Ceiling(preferredWidth * scale), workArea.Width - 2 * edge));

        // Reserve space for a vertical scrollbar if the display cannot fit all content.
        content.Measure(new Size(Math.Max(1, clientWidth / scale - 20), double.PositiveInfinity));
        var desiredHeight = (int)Math.Ceiling((content.DesiredSize.Height + 8) * scale);
        var chromeHeight = Math.Max(0, window.AppWindow.Size.Height - window.AppWindow.ClientSize.Height);
        var maxClientHeight = Math.Max(1, workArea.Height - 2 * edge - chromeHeight);
        window.AppWindow.ResizeClient(new SizeInt32(clientWidth, Math.Min(desiredHeight, maxClientHeight)));

        var bounds = window.AppWindow.Size;
        var preferredX = preservePosition ? window.AppWindow.Position.X : owner is null
            ? workArea.X + (workArea.Width - bounds.Width) / 2
            : owner.Position.X + (owner.Size.Width - bounds.Width) / 2;
        var preferredY = preservePosition ? window.AppWindow.Position.Y : owner is null
            ? workArea.Y + (workArea.Height - bounds.Height) / 2
            : owner.Position.Y + (owner.Size.Height - bounds.Height) / 2;
        window.AppWindow.Move(new PointInt32(
            Math.Clamp(preferredX, workArea.X,
                Math.Max(workArea.X, workArea.X + workArea.Width - bounds.Width)),
            Math.Clamp(preferredY, workArea.Y,
                Math.Max(workArea.Y, workArea.Y + workArea.Height - bounds.Height))));
    }
}
