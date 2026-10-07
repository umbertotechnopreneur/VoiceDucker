using System.Text.Json;
using VoiceDucker.Audio;

namespace VoiceDucker;

internal static class SettingsStore
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "VibeWare", "VoiceDucker", "settings.json");

    public static DuckingSettings Load(out string? error)
    {
        error = null;
        if (!File.Exists(SettingsPath))
        {
            return new DuckingSettings();
        }

        try
        {
            var settings = JsonSerializer.Deserialize<DuckingSettings>(File.ReadAllText(SettingsPath));
            if (settings is null || !settings.IsValid)
            {
                throw new InvalidDataException("Settings contain a value outside its allowed range.");
            }

            return settings;
        }
        catch (Exception exception)
        {
            error = $"Settings could not be loaded: {exception.Message}";
            return new DuckingSettings();
        }
    }

    public static void Save(DuckingSettings settings)
    {
        if (!settings.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(settings));
        }

        var directory = Path.GetDirectoryName(SettingsPath)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = SettingsPath + ".tmp";
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(settings));
            File.Move(temporaryPath, SettingsPath, true);
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
