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

namespace VoiceDucker.Audio;

internal sealed class VoiceGate
{
    private readonly object _sync = new();
    private double _threshold = 0.018;
    private int _hysteresisDecibels = 6;
    private readonly Func<long> _clock;
    private int _holdMilliseconds = 650;
    private long _lastLoudTick = -1;
    private double _latestRms;
    private bool _signalActive;

    public VoiceGate(Func<long>? clock = null)
    {
        _clock = clock ?? (() => Environment.TickCount64);
    }

    public double Threshold
    {
        get { lock (_sync) return _threshold; }
        set
        {
            if (!double.IsFinite(value) || value is < 0.001 or > 1)
                throw new ArgumentOutOfRangeException(nameof(value));
            lock (_sync)
            {
                if (_threshold == value) return;
                _threshold = value;
                EvaluateLevel();
            }
        }
    }

    public int HysteresisDecibels
    {
        get { lock (_sync) return _hysteresisDecibels; }
        set
        {
            if (value is < 0 or > 12)
                throw new ArgumentOutOfRangeException(nameof(value));
            lock (_sync)
            {
                if (_hysteresisDecibels == value) return;
                _hysteresisDecibels = value;
                EvaluateLevel();
            }
        }
    }

    public void Configure(DuckingSettings settings)
    {
        if (!settings.IsValid) throw new ArgumentOutOfRangeException(nameof(settings));
        lock (_sync)
        {
            var threshold = settings.GetThresholdAmplitude();
            var thresholdsChanged = _threshold != threshold || _hysteresisDecibels != settings.HysteresisDecibels;
            _threshold = threshold;
            _hysteresisDecibels = settings.HysteresisDecibels;
            _holdMilliseconds = settings.RestoreDelayMilliseconds;
            if (thresholdsChanged) EvaluateLevel();
        }
    }

    public int HoldMilliseconds
    {
        get { lock (_sync) return _holdMilliseconds; }
        set
        {
            if (value is < 0 or > 10000)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }
            lock (_sync) _holdMilliseconds = value;
        }
    }

    public void Observe(double rms)
    {
        lock (_sync)
        {
            _latestRms = double.IsFinite(rms) && rms > 0 ? rms : 0;
            EvaluateLevel();
        }
    }

    // Called with _sync held. Only a new observation or changed thresholds renews the hold.
    private void EvaluateLevel()
    {
        var releaseThreshold = _threshold * Math.Pow(10, -_hysteresisDecibels / 20.0);
        _signalActive = _latestRms >= _threshold || (_signalActive && _latestRms >= releaseThreshold);
        if (_signalActive) _lastLoudTick = _clock();
    }

    public bool IsSpeaking
    {
        get
        {
            lock (_sync)
            {
                return _signalActive || (_holdMilliseconds > 0 && _lastLoudTick >= 0 &&
                    _clock() - _lastLoudTick <= _holdMilliseconds);
            }
        }
    }
}
