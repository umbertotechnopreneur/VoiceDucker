/* VBWR B
 * Project: VoiceDucker
 * Repository: https://github.com/umbertotechnopreneur/VoiceDucker
 * Creator: Umberto Giacobbi | https://umbertogiacobbi.biz
 * VibeWare: Human intent, AI execution, and plenty of tokens
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

/// <summary>
/// Validates saved audio preferences and reports unreadable files to the caller.
/// Loading defaults never enables microphone capture.
/// </summary>
internal static class SettingsStore
{
    #region Private fields

    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "VibeWare", "VoiceDucker", "settings.json");

    #endregion

    #region Public methods

    /// <summary>
    /// Loads validated preferences, returning defaults and an error when saved data is unreadable.
    /// </summary>
    public static DuckingSettings Load(out string? error) => Load(SettingsPath, out error);

    /// <summary>
    /// Loads validated preferences, returning defaults and an error when saved data is unreadable.
    /// </summary>
    internal static DuckingSettings Load(string settingsPath, out string? error)
    {
        error = null;
        if (!File.Exists(settingsPath))
        {
            return new DuckingSettings();
        }

        try
        {
            var settings = JsonSerializer.Deserialize<DuckingSettings>(File.ReadAllText(settingsPath));
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

    /// <summary>
    /// Validates preferences and atomically replaces the saved document.
    /// </summary>
    public static void Save(DuckingSettings settings) => Save(settings, SettingsPath);

    /// <summary>
    /// Validates preferences and atomically replaces the saved document.
    /// </summary>
    internal static void Save(DuckingSettings settings, string settingsPath)
    {
        if (!settings.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(settings));
        }

        AtomicJsonFile.Write(settingsPath, settings);
    }

    #endregion
}
