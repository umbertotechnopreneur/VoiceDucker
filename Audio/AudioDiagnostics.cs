// VoiceDucker - Copyright (c) 2026 Umberto Giacobbi. MIT license.

namespace VoiceDucker.Audio;

/// <summary>Opt-in, bounded local volume diagnostics. Never stores microphone samples.</summary>
internal static class AudioDiagnostics
{
    #region Private fields

    private static readonly object Sync = new();
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "VibeWare", "VoiceDucker", "audio-diagnostics.log");
    private static readonly bool Enabled = Environment.GetEnvironmentVariable("VOICEDUCKER_AUDIO_LOG") == "1" ||
        File.Exists(Path.Combine(Path.GetDirectoryName(LogPath)!, "audio-diagnostics.enabled"));

    #endregion

    #region Public methods

    /// <summary>
    /// Evaluates and appends a diagnostic message only when logging is enabled.
    /// Rotation and write failures never interrupt audio control or restoration.
    /// </summary>
    public static void Write(Func<string> message)
    {
        if (!Enabled) return;

        try
        {
            lock (Sync)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
                if (File.Exists(LogPath) && new FileInfo(LogPath).Length >= 1024 * 1024)
                    File.Move(LogPath, LogPath + ".previous", overwrite: true);

                File.AppendAllText(LogPath, $"{DateTimeOffset.UtcNow:O} pid={Environment.ProcessId} {message()}{Environment.NewLine}");
            }
        }
        catch (Exception)
        {
            // Diagnostics must not interrupt playback or restoration.
        }
    }

    #endregion
}
