// VoiceDucker - Copyright (c) 2026 Umberto Giacobbi. MIT license.

namespace VoiceDucker.Audio;

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
    public static EngineStatus Initial(DuckingSettings settings) =>
        new(false, false, false, 0, GateState.Off(settings), string.Empty, [], []);
}
