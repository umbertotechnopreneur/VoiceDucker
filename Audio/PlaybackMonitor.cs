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

using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace VoiceDucker.Audio;

internal sealed record PlaybackStream(string Key, string Name, int ProcessId, float Volume,
    float Peak, bool Muted, bool IsSpotify);

internal sealed class PlaybackMonitor(PlaybackVolumeSnapshot snapshot, PlaybackVolumeEvents volumeEvents) : IDisposable
{
    private readonly MMDeviceEnumerator _devices = new();
    private readonly Dictionary<string, NativePlaybackSession> _subscriptions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _reportedVersions = new(StringComparer.Ordinal);

    public IReadOnlyList<PlaybackStream> Read()
    {
        var streams = new List<PlaybackStream>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var endpoint in _devices.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
        {
            using (endpoint)
            {
                var sessions = endpoint.AudioSessionManager.Sessions;
                for (var index = 0; index < sessions.Count; index++)
                {
                    try
                    {
                        var session = sessions[index];
                        if (session.State == AudioSessionState.AudioSessionStateExpired) continue;
                        var key = endpoint.ID + ":" + session.GetSessionInstanceIdentifier;
                        seen.Add(key);
                        snapshot.GetOrCapture(key, session.SimpleAudioVolume.Volume);
                        if (!_subscriptions.ContainsKey(key))
                        {
                            _subscriptions.Add(key, NativePlaybackSession.Open(endpoint.ID,
                                session.GetSessionInstanceIdentifier, volumeEvents));
                            AudioDiagnostics.Write(() => $"volume-events-subscribed session={key}");
                        }
                        var change = volumeEvents.Latest(key);
                        if (change is not null && change.Version > _reportedVersions.GetValueOrDefault(key))
                        {
                            _reportedVersions[key] = change.Version;
                            AudioDiagnostics.Write(() => $"external-volume-change session={key} volume={change.Volume:R} muted={change.Muted}");
                        }
                        if (session.State != AudioSessionState.AudioSessionStateActive)
                        {
                            continue;
                        }

                        var processId = (int)session.GetProcessID;
                        if (processId <= 0)
                        {
                            continue;
                        }

                        var name = PlaybackIdentity.ProcessName(processId);
                        if (string.IsNullOrEmpty(name) || PlaybackIdentity.IsOwnProcess(name))
                        {
                            continue;
                        }

                        var audio = session.SimpleAudioVolume;
                        streams.Add(new PlaybackStream(
                            endpoint.ID + ":" + session.GetSessionInstanceIdentifier,
                            name, processId, audio.Volume,
                            session.AudioMeterInformation.MasterPeakValue, audio.Mute,
                            name.StartsWith("Spotify", StringComparison.OrdinalIgnoreCase)));
                    }
                    catch (Exception exception)
                    {
                        throw new InvalidOperationException("Playback session notifications could not be refreshed.", exception);
                    }
                }
            }
        }

        foreach (var key in _subscriptions.Keys.Where(key => !seen.Contains(key)).ToArray())
        {
            _subscriptions[key].Dispose();
            _subscriptions.Remove(key);
            _reportedVersions.Remove(key);
        }

        return streams;
    }

    public bool SetVolume(string key, float volume)
    {
        foreach (var endpoint in _devices.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
        {
            using (endpoint)
            {
                var sessions = endpoint.AudioSessionManager.Sessions;
                for (var index = 0; index < sessions.Count; index++)
                {
                    try
                    {
                        var session = sessions[index];
                        if (key != endpoint.ID + ":" + session.GetSessionInstanceIdentifier)
                        {
                            continue;
                        }

                        if (!_subscriptions.TryGetValue(key, out var native))
                        {
                            native = NativePlaybackSession.Open(endpoint.ID, session.GetSessionInstanceIdentifier,
                                volumeEvents);
                            _subscriptions.Add(key, native);
                        }
                        native.SetVolume(volume, volumeEvents.EventContext);
                        return true;
                    }
                    catch (Exception)
                    {
                        // The session may have ended while the user moved the slider.
                    }
                }
            }
        }
        return false;
    }

    public void Dispose()
    {
        try
        {
            NativePlaybackSession.DisposeAll(_subscriptions.Values);
            AudioDiagnostics.Write(() => $"volume-events-unsubscribed count={_subscriptions.Count}");
        }
        finally
        {
            _subscriptions.Clear();
            _reportedVersions.Clear();
            _devices.Dispose();
        }
    }
}
