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

using System.Diagnostics;

namespace VoiceDucker.Audio;

/// <summary>Provides consistent process identities for playback discovery and exclusions.</summary>
internal static class PlaybackIdentity
{
    #region Public methods

    /// <summary>Trims a process name and removes an optional executable extension.</summary>
    public static string NormalizeProcessName(string name) =>
        name.Trim().EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? name.Trim()[..^4]
            : name.Trim();

    /// <summary>Identifies VoiceDucker so its own sessions are never offered for ducking.</summary>
    public static bool IsOwnProcess(string name) =>
        NormalizeProcessName(name).Equals("VoiceDucker", StringComparison.OrdinalIgnoreCase);

    /// <summary>Reads a live process name, returning an empty value if the process is unavailable.</summary>
    public static string ProcessName(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return process.ProcessName;
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    #endregion
}
