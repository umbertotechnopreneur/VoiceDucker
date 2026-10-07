using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace VoiceDucker.Audio;

internal sealed record PlaybackStream(string Key, string Name, int ProcessId, float Volume,
    float Peak, bool Muted, bool IsSpotify);

internal sealed class PlaybackMonitor : IDisposable
{
    private readonly MMDeviceEnumerator _devices = new();

    public IReadOnlyList<PlaybackStream> Read()
    {
        var streams = new List<PlaybackStream>();
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
                        if (string.IsNullOrEmpty(name))
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
                    catch (Exception)
                    {
                        // A session can disappear between enumeration and reading it.
                    }
                }
            }
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

                        session.SimpleAudioVolume.Volume = Math.Clamp(volume, 0, 1);
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

    public void Dispose() => _devices.Dispose();
}
