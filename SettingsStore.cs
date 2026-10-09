/* VBWR B
 * Project: VoiceDucker
 * Repository: https://github.com/umbertotechnopreneur/VoiceDucker
 * Creator: Umberto Giacobbi | https://umbertogiacobbi.biz
 * VibeWare: Human intent. AI implementation. Accountable human review.
 * Manifesto: https://umbertogiacobbi.biz/vibeware/manifesto
 * Created with AI: See file history for implementation provenance.
 * Modified with AI: OpenAI Codex; source attribution and release preparation, 2026-10-09.
 * Human guidance: Umberto Giacobbi requested branding and the public 0.1.0 release.
 * Evidence: Git history and GitHub Actions; this header does not certify human review.
 * Copyright (c) 2026 Umberto Giacobbi
 * SPDX-License-Identifier: MIT
 * License: MIT - see LICENSE
 * VBWR E */

using System.Text.Json;
using VoiceDucker.Audio;

namespace VoiceDucker;

internal static class SettingsStore
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "VibeWare", "VoiceDucker", "settings.json");

    public static bool HasSavedSettings => File.Exists(SettingsPath);

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
