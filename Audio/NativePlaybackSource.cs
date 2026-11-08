// VoiceDucker - Copyright (c) 2026 Umberto Giacobbi. MIT license.

using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace VoiceDucker.Audio;

/// <summary>Core Audio transport; every method and owned COM reference stays on the audio worker.</summary>
internal sealed class NativePlaybackSource(PlaybackVolumeEvents volumeEvents) : IPlaybackSource
{
    #region Private fields

    private readonly MMDeviceEnumerator _devices = new();
    private readonly Dictionary<string, NativePlaybackSession> _connections = new(StringComparer.Ordinal);

    #endregion

    #region Public methods

    /// <summary>
    /// Discovers render sessions, subscribes new instances and releases disappeared connections.
    /// Partial failures retain healthy observations but prevent a complete startup snapshot claim.
    /// </summary>
    public PlaybackReadResult Read()
    {
        var observations = new List<PlaybackObservation>();
        var errors = new List<string>();
        var sessionExpiry = new Dictionary<string, bool>(StringComparer.Ordinal);

        try
        {
            foreach (var endpoint in _devices.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            {
                using (endpoint)
                {
                    try
                    {
                        var sessions = endpoint.AudioSessionManager.Sessions;
                        for (var index = 0; index < sessions.Count; index++)
                        {
                            try
                            {
                                var session = sessions[index];
                                var state = session.State;
                                var endpointId = endpoint.ID;
                                var instanceId = session.GetSessionInstanceIdentifier;
                                var key = endpointId + ":" + instanceId;
                                var expired = state == AudioSessionState.AudioSessionStateExpired;
                                sessionExpiry[key] = expired;
                                if (expired) continue;

                                if (!_connections.ContainsKey(key))
                                {
                                    _connections.Add(key, NativePlaybackSession.Open(endpointId, instanceId, volumeEvents));
                                    AudioDiagnostics.Write(() => $"volume-events-subscribed session={key}");
                                }

                                var processId = (int)session.GetProcessID;
                                var name = processId == 0 ? "System sounds" : PlaybackIdentity.ProcessName(processId);
                                if (string.IsNullOrWhiteSpace(name) || PlaybackIdentity.IsOwnProcess(name)) continue;

                                var volume = session.SimpleAudioVolume;
                                observations.Add(new PlaybackObservation(key, name, processId,
                                    volume.Volume, session.AudioMeterInformation.MasterPeakValue,
                                    volume.Mute, state == AudioSessionState.AudioSessionStateActive));
                            }
                            catch (Exception exception)
                            {
                                errors.Add($"A playback session is unavailable: {exception.Message}");
                            }
                        }
                    }
                    catch (Exception exception)
                    {
                        errors.Add($"A playback device is unavailable: {exception.Message}");
                    }
                }
            }
        }
        catch (Exception exception)
        {
            errors.Add($"Playback devices could not be enumerated: {exception.Message}");
            return new PlaybackReadResult(observations, errors, false, sessionExpiry);
        }

        foreach (var key in _connections.Keys
                     .Where(key => !sessionExpiry.TryGetValue(key, out var expired) || expired).ToArray())
        {
            var connection = _connections[key];
            try
            {
                // An omitted session can still report its state before its subscription is released.
                if (!sessionExpiry.ContainsKey(key) && connection.ReadExpiry() is { } expired)
                    sessionExpiry.Add(key, expired);
            }
            catch (Exception exception)
            {
                errors.Add($"A playback session is unavailable: {exception.Message}");
            }

            try
            {
                connection.Dispose();
            }
            catch (Exception exception)
            {
                errors.Add($"A playback notification could not be released: {exception.Message}");
            }
            finally
            {
                _connections.Remove(key);
            }
        }

        return new PlaybackReadResult(observations, errors, errors.Count == 0, sessionExpiry);
    }

    /// <summary>Reads the current scalar from an existing native session connection.</summary>
    public float ReadVolume(string key)
    {
        if (!_connections.TryGetValue(key, out var session))
            throw new InvalidOperationException("The playback session is unavailable.");

        return session.ReadVolume();
    }

    /// <summary>Writes a tagged session scalar and immediately returns native readback for verification.</summary>
    public float SetVolume(string key, float volume)
    {
        if (!_connections.TryGetValue(key, out var session))
            throw new InvalidOperationException("The playback session is unavailable.");

        session.SetVolume(volume, volumeEvents.EventContext);

        return session.ReadVolume();
    }

    /// <summary>Unsubscribes every connection and releases the worker-owned device enumerator.</summary>
    public void Dispose()
    {
        try
        {
            NativePlaybackSession.DisposeAll(_connections.Values);
        }
        finally
        {
            AudioDiagnostics.Write(() => $"volume-events-unsubscribed count={_connections.Count}");
            _connections.Clear();
            _devices.Dispose();
        }
    }

    #endregion
}

