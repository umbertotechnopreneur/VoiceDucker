// VoiceDucker - Copyright (c) 2026 Umberto Giacobbi. MIT license.

using System.Runtime.InteropServices;
using NAudio.CoreAudioApi.Interfaces;

namespace VoiceDucker.Audio;

/// <summary>
/// Uses Core Audio directly where NAudio's convenience wrappers omit EventContext.
/// The audio worker owns and disposes each session's COM references.
/// </summary>
internal sealed class NativePlaybackSession : IDisposable
{
    #region Private fields

    private const uint InProcessServer = 1;
    private static readonly Guid DeviceEnumeratorClassId = new("BCDE0395-E52F-467C-8E3D-C4579291692E");
    private readonly IAudioSessionControl _control;
    private readonly IContextAudioVolume _volume;
    private readonly PlaybackVolumeEvents.VolumeEventSink? _sink;
    private bool _disposed;

    #endregion

    #region Initialization

    /// <summary>Takes ownership of a session control and registers its optional notification sink.</summary>
    private NativePlaybackSession(IAudioSessionControl control, PlaybackVolumeEvents.VolumeEventSink? sink)
    {
        _control = control;
        _volume = (IContextAudioVolume)control;
        _sink = sink;
        if (sink is not null)
        {
            Marshal.ThrowExceptionForHR(control.RegisterAudioSessionNotification(sink));
        }
    }

    #endregion

    #region Public methods

    /// <summary>
    /// Opens one exact endpoint/session instance and transfers only its control reference
    /// to the returned object. All temporary enumeration references are released.
    /// </summary>
    public static NativePlaybackSession Open(string endpointId, string instanceId,
        PlaybackVolumeEvents? events = null)
    {
        var devices = (IMMDeviceEnumerator)Activator.CreateInstance(
            Type.GetTypeFromCLSID(DeviceEnumeratorClassId, throwOnError: true)!)!;
        IMMDevice? device = null;
        object? managerObject = null;
        IAudioSessionEnumerator? sessions = null;

        try
        {
            Marshal.ThrowExceptionForHR(devices.GetDevice(endpointId, out device));
            var managerId = typeof(IAudioSessionManager2).GUID;
            Marshal.ThrowExceptionForHR(device.Activate(ref managerId, InProcessServer, IntPtr.Zero, out managerObject));
            var manager = (IAudioSessionManager2)managerObject;
            Marshal.ThrowExceptionForHR(manager.GetSessionEnumerator(out sessions));
            Marshal.ThrowExceptionForHR(sessions.GetCount(out var count));

            for (var index = 0; index < count; index++)
            {
                IAudioSessionControl? control = null;
                try
                {
                    Marshal.ThrowExceptionForHR(sessions.GetSession(index, out control));
                    Marshal.ThrowExceptionForHR(((IAudioSessionControl2)control).GetSessionInstanceIdentifier(out var id));
                    if (id == instanceId)
                    {
                        var result = new NativePlaybackSession(control, events?.CreateSink(endpointId + ":" + id));
                        control = null; // The returned session owns this reference.
                        return result;
                    }
                }
                finally
                {
                    Release(control);
                }
            }

            throw new InvalidOperationException("The playback session ended before it could be opened.");
        }
        finally
        {
            Release(sessions);
            Release(managerObject);
            Release(device);
            Release(devices);
        }
    }

    /// <summary>Clamps and tags a session-volume write, skipping an already-matching native value.</summary>
    public void SetVolume(float volume, Guid eventContext)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var target = Math.Clamp(volume, 0, 1);
        if (ReadVolume() == target) return;

        Marshal.ThrowExceptionForHR(_volume.SetMasterVolume(target, eventContext));
    }

    /// <summary>Reads the current session-volume scalar and propagates native failures.</summary>
    public float ReadVolume()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Marshal.ThrowExceptionForHR(_volume.GetMasterVolume(out var volume));

        return volume;
    }

    /// <summary>Sets the session mute state with an explicit event context.</summary>
    public void SetMute(bool muted, Guid eventContext)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Marshal.ThrowExceptionForHR(_volume.SetMute(muted, eventContext));
    }

    /// <summary>Deactivates callbacks, unregisters the sink and releases its control reference once.</summary>
    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;
        _sink?.Deactivate();
        try
        {
            if (_sink is not null)
            {
                Marshal.ThrowExceptionForHR(_control.UnregisterAudioSessionNotification(_sink));
            }
        }
        finally
        {
            Release(_control);
        }
    }

    /// <summary>Attempts every session cleanup and reports all failures after the full collection.</summary>
    public static void DisposeAll(IEnumerable<NativePlaybackSession> sessions)
    {
        List<Exception>? failures = null;
        foreach (var session in sessions)
        {
            try
            {
                session.Dispose();
            }
            catch (Exception exception)
            {
                (failures ??= []).Add(exception);
            }
        }

        if (failures is not null) throw new AggregateException("Playback session cleanup failed.", failures);
    }

    #endregion

    #region Implementation

    /// <summary>Releases a temporary COM wrapper when one was successfully obtained.</summary>
    private static void Release(object? value)
    {
        if (value is not null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value);
    }

    #endregion

    #region Native declarations

    // Only the vtable prefix used here is declared for these private Windows interfaces.
    /// <summary>Endpoint enumeration ABI prefix; declaration order must match Windows.</summary>
    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        /// <summary>Enumerates endpoints matching direction and state.</summary>
        [PreserveSig]
        int EnumAudioEndpoints(int dataFlow, uint stateMask, out IntPtr devices);

        /// <summary>Resolves the default endpoint for a Windows role.</summary>
        [PreserveSig]
        int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);

        /// <summary>Resolves an endpoint by its persisted identifier.</summary>
        [PreserveSig]
        int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
    }

    /// <summary>Endpoint activation ABI prefix used to obtain its session manager.</summary>
    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        /// <summary>Activates the requested endpoint interface.</summary>
        [PreserveSig]
        int Activate(ref Guid interfaceId, uint context, IntPtr parameters,
            [MarshalAs(UnmanagedType.IUnknown)] out object result);
    }

    /// <summary>Session-manager ABI prefix through session enumeration.</summary>
    [ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionManager2
    {
        /// <summary>Obtains control for a session grouping identifier.</summary>
        [PreserveSig]
        int GetAudioSessionControl([MarshalAs(UnmanagedType.LPStruct)] Guid sessionId,
            uint flags, out IAudioSessionControl control);

        /// <summary>Obtains the scalar volume interface for a session grouping identifier.</summary>
        [PreserveSig]
        int GetSimpleAudioVolume([MarshalAs(UnmanagedType.LPStruct)] Guid sessionId,
            uint flags, out IContextAudioVolume volume);

        /// <summary>Obtains the current session-instance enumerator.</summary>
        [PreserveSig]
        int GetSessionEnumerator(out IAudioSessionEnumerator sessions);
    }

    /// <summary>Enumerates session controls by index with their native HRESULT results.</summary>
    [ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionEnumerator
    {
        /// <summary>Reads the number of available session controls.</summary>
        [PreserveSig]
        int GetCount(out int count);

        /// <summary>Obtains a session-control reference at the requested index.</summary>
        [PreserveSig]
        int GetSession(int index, out IAudioSessionControl control);
    }

    // NAudio's ISimpleAudioVolume is internal; this is the Windows ABI, in vtable order.
    /// <summary>Session scalar volume/mute ABI with EventContext preserved on writes.</summary>
    [ComImport]
    [Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IContextAudioVolume
    {
        /// <summary>Sets this session's volume; this is not the system endpoint master volume.</summary>
        [PreserveSig]
        int SetMasterVolume(float volume, [MarshalAs(UnmanagedType.LPStruct)] Guid eventContext);

        /// <summary>Reads this session's current scalar volume.</summary>
        [PreserveSig]
        int GetMasterVolume(out float volume);

        /// <summary>Sets this session's mute state and identifies the writer.</summary>
        [PreserveSig]
        int SetMute([MarshalAs(UnmanagedType.Bool)] bool muted,
            [MarshalAs(UnmanagedType.LPStruct)] Guid eventContext);

        /// <summary>Reads this session's current mute state.</summary>
        [PreserveSig]
        int GetMute([MarshalAs(UnmanagedType.Bool)] out bool muted);
    }

    #endregion
}
