// VoiceDucker - Copyright (c) 2026 Umberto Giacobbi. MIT license.

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Windows.AppLifecycle;

namespace VoiceDucker;

/// <summary>
/// Samples private process memory on a dedicated thread and recovers without the XAML dispatcher.
/// No microphone samples, user content or telemetry are collected.
/// </summary>
internal sealed class MemoryGuard : IDisposable
{
    internal const long LimitBytes = 1024L * 1024 * 1024;
    private static readonly TimeSpan SampleInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan RestartCooldown = TimeSpan.FromMinutes(10);
    private static readonly string DirectoryPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VibeWare", "VoiceDucker");
    private readonly ManualResetEventSlim _stop = new(false);
    private readonly object _stopSync = new();
    private readonly Func<bool> _stopAudio;
    private readonly Thread _thread;
    private int _disposed;
    private bool _threadFinished;

    /// <summary>Starts lifetime sampling; the callback must stop audio without UI dispatch.</summary>
    /// <param name="stopAudio">Returns true only when the audio worker confirms restoration.</param>
    public MemoryGuard(Func<bool> stopAudio)
    {
        _stopAudio = stopAudio;
        _thread = new Thread(Run) { IsBackground = true, Name = "VoiceDucker memory guard" };
        _thread.SetApartmentState(ApartmentState.MTA);
        _thread.Start();
    }

    /// <summary>Stops sampling when normal application shutdown begins.</summary>
    public void Dispose()
    {
        lock (_stopSync)
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0 && !_threadFinished)
            {
                _stop.Set();
            }
        }
    }

    /// <summary>Reads OS process counters independently of the UI and audio workers.</summary>
    private void Run()
    {
        using var process = Process.GetCurrentProcess();
        var samplingErrorReported = false;
        try
        {
            while (!_stop.Wait(SampleInterval))
            {
                long privateBytes;
                try
                {
                    process.Refresh();
                    privateBytes = process.PrivateMemorySize64;
                    samplingErrorReported = false;
                }
                catch (Exception exception)
                {
                    if (!samplingErrorReported) WriteLog($"sampling-failed error={exception}");
                    samplingErrorReported = true;
                    continue;
                }

                if (privateBytes > LimitBytes)
                {
                    Recover(privateBytes);
                    return;
                }
            }
        }
        finally
        {
            lock (_stopSync)
            {
                _threadFinished = true;
                _stop.Dispose();
            }
        }
    }

    /// <summary>Warns, stops audio with its bounded worker shutdown, then requests an OS restart.</summary>
    private void Recover(long privateBytes)
    {
        var restart = ReserveRestart(out var reason);
        WriteLog($"limit-exceeded privateBytes={privateBytes} limitBytes={LimitBytes} restart={restart} reason={reason}");
        var action = restart
            ? "VoiceDucker will restart automatically."
            : "VoiceDucker will close without restarting to prevent a restart loop. Please reopen it after checking the problem.";
        ShowWarning($"VoiceDucker is using more than 1 GB of private memory.\n\n{action}\n\n"
            + "Microphone capture will stop and startup playback volumes will be restored where possible. Your saved settings will be kept.");

        // The native warning uses its own thread, so neither a frozen UI nor an unanswered
        // dialog can prevent recovery. It stays visible for at least this grace interval.
        if (_stop.Wait(TimeSpan.FromSeconds(5))) return;

        var restored = false;
        try { restored = _stopAudio(); }
        catch (Exception exception) { WriteLog($"audio-shutdown-failed error={exception}"); }
        WriteLog($"audio-shutdown restoration={(restored ? "confirmed" : "unconfirmed")}");
        if (_stop.IsSet) return;

        if (!restored)
        {
            ShowWarning("VoiceDucker could not confirm playback volume restoration before recovery.\n\n"
                + "Please check your apps' playback volumes. " + action);
            if (_stop.Wait(TimeSpan.FromSeconds(5))) return;
        }

        if (restart)
        {
            try
            {
                // Windows terminates this instance before relaunch, preserving package identity
                // and avoiding a race with our single-instance mutex. Success does not return.
                var failure = AppInstance.Restart("--memory-recovery");
                WriteLog($"restart-failed reason={failure}");
            }
            catch (Exception exception) { WriteLog($"restart-failed error={exception}"); }

            ShowWarning("VoiceDucker could not restart automatically and will now close. Please reopen it manually.");
            if (_stop.Wait(TimeSpan.FromSeconds(5))) return;
        }

        // UI shutdown may be exactly what is broken. Audio cleanup has already had its
        // bounded opportunity; do not leave the runaway process alive waiting for XAML.
        Environment.Exit(1);
    }

    /// <summary>Persists one restart allowance per ten minutes, including across manual launches.</summary>
    private static bool ReserveRestart(out string reason)
    {
        try
        {
            var path = Path.Combine(DirectoryPath, "memory-recovery.json");
            var now = DateTimeOffset.UtcNow;
            if (File.Exists(path))
            {
                var previous = JsonSerializer.Deserialize<DateTimeOffset>(File.ReadAllText(path));
                if (now - previous < RestartCooldown)
                {
                    reason = "restart-cooldown";
                    return false;
                }
            }

            AtomicJsonFile.Write(path, now);
            reason = "restart-reserved";
            return true;
        }
        catch (Exception exception)
        {
            // Without a durable marker, do not risk an unlimited automatic restart loop.
            WriteLog($"restart-marker-failed error={exception}");
            reason = "restart-marker-unavailable";
            return false;
        }
    }

    /// <summary>Writes a small local recovery record even when optional audio diagnostics are off.</summary>
    private static void WriteLog(string message)
    {
        try
        {
            Directory.CreateDirectory(DirectoryPath);
            var path = Path.Combine(DirectoryPath, "memory-recovery.log");
            if (File.Exists(path) && new FileInfo(path).Length > 64 * 1024)
                File.Move(path, path + ".previous", overwrite: true);
            File.AppendAllText(path, $"{DateTimeOffset.UtcNow:O} pid={Environment.ProcessId} {message}{Environment.NewLine}");
        }
        catch (Exception)
        {
            // A full disk or locked log must not prevent the warning and recovery.
        }
    }

    /// <summary>Shows an OS warning even if the XAML dispatcher has stopped responding.</summary>
    private static void ShowWarning(string text)
    {
        var warning = new Thread(() =>
        {
            _ = MessageBox(IntPtr.Zero, text, "VoiceDucker memory protection", 0x00040000 | 0x00010000 | 0x30);
        }) { IsBackground = true, Name = "VoiceDucker recovery notice" };
        warning.SetApartmentState(ApartmentState.STA);
        warning.Start();
    }

    /// <summary>Displays a native, topmost warning without depending on the application window.</summary>
    [DllImport("user32.dll", EntryPoint = "MessageBoxW", CharSet = CharSet.Unicode)]
    private static extern int MessageBox(IntPtr owner, string text, string caption, uint flags);
}
