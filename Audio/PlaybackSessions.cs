/* VBWR B
 * Project: VoiceDucker
 * Repository: https://github.com/umbertotechnopreneur/VoiceDucker
 * Creator: Umberto Giacobbi | https://umbertogiacobbi.biz
 * VibeWare initiative: Human intent, AI execution, and plenty of tokens
 * Manifesto: https://umbertogiacobbi.biz/vibeware/manifesto
 * Created with AI: OpenAI Codex assisted the initial implementation; Git history
 * Modified with AI: OpenAI Codex; all audible playback sessions, 2026-10-08
 * Human guidance: Umberto Giacobbi defined the purpose and audio behavior
 * Evidence: Git history
 * Copyright (c) 2026 Umberto Giacobbi
 * License: MIT
 * VBWR E */

using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace VoiceDucker.Audio;

/// <summary>
/// Fades playing sessions against their immutable app-lifetime volume snapshot.
/// </summary>
internal sealed class PlaybackSessions : IDisposable
{
    private const float AudiblePeak = 0.001f;
    private const float VolumeTolerance = 0.005f;
    private readonly MMDeviceEnumerator _devices = new();
    private readonly Dictionary<string, SavedVolume> _saved = new(StringComparer.Ordinal);
    private readonly PlaybackVolumeSnapshot _snapshot;
    private readonly PlaybackVolumeEvents _volumeEvents;
    private readonly Dictionary<string, NativePlaybackSession> _volumeSessions = new(StringComparer.Ordinal);
    private readonly Dictionary<int, bool> _spotifyProcesses = new();
    private readonly Func<long> _clock;

    public PlaybackSessions(Func<long>? clock = null, PlaybackVolumeSnapshot? snapshot = null,
        PlaybackVolumeEvents? volumeEvents = null)
    {
        _clock = clock ?? (() => Environment.TickCount64);
        _snapshot = snapshot ?? new PlaybackVolumeSnapshot();
        _volumeEvents = volumeEvents ?? new PlaybackVolumeEvents();
    }

    public bool HasOwnedSessions => _saved.Count > 0;

    public void CaptureSnapshot()
    {
        foreach (var endpoint in _devices.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
        {
            using (endpoint)
            {
                var sessions = endpoint.AudioSessionManager.Sessions;
                for (var index = 0; index < sessions.Count; index++)
                {
                    var session = sessions[index];
                    _snapshot.GetOrCapture(endpoint.ID + ":" + session.GetSessionInstanceIdentifier,
                        session.SimpleAudioVolume.Volume);
                }
            }
        }
    }

    /// <summary>
    /// Advances fades and returns the number of lowered sessions still owned by the app.
    /// Existing owned sessions are handled before eligibility checks so exclusions can restore them.
    /// </summary>
    public int Update(bool shouldDuck, DuckingSettings settings, bool restoreImmediately = false)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var owned = 0;
        var now = _clock();

        foreach (var endpoint in _devices.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
        {
            using (endpoint)
            {
                var sessions = endpoint.AudioSessionManager.Sessions;
                for (var index = 0; index < sessions.Count; index++)
                {
                    var session = sessions[index];
                    var key = endpoint.ID + ":" + session.GetSessionInstanceIdentifier;
                    seen.Add(key);
                    var volume = session.SimpleAudioVolume;
                    var original = _snapshot.GetOrCapture(key, volume.Volume);
                    var processName = PlaybackIdentity.ProcessName((int)session.GetProcessID);
                    var eligible = !settings.IsProcessExcluded(processName) &&
                                   (settings.IncludeOtherSources || IsSpotify(session));

                    if (_saved.TryGetValue(key, out var saved))
                    {
                        if (volume.Mute)
                        {
                            _saved.Remove(key);
                            continue;
                        }
                        var external = _volumeEvents.Latest(key);
                        if (external is not null && external.Version > saved.ExternalVersion)
                        {
                            saved.ExternalVersion = external.Version;
                            // Resume from the current mixer level without replacing the snapshot.
                            saved.StartVolume = volume.Volume;
                            saved.Expected = saved.StartVolume;
                            saved.StartTick = now;
                        }
                        if (restoreImmediately)
                        {
                            SetVolume(endpoint.ID, key, session, saved.Original);
                            AudioDiagnostics.Write(() => $"restore-immediate process={processName} session={key} target={saved.Original:R} actual={volume.Volume:R}");
                            _saved.Remove(key);
                        }
                        else
                        {
                            var target = shouldDuck && eligible
                                ? saved.Original * (1f - settings.ReductionPercent / 100f)
                                : saved.Original;
                            var movingDown = IsAt(target, saved.Target)
                                ? saved.Target < saved.StartVolume
                                : target < saved.Expected;
                            var duration = movingDown
                                ? settings.FadeDownMilliseconds
                                : settings.FadeUpMilliseconds;

                            if (!IsAt(target, saved.Target) || duration != saved.Duration)
                            {
                                AudioDiagnostics.Write(() => $"fade-start process={processName} session={key} original={saved.Original:R} actual={volume.Volume:R} target={target:R} duration={duration}");
                                saved.StartVolume = volume.Volume;
                                saved.Target = target;
                                saved.StartTick = now;
                                saved.Duration = duration;
                            }

                            var fraction = duration == 0
                                ? 1f
                                : Math.Clamp((float)(now - saved.StartTick) / duration, 0f, 1f);
                            var next = fraction >= 1f ? saved.Target
                                : saved.StartVolume + (saved.Target - saved.StartVolume) * fraction;
                            // Always finish exactly at the immutable target, even when the
                            // final step is smaller than the mixer-change tolerance.
                            if (fraction >= 1f ? volume.Volume != next : !IsAt(volume.Volume, next))
                            {
                                SetVolume(endpoint.ID, key, session, next);
                                saved.Expected = next;
                            }

                            if ((!shouldDuck || !eligible) && fraction >= 1f)
                            {
                                AudioDiagnostics.Write(() => $"restore-complete process={processName} session={key} target={saved.Original:R} actual={volume.Volume:R}");
                                _saved.Remove(key);
                            }
                            else
                            {
                                owned++;
                            }
                        }
                        continue;
                    }

                    if (!shouldDuck || !eligible || volume.Mute ||
                        settings.ReductionPercent == 0 ||
                        session.State != AudioSessionState.AudioSessionStateActive ||
                        session.AudioMeterInformation.MasterPeakValue <= AudiblePeak)
                    {
                        continue;
                    }

                    if (original <= VolumeTolerance || volume.Volume <= VolumeTolerance)
                    {
                        continue;
                    }

                    var duckTarget = original * (1f - settings.ReductionPercent / 100f);
                    var newVolume = new SavedVolume(original, volume.Volume, duckTarget, now,
                        settings.FadeDownMilliseconds)
                    {
                        ExternalVersion = _volumeEvents.Latest(key)?.Version ?? 0
                    };
                    if (settings.FadeDownMilliseconds == 0)
                    {
                        SetVolume(endpoint.ID, key, session, duckTarget);
                        newVolume.Expected = duckTarget;
                    }
                    _saved.Add(key, newVolume);
                    AudioDiagnostics.Write(() => $"acquire process={processName} session={key} original={original:R} target={duckTarget:R}");
                    owned++;
                }
            }
        }

        // A session that disappeared cannot be restored through the mixer.
        foreach (var key in _saved.Keys.Where(key => !seen.Contains(key)).ToArray())
        {
            AudioDiagnostics.Write(() => $"session-disappeared session={key} original={_saved[key].Original:R} expected={_saved[key].Expected:R}");
            _saved.Remove(key);
        }
        foreach (var key in _volumeSessions.Keys.Where(key => !seen.Contains(key)).ToArray())
        {
            _volumeSessions[key].Dispose();
            _volumeSessions.Remove(key);
        }

        return owned;
    }

    /// <summary>
    /// Restores only levels the app still owns, immediately on Disable or with an exit fade.
    /// Runs on the audio worker; it must not block the UI thread directly.
    /// </summary>
    public void RestoreAll(int fadeMilliseconds)
    {
        if (fadeMilliseconds == 0)
        {
            Update(false, new DuckingSettings(), true);
            return;
        }

        var settings = new DuckingSettings(FadeUpMilliseconds: fadeMilliseconds);
        while (HasOwnedSessions)
        {
            Update(false, settings);
            if (HasOwnedSessions)
            {
                Thread.Sleep(50);
            }
        }
    }

    public void Dispose()
    {
        try { NativePlaybackSession.DisposeAll(_volumeSessions.Values); }
        finally
        {
            _volumeSessions.Clear();
            _devices.Dispose();
        }
    }

    private void SetVolume(string endpointId, string key, AudioSessionControl session, float volume)
    {
        if (!_volumeSessions.TryGetValue(key, out var native))
        {
            native = NativePlaybackSession.Open(endpointId, session.GetSessionInstanceIdentifier);
            _volumeSessions.Add(key, native);
        }
        native.SetVolume(volume, _volumeEvents.EventContext);
    }

    private static bool IsAt(float actual, float expected) =>
        Math.Abs(actual - expected) < VolumeTolerance;

    private bool IsSpotify(AudioSessionControl session)
    {
        var processId = (int)session.GetProcessID;
        if (processId <= 0)
        {
            return false;
        }

        if (_spotifyProcesses.TryGetValue(processId, out var spotify))
        {
            return spotify;
        }

        spotify = PlaybackIdentity.ProcessName(processId)
            .StartsWith("Spotify", StringComparison.OrdinalIgnoreCase);
        _spotifyProcesses[processId] = spotify;
        return spotify;
    }

    // External changes can restart a fade, but Original always comes from the startup snapshot.
    private sealed class SavedVolume(float original, float current, float target, long startTick, int duration)
    {
        public float Original { get; } = original;
        public float Expected { get; set; } = current;
        public float StartVolume { get; set; } = current;
        public float Target { get; set; } = target;
        public long StartTick { get; set; } = startTick;
        public int Duration { get; set; } = duration;
        public long ExternalVersion { get; set; }
    }
}
