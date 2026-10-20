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
/// Tracks only session levels changed by VoiceDucker, across active playback devices.
/// A user mixer edit ends ownership; restoring audio must never undo that edit.
/// </summary>
internal sealed class PlaybackSessions : IDisposable
{
    private const float AudiblePeak = 0.001f;
    private const float VolumeTolerance = 0.005f;
    private readonly MMDeviceEnumerator _devices = new();
    private readonly Dictionary<string, SavedVolume> _saved = new(StringComparer.Ordinal);
    private readonly HashSet<string> _manualOverrides = new(StringComparer.Ordinal);
    private readonly Dictionary<int, bool> _spotifyProcesses = new();
    private readonly Func<long> _clock;

    public PlaybackSessions(Func<long>? clock = null)
    {
        _clock = clock ?? (() => Environment.TickCount64);
    }

    public bool HasOwnedSessions => _saved.Count > 0;

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
                    var processName = PlaybackIdentity.ProcessName((int)session.GetProcessID);
                    var eligible = !settings.IsProcessExcluded(processName) &&
                                   (settings.IncludeOtherSources || IsSpotify(session));

                    if (_saved.TryGetValue(key, out var saved))
                    {
                        if (!IsAt(volume.Volume, saved.Expected))
                        {
                            AudioDiagnostics.Write(() => $"ownership-lost process={processName} session={key} original={saved.Original:R} expected={saved.Expected:R} actual={volume.Volume:R} ducking={shouldDuck}");
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
                            // The tolerance detects mixer edits; it must not leave a fade
                            // short of its target and save that lower level on the next cycle.
                            if (fraction >= 1f ? volume.Volume != next : !IsAt(volume.Volume, next))
                            {
                                volume.Volume = next;
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
        _manualOverrides.RemoveWhere(key => !seen.Contains(key));
        if (!shouldDuck)
        {
            _manualOverrides.Clear();
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

    // Expected is the last value written by this app, not simply the current fade target.
    // Comparing it with the Windows mixer detects external edits before another write.
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
