// VoiceDucker — Copyright (c) 2026 Umberto Giacobbi. MIT license.

using System.Text.Json;

namespace VoiceDucker;

/// <summary>
/// Persists window layout separately from audio preferences.
/// Display adaptation belongs to WindowPlacementState, not to file storage.
/// </summary>
internal static class WindowPlacementStore
{
    internal static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "VibeWare", "VoiceDucker", "window-placement.json");

    public static WindowPlacementState? Load(string? path = null)
    {
        path ??= FilePath;

        if (!File.Exists(path))
        {
            return null;
        }

        var state = JsonSerializer.Deserialize<WindowPlacementState>(File.ReadAllText(path));
        return state is { IsValid: true } ? state : null;
    }

    public static void Save(WindowPlacementState state, string? path = null)
    {
        if (!state.IsValid)
        {
            throw new ArgumentException("Invalid window placement.", nameof(state));
        }

        AtomicJsonFile.Write(path ?? FilePath, state);
    }
}
