// VoiceDucker - Copyright (c) 2026 Umberto Giacobbi. MIT license.

namespace VoiceDucker.Audio;

/// <summary>
/// Immutable worker snapshot shared with the application model. Detection and
/// playback values describe the same worker update; no native audio objects escape.
/// </summary>
internal sealed record EngineStatus(
    bool Ready,
    bool Running,
    bool Speaking,
    int AffectedSessions,
    GateState Detection,
    string MicrophoneName,
    IReadOnlyList<PlaybackStream> Streams,
    IReadOnlyList<MicrophoneChoice> Microphones,
    string? Error = null,
    bool Restoring = false)
{
    #region Public methods

    /// <summary>Creates an uninitialized snapshot with capture and detection off.</summary>
    public static EngineStatus Initial(DuckingSettings settings) =>
        new(false, false, false, 0, GateState.Off(settings), string.Empty, [], []);

    #endregion
}
