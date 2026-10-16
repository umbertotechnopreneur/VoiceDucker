// VoiceDucker - Copyright (c) 2026 Umberto Giacobbi. MIT license.

using System.Text.Json;

namespace VoiceDucker;

/// <summary>
/// Shares the crash-safe write sequence used by the app's small JSON stores.
/// Each caller remains responsible for validating its own data.
/// </summary>
internal static class AtomicJsonFile
{
    /// <summary>
    /// Flushes a sibling temporary file before replacing the last saved document.
    /// A failed replacement leaves the previous document intact.
    /// </summary>
    public static void Write<T>(string path, T value)
    {
        var directory = Path.GetDirectoryName(path)!;
        var temporaryPath = path + ".tmp";

        Directory.CreateDirectory(directory);

        try
        {
            using (var stream = new FileStream(
                       temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, value);
                stream.Flush(flushToDisk: true);
            }

            // The temporary file is beside the destination, on the same volume.
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}
