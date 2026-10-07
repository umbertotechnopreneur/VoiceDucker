using System.Diagnostics;

namespace VoiceDucker.Audio;

internal static class PlaybackIdentity
{
    public static string ProcessName(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return process.ProcessName;
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }
}
