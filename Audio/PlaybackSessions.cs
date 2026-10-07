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
    private const float DuckFactor = 0.5f;
    private const float AudiblePeak = 0.001f;
    private const float VolumeTolerance = 0.005f;
    private readonly MMDeviceEnumerator _devices = new();
    private readonly Dictionary<string, SavedVolume> _saved = new(StringComparer.Ordinal);
    private readonly HashSet<string> _manualOverrides = new(StringComparer.Ordinal);

    // shouldDuck: whether microphone input currently exceeds the voice gate.
    // Returns the number of audio sessions whose lowered level is still owned.
    public int Update(bool shouldDuck)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var owned = 0;

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

                    if (_saved.TryGetValue(key, out var saved))
                    {
                        if (!IsAt(volume.Volume, saved.Lowered))
                        {
                            // A mixer change by the user ends our claim on this session.
                            _saved.Remove(key);
                            if (shouldDuck)
                            {
                                _manualOverrides.Add(key);
                            }
                        }
                        else if (shouldDuck)
                        {
                            owned++;
                        }
                        else
                        {
                            volume.Volume = saved.Original;
                            _saved.Remove(key);
                        }
                        continue;
                    }

                    if (!shouldDuck || _manualOverrides.Contains(key) || volume.Mute ||
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

                    var lowered = original * DuckFactor;
                    volume.Volume = lowered;
                    _saved.Add(key, new SavedVolume(original, lowered));
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

    public void RestoreAll() => Update(false);

    public void Dispose() => _devices.Dispose();

    private static bool IsAt(float actual, float expected) =>
        Math.Abs(actual - expected) < VolumeTolerance;

    private sealed record SavedVolume(float Original, float Lowered);
}
