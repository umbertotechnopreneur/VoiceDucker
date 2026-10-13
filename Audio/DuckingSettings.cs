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

namespace VoiceDucker.Audio;

internal sealed record DuckingSettings(
    int ReductionPercent = 90,
    int FadeDownMilliseconds = 300,
    int FadeUpMilliseconds = 30000,
    bool IncludeOtherSources = false)
{
    public int MicrophoneGainDecibels { get; init; } = 0;

    public string MicrophoneDeviceId { get; init; } = string.Empty;

    public int RestoreDelayMilliseconds { get; init; } = 650;

    public string[] BlacklistedProcesses { get; init; } = [];

    public bool IsProcessExcluded(string name) => PlaybackIdentity.IsOwnProcess(name) ||
        BlacklistedProcesses.Any(hidden => PlaybackIdentity.NormalizeProcessName(hidden).Equals(
            PlaybackIdentity.NormalizeProcessName(name), StringComparison.OrdinalIgnoreCase));

    public bool IsValid => MicrophoneGainDecibels is >= -24 and <= 36 && ReductionPercent is >= 0 and <= 100 &&
                           MicrophoneDeviceId is not null &&
                           RestoreDelayMilliseconds is >= 0 and <= 10000 &&
                           FadeDownMilliseconds is >= 0 and <= 5000 &&
                           FadeUpMilliseconds is >= 0 and <= 30000 &&
                           BlacklistedProcesses is not null &&
                           BlacklistedProcesses.All(name => !string.IsNullOrWhiteSpace(name));
}
