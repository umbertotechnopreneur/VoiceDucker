// VoiceDucker — Copyright (c) 2026 Umberto Giacobbi. MIT license.

namespace VoiceDucker;

internal sealed class SingleInstanceService : IDisposable
{
    private readonly Mutex _mutex;
    private readonly EventWaitHandle _activation;
    private RegisteredWaitHandle? _registration;
    private int _disposed;

    public bool IsPrimary { get; }

    public SingleInstanceService(string name = @"Local\VibeWare.VoiceDucker")
    {
        // A repeat launch can signal this before the primary window is ready.
        // AutoReset retains that request until the primary registers its listener.
        _activation = new EventWaitHandle(false, EventResetMode.AutoReset, name + ".Activate");
        try
        {
            _mutex = new Mutex(false, name);
            try
            {
                IsPrimary = _mutex.WaitOne(0);
            }
            catch (AbandonedMutexException)
            {
                // Windows granted ownership after the previous owner crashed.
                IsPrimary = true;
            }
        }
        catch
        {
            _activation.Dispose();
            throw;
        }
    }

    public void Listen(Action activateWindow)
    {
        if (!IsPrimary || _registration is not null)
            throw new InvalidOperationException("Only the primary instance can register one activation listener.");
        _registration = ThreadPool.RegisterWaitForSingleObject(_activation,
            (_, _) =>
            {
                if (Volatile.Read(ref _disposed) == 0) activateWindow();
            }, null, Timeout.Infinite, executeOnlyOnce: false);
    }

    public void RequestActivation() => _activation.Set();

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _registration?.Unregister(null);
        _activation.Dispose();
        // Ownership belongs to the UI thread, which also closes the application.
        if (IsPrimary) _mutex.ReleaseMutex();
        _mutex.Dispose();
    }
}
