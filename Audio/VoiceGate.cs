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
    public const double Threshold = 0.018;
    private readonly Func<long> _clock;
    private int _holdMilliseconds = 650;
    private long _lastLoudTick = -1;
    private double _latestRms;

    public VoiceGate(Func<long>? clock = null)
    {
        _clock = clock ?? (() => Environment.TickCount64);
    }

    public int HoldMilliseconds
    {
        get => Volatile.Read(ref _holdMilliseconds);
        set
        {
            if (value is < 0 or > 10000)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }
            Volatile.Write(ref _holdMilliseconds, value);
        }
    }

    public void Observe(double rms)
    {
        if (rms >= Threshold)
        {
            Volatile.Write(ref _lastLoudTick, _clock());
        }
        Volatile.Write(ref _latestRms, rms);
    }

    public bool IsSpeaking
    {
        get
        {
            if (Volatile.Read(ref _latestRms) >= Threshold)
            {
                return true;
            }
            var last = Volatile.Read(ref _lastLoudTick);
            var holdMilliseconds = HoldMilliseconds;
            return holdMilliseconds > 0 && last >= 0 && _clock() - last <= holdMilliseconds;
        }
    }
}
