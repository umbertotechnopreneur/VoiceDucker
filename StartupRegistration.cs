using Microsoft.Win32;

namespace VoiceDucker;

internal static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "VibeWare.VoiceDucker";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        var command = key?.GetValue(ValueName) as string;
        return string.Equals(command, GetCommand(), StringComparison.OrdinalIgnoreCase);
    }

    public static void SetEnabled(bool enabled)
    {
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

    private static string GetCommand()
    {
        var path = Environment.ProcessPath ??
                   throw new InvalidOperationException("The executable path is unavailable.");
        return $"\"{path}\"";
    }
}
