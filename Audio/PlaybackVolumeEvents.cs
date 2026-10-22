// VoiceDucker - Copyright (c) 2026 Umberto Giacobbi. MIT license.

using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using NAudio.CoreAudioApi.Interfaces;

namespace VoiceDucker.Audio;

/// <summary>A scalar external volume/mute observation ordered with application commands.</summary>
internal sealed record ExternalVolumeChange(long Version, float Volume, bool Muted);

/// <summary>
/// Separates our mixer writes from external callbacks without blocking a COM thread.
/// Application commands and external notifications share a monotonic intent order.
/// </summary>
internal sealed class PlaybackVolumeEvents
{
    #region Private fields

    private readonly ConcurrentDictionary<string, ExternalVolumeChange> _changes = new(StringComparer.Ordinal);
    private long _version;

    #endregion

    #region Public state

    /// <summary>Marks all application mixer writes so their callbacks cannot become new user intent.</summary>
    public Guid EventContext { get; } = Guid.NewGuid();

    #endregion

    #region Public methods

    /// <summary>Assigns intent order when a command or callback arrives, before worker execution.</summary>
    public long NextIntentVersion() => Interlocked.Increment(ref _version);

    /// <summary>Returns the latest accepted external observation for a session without blocking.</summary>
    public ExternalVolumeChange? Latest(string key) => _changes.GetValueOrDefault(key);

    /// <summary>Creates an independently deactivatable notification sink for one session instance.</summary>
    internal VolumeEventSink CreateSink(string key) => new(this, key);

    #endregion

    #region Callback implementation

    /// <summary>
    /// Receives Core Audio callbacks and publishes only scalar external changes.
    /// No callback waits for the worker or retains a native session object.
    /// </summary>
    [ComVisible(true)]
    [ClassInterface(ClassInterfaceType.None)]
    internal sealed class VolumeEventSink(PlaybackVolumeEvents owner, string key) : IAudioSessionEvents
    {
        #region Private fields

        private int _active = 1;

        #endregion

        #region Public methods

        /// <summary>Rejects late callbacks before the native subscription is released.</summary>
        public void Deactivate() => Interlocked.Exchange(ref _active, 0);

        /// <summary>Publishes a foreign volume or mute change, retaining the newest concurrent event.</summary>
        public int OnSimpleVolumeChanged(float volume, bool isMuted, ref Guid eventContext)
        {
            if (Volatile.Read(ref _active) != 0 && eventContext != owner.EventContext)
            {
                var change = new ExternalVolumeChange(owner.NextIntentVersion(), volume, isMuted);
                owner._changes.AddOrUpdate(key, change,
                    (_, previous) => previous.Version > change.Version ? previous : change);
            }

            return 0;
        }

        /// <summary>Acknowledges display-name notifications; discovery supplies presentation names.</summary>
        public int OnDisplayNameChanged(string displayName, ref Guid eventContext) => 0;

        /// <summary>Acknowledges icon notifications; icons are resolved outside audio callbacks.</summary>
        public int OnIconPathChanged(string iconPath, ref Guid eventContext) => 0;

        /// <summary>Acknowledges per-channel notifications; control uses the session scalar volume.</summary>
        public int OnChannelVolumeChanged(uint channelCount, IntPtr newVolumes, uint channelIndex,
            ref Guid eventContext) => 0;

        /// <summary>Acknowledges grouping changes without changing session-instance identity.</summary>
        public int OnGroupingParamChanged(ref Guid groupingId, ref Guid eventContext) => 0;

        /// <summary>Acknowledges lifecycle changes; worker discovery refreshes active session state.</summary>
        public int OnStateChanged(AudioSessionState state) => 0;

        /// <summary>Acknowledges disconnection; worker discovery owns unavailable-session cleanup.</summary>
        public int OnSessionDisconnected(AudioSessionDisconnectReason disconnectReason) => 0;

        #endregion
    }

    #endregion
}
