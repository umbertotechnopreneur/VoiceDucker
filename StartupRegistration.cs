using Microsoft.Win32;
using Windows.ApplicationModel;

namespace VoiceDucker;

internal static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "VibeWare.VoiceDucker";
    private const string StartupTaskId = "VoiceDuckerStartup";
    private static readonly string PreferencePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "VibeWare", "VoiceDucker", "startup-preference-set");

    public static async Task InitializeDefaultAsync(bool hasSavedSettings)
    {
        if (File.Exists(PreferencePath))
        {
            return;
        }

        // Older installations with saved settings keep their current startup choice.
        if (!hasSavedSettings && !await IsEnabledAsync())
        {
            if (IsPackaged())
            {
                var task = await StartupTask.GetAsync(StartupTaskId);
                if (task.State == StartupTaskState.DisabledByUser)
                {
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
        return string.Equals(command, GetCommand(), StringComparison.OrdinalIgnoreCase);
    }

    public static async Task SetEnabledAsync(bool enabled)
    {
        if (IsPackaged())
        {
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

    private static string GetCommand()
    {
        var path = Environment.ProcessPath ??
                   throw new InvalidOperationException("The executable path is unavailable.");
        return $"\"{path}\"";
    }
}
