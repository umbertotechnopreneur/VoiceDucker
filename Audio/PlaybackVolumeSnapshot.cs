// VoiceDucker - Copyright (c) 2026 Umberto Giacobbi. MIT license.

using System.Collections.Concurrent;

namespace VoiceDucker.Audio;

/// <summary>
/// Keeps the first observed volume of each playback session until the app exits.
/// Only scalar values cross between the UI monitor and the audio worker.
/// </summary>
internal sealed class PlaybackVolumeSnapshot
{
    private readonly ConcurrentDictionary<string, float> _volumes = new(StringComparer.Ordinal);

    public float GetOrCapture(string key, float volume) => _volumes.GetOrAdd(key, volume);
}
