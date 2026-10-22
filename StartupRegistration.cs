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

using Microsoft.Win32;
using Windows.ApplicationModel;

namespace VoiceDucker;

/// <summary>
/// Uses StartupTask for an installed MSIX and the Run key for a portable build.
/// The preference marker prevents first-run defaults from overriding later choices.
/// </summary>
internal static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "VibeWare.VoiceDucker";
    private const string StartupTaskId = "VoiceDuckerStartup";
    private static readonly string PreferencePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "VibeWare", "VoiceDucker", "startup-preference-set");

    public static async Task InitializeDefaultAsync()
    {
        if (IsPackaged())
        {
            // A prior portable build may have left an HKCU Run entry that points
            // to a build folder. Package identity owns startup for MSIX, so never
            // leave two launch mechanisms for the same app.
            RemoveLegacyRunEntry();
        }

        if (File.Exists(PreferencePath))
        {
            return;
        }

        if (!await IsEnabledAsync())
        {
            if (IsPackaged())
            {
                var task = await StartupTask.GetAsync(StartupTaskId);
                if (task.State == StartupTaskState.DisabledByUser)
                {
                    // This is a current Windows choice, not a migration rule.
                    SavePreferenceMarker();
                    return;
                }
            }
            await SetEnabledAsync(true);
        }

        SavePreferenceMarker();
    }

    public static void SavePreferenceMarker()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(PreferencePath)!);
        File.WriteAllText(PreferencePath, string.Empty);
    }

    public static async Task<bool> IsEnabledAsync()
    {
        if (IsPackaged())
        {
            var task = await StartupTask.GetAsync(StartupTaskId);
            return task.State == StartupTaskState.Enabled;
        }

        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        var command = key?.GetValue(ValueName) as string;
        return command?.StartsWith(GetExecutableCommand(), StringComparison.OrdinalIgnoreCase) == true;
    }

    public static async Task SetEnabledAsync(bool enabled)
    {
        if (IsPackaged())
        {
            RemoveLegacyRunEntry();
            var task = await StartupTask.GetAsync(StartupTaskId);
            if (enabled)
            {
                if (await task.RequestEnableAsync() != StartupTaskState.Enabled)
                {
                    throw new InvalidOperationException("Windows did not enable startup. Check Startup apps in Settings.");
                }
            }
            else
            {
                task.Disable();
            }
            return;
        }

        if (enabled)
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            key.SetValue(ValueName, GetCommand(), RegistryValueKind.String);
        }
        else
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, true);
            key?.DeleteValue(ValueName, false);
        }
    }

    private static void RemoveLegacyRunEntry()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, true);
        key?.DeleteValue(ValueName, false);
    }

    private static bool IsPackaged()
    {
        try
        {
            _ = Package.Current.Id.FamilyName;
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static string GetCommand() => GetExecutableCommand();

    private static string GetExecutableCommand()
    {
        var path = Environment.ProcessPath ??
                   throw new InvalidOperationException("The executable path is unavailable.");
        return $"\"{path}\"";
    }
}
