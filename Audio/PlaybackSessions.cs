/* VBWR B
 * Project: VoiceDucker
 * Repository: https://github.com/umbertotechnopreneur/VoiceDucker
 * Creator: Umberto Giacobbi | https://umbertogiacobbi.biz
 * VibeWare initiative: Human intent. AI implementation. Accountable human review.
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

// Tracks only session levels changed by VoiceDucker, across active playback devices.
internal sealed class PlaybackSessions : IDisposable
{
    private const float AudiblePeak = 0.001f;
    private const float VolumeTolerance = 0.005f;
    private readonly MMDeviceEnumerator _devices = new();
    private readonly Dictionary<string, SavedVolume> _saved = new(StringComparer.Ordinal);
    private readonly HashSet<string> _manualOverrides = new(StringComparer.Ordinal);
    private readonly Dictionary<int, bool> _spotifyProcesses = new();

    public bool HasOwnedSessions => _saved.Count > 0;

    // shouldDuck: whether microphone input currently exceeds the voice gate.
    // Returns the number of audio sessions whose lowered level is still owned.
    public int Update(bool shouldDuck, DuckingSettings settings, bool restoreImmediately = false)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var owned = 0;
        var now = Environment.TickCount64;

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
                    var eligible = settings.IncludeOtherSources || IsSpotify(session);

                    if (_saved.TryGetValue(key, out var saved))
                    {
                        if (!IsAt(volume.Volume, saved.Expected))
                        {
                            // A mixer change by the user ends our claim on this session.
                            _saved.Remove(key);
                            if (shouldDuck)
                            {
                                _manualOverrides.Add(key);
                            }
                        }
                        else if (restoreImmediately)
                        {
                            volume.Volume = saved.Original;
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
                                saved.StartVolume = volume.Volume;
                                saved.Target = target;
                                saved.StartTick = now;
                                saved.Duration = duration;
                            }

                            var fraction = duration == 0
                                ? 1f
                                : Math.Clamp((float)(now - saved.StartTick) / duration, 0f, 1f);
                            var next = saved.StartVolume + (saved.Target - saved.StartVolume) * fraction;
                            if (!IsAt(volume.Volume, next))
                            {
                                volume.Volume = next;
                            }
                            saved.Expected = next;

                            if ((!shouldDuck || !eligible) && fraction >= 1f)
                            {
                                _saved.Remove(key);
                            }
                            else
                            {
                                owned++;
                            }
                        }
                        continue;
                    }

                    if (!shouldDuck || !eligible || _manualOverrides.Contains(key) || volume.Mute ||
                        settings.ReductionPercent == 0 ||
                        session.State != AudioSessionState.AudioSessionStateActive ||
                        session.AudioMeterInformation.MasterPeakValue <= AudiblePeak)
                    {
                        continue;
                    }

                    var original = volume.Volume;
                    if (original <= VolumeTolerance)
                    {
                        continue;
                    }

                    var duckTarget = original * (1f - settings.ReductionPercent / 100f);
                    var newVolume = new SavedVolume(original, duckTarget, now,
                        settings.FadeDownMilliseconds);
                    if (settings.FadeDownMilliseconds == 0)
                    {
                        volume.Volume = duckTarget;
                        newVolume.Expected = duckTarget;
                    }
                    _saved.Add(key, newVolume);
                    owned++;
                }
            }
        }

        // A session that disappeared cannot be restored through the mixer.
        foreach (var key in _saved.Keys.Where(key => !seen.Contains(key)).ToArray())
        {
            _saved.Remove(key);
        }
        _manualOverrides.RemoveWhere(key => !seen.Contains(key));
        if (!shouldDuck)
        {
            _manualOverrides.Clear();
        }

        return owned;
    }

    public void RestoreAll() => Update(false, new DuckingSettings(), true);

    public void Dispose() => _devices.Dispose();

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

    private sealed class SavedVolume(float original, float target, long startTick, int duration)
    {
        public float Original { get; } = original;
        public float Expected { get; set; } = original;
        public float StartVolume { get; set; } = original;
        public float Target { get; set; } = target;
        public long StartTick { get; set; } = startTick;
        public int Duration { get; set; } = duration;
    }
}
