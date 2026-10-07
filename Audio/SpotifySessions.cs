using System.Diagnostics;
using System.ComponentModel;
using NAudio.CoreAudioApi;

namespace VoiceDucker.Audio;

internal sealed class SpotifySessions : IDisposable
{
    private const float DuckFactor = 0.5f;
    private readonly MMDeviceEnumerator _devices = new();
    private readonly Dictionary<string, SavedVolume> _saved = new(StringComparer.Ordinal);

    public bool Update(bool shouldDuck)
    {
        var found = false;
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var endpoint in _devices.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
        {
            using (endpoint)
            {
                var sessions = endpoint.AudioSessionManager.Sessions;
                for (var i = 0; i < sessions.Count; i++)
                {
                    var session = sessions[i];
                    if (!IsSpotify(session.GetProcessID))
                    {
                        continue;
                    }

                    found = true;
                    var key = endpoint.ID + ":" + session.GetSessionInstanceIdentifier;
                    seen.Add(key);
                    var volume = session.SimpleAudioVolume;

                    if (shouldDuck && !_saved.ContainsKey(key))
                    {
                        var original = volume.Volume;
                        var lowered = original * DuckFactor;
                        volume.Volume = lowered;
                        _saved.Add(key, new SavedVolume(original, lowered));
                    }
                    else if (!shouldDuck && _saved.TryGetValue(key, out var saved))
                    {
                        // Respect a level the user changed while VoiceDucker was active.
                        if (Math.Abs(volume.Volume - saved.Lowered) < 0.005f)
                        {
                            volume.Volume = saved.Original;
                        }
                        _saved.Remove(key);
                    }
                }
            }
        }

        // A disappeared Spotify session has no live mixer level to restore.
        foreach (var key in _saved.Keys.Where(key => !seen.Contains(key)).ToArray())
        {
            _saved.Remove(key);
        }

        return found;
    }

    public void RestoreAll()
    {
        Update(false);
    }

    public void Dispose()
    {
        _devices.Dispose();
    }

    private static bool IsSpotify(uint processId)
    {
        if (processId == 0 || processId > int.MaxValue)
        {
            return false;
        }

        try
        {
            using var process = Process.GetProcessById((int)processId);
            return string.Equals(process.ProcessName, "Spotify", StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException)
        {
            // The session can outlive its process during a mixer enumeration.
            return false;
        }
        catch (Win32Exception)
        {
            // A protected non-Spotify process can deny name lookup.
            return false;
        }
    }

    private sealed record SavedVolume(float Original, float Lowered);
}
