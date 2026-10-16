// VoiceDucker — Copyright (c) 2026 Umberto Giacobbi. MIT license.

using System.Text.Json;

namespace VoiceDucker;

internal static class WindowPlacementStore
{
    internal static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "VibeWare", "VoiceDucker", "window-placement.json");

    public static WindowPlacementState? Load(string? path = null)
    {
        path ??= FilePath;
        if (!File.Exists(path)) return null;
        var state = JsonSerializer.Deserialize<WindowPlacementState>(File.ReadAllText(path));
        return state is { IsValid: true } ? state : null;
    }

    public static void Save(WindowPlacementState state, string? path = null)
    {
        if (!state.IsValid) throw new ArgumentException("Invalid window placement.", nameof(state));
        path ??= FilePath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporaryPath = path + ".tmp";
        try
        {
            using (var stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, state);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }
}
