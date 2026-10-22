// VoiceDucker - Copyright (c) 2026 Umberto Giacobbi. MIT license.

namespace VoiceDucker.Audio;

/// <summary>A captured level and monotonic observation time; no audio samples are retained.</summary>
internal sealed record CapturedMicrophoneSample(double RawRms, long Timestamp, long Sequence);

internal enum DetectionPhase
{
    Off,
    BelowThreshold,
    AboveThreshold,
    Hysteresis,
    Hold,
    Stale
}

/// <summary>The single detection result used by playback control and presentation.</summary>
internal sealed record GateState(
    DetectionPhase Phase,
    bool Speaking,
    double Rms,
    double Decibels,
    double ThresholdDecibels,
    double ReleaseThresholdDecibels,
    long SampleAgeMilliseconds)
{
    public static GateState Off(DuckingSettings settings) => new(
        DetectionPhase.Off, false, 0, double.NegativeInfinity,
        settings.ThresholdDecibels,
        settings.ThresholdDecibels - settings.HysteresisDecibels, 0);
}

/// <summary>
/// Evaluates captured levels on the audio worker. Settings, gain, hysteresis and
/// release timing are applied together; polling never creates a new observation.
/// </summary>
internal sealed class VoiceGate(Func<long>? clock = null)
{
    public const int StaleAfterMilliseconds = 1000;

    private readonly Func<long> _clock = clock ?? (() => Environment.TickCount64);
    private CapturedMicrophoneSample? _latestSample;
    private long? _firstEvaluationTick;
    private long? _lastLoudTick;
    private bool _signalActive;

    public GateState Evaluate(CapturedMicrophoneSample? sample, DuckingSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!settings.IsValid)
            throw new ArgumentOutOfRangeException(nameof(settings));

        var now = _clock();
        _firstEvaluationTick ??= now;
        if (sample is not null &&
            (_latestSample is null || sample.Sequence > _latestSample.Sequence))
        {
            _latestSample = sample;
        }

        var observedAt = _latestSample?.Timestamp ?? _firstEvaluationTick.Value;
        var age = Math.Max(0, now - observedAt);
        var releaseDecibels = settings.ThresholdDecibels - settings.HysteresisDecibels;
        if (age >= StaleAfterMilliseconds)
        {
            _signalActive = false;
            _lastLoudTick = null;
            return new GateState(DetectionPhase.Stale, false, 0, double.NegativeInfinity,
                settings.ThresholdDecibels, releaseDecibels, age);
        }

        var rms = MicrophoneLevel.ApplyGain(_latestSample?.RawRms ?? 0,
            settings.MicrophoneGainDecibels);
        var threshold = settings.GetThresholdAmplitude();
        var releaseThreshold = threshold * Math.Pow(10, -settings.HysteresisDecibels / 20.0);
        var aboveThreshold = _latestSample is not null && rms >= threshold;
        var heldByHysteresis = _latestSample is not null && _signalActive && rms >= releaseThreshold;
        _signalActive = aboveThreshold || heldByHysteresis;

        if (_signalActive)
        {
            // Reclassifying a sample after a settings edit keeps its original time.
            // Reading the same sequence on another worker tick cannot renew hold.
            _lastLoudTick = _lastLoudTick.HasValue
                ? Math.Max(_lastLoudTick.Value, observedAt) : observedAt;
        }

        var holding = !_signalActive && settings.RestoreDelayMilliseconds > 0 &&
            _lastLoudTick.HasValue && now - _lastLoudTick.Value < settings.RestoreDelayMilliseconds;
        var phase = aboveThreshold ? DetectionPhase.AboveThreshold
            : heldByHysteresis ? DetectionPhase.Hysteresis
            : holding ? DetectionPhase.Hold
            : DetectionPhase.BelowThreshold;
        return new GateState(phase, _signalActive || holding, rms,
            rms > 0 ? 20 * Math.Log10(rms) : double.NegativeInfinity,
            settings.ThresholdDecibels, releaseDecibels, age);
    }

    public void Reset()
    {
        _latestSample = null;
        _firstEvaluationTick = null;
        _lastLoudTick = null;
        _signalActive = false;
    }
}
