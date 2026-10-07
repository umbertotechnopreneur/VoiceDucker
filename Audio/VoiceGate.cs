/* VBWR B
 * Project: VoiceDucker
 * Repository: https://github.com/umbertotechnopreneur/VoiceDucker
 * Creator: Umberto Giacobbi | https://umbertogiacobbi.biz
 * VibeWare initiative: Human intent. AI implementation. Accountable human review.
 * Manifesto: https://umbertogiacobbi.biz/vibeware/manifesto
 * Created with AI: OpenAI Codex assisted the initial implementation; Git history
 * Human guidance: Umberto Giacobbi defined the purpose and audio behavior
 * Evidence: Git history
 * Copyright (c) 2026 Umberto Giacobbi
 * License: MIT
 * VBWR E */

namespace VoiceDucker.Audio;

internal sealed class VoiceGate
{
    private const double Threshold = 0.018;
    private const long HoldMilliseconds = 650;
    private long _lastLoudTick = -1;

    public void Observe(double rms)
    {
        if (rms >= Threshold)
        {
            Volatile.Write(ref _lastLoudTick, Environment.TickCount64);
        }
    }

    public bool IsSpeaking
    {
        get
        {
            var last = Volatile.Read(ref _lastLoudTick);
            return last >= 0 && Environment.TickCount64 - last <= HoldMilliseconds;
        }
    }
}
