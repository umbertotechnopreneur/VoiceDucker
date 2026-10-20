// VoiceDucker - Copyright (c) 2026 Umberto Giacobbi. MIT license.

using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using NAudio.CoreAudioApi.Interfaces;

namespace VoiceDucker.Audio;

internal sealed record ExternalVolumeChange(long Version, float Volume, bool Muted);

/// <summary>
/// Separates our mixer writes from external callbacks without blocking a COM thread.
/// The UI and audio worker read the latest external event independently.
/// </summary>
internal sealed class PlaybackVolumeEvents
{
    private readonly ConcurrentDictionary<string, ExternalVolumeChange> _changes = new(StringComparer.Ordinal);
    private long _version;

    public Guid EventContext { get; } = Guid.NewGuid();

    public ExternalVolumeChange? Latest(string key) => _changes.GetValueOrDefault(key);

    internal VolumeEventSink CreateSink(string key) => new(this, key);

    [ComVisible(true)]
    [ClassInterface(ClassInterfaceType.None)]
    internal sealed class VolumeEventSink(PlaybackVolumeEvents owner, string key) : IAudioSessionEvents
    {
        private int _active = 1;

        public void Deactivate() => Interlocked.Exchange(ref _active, 0);

        public int OnSimpleVolumeChanged(float volume, bool isMuted, ref Guid eventContext)
        {
            if (Volatile.Read(ref _active) != 0 && eventContext != owner.EventContext)
            {
                var change = new ExternalVolumeChange(Interlocked.Increment(ref owner._version), volume, isMuted);
                owner._changes.AddOrUpdate(key, change,
                    (_, previous) => previous.Version > change.Version ? previous : change);
            }
            return 0;
        }

        public int OnDisplayNameChanged(string displayName, ref Guid eventContext) => 0;
        public int OnIconPathChanged(string iconPath, ref Guid eventContext) => 0;
        public int OnChannelVolumeChanged(uint channelCount, IntPtr newVolumes, uint channelIndex,
            ref Guid eventContext) => 0;
        public int OnGroupingParamChanged(ref Guid groupingId, ref Guid eventContext) => 0;
        public int OnStateChanged(AudioSessionState state) => 0;
        public int OnSessionDisconnected(AudioSessionDisconnectReason disconnectReason) => 0;
    }
}
