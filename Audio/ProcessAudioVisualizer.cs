/* VBWR B
 * Project: VoiceDucker
 * Creator: Umberto Giacobbi | https://umbertogiacobbi.biz
 * VibeWare: Human intent, AI execution, and plenty of tokens
 * Modified with AI: OpenAI Codex; process loopback visualizers, 2026-10-10.
 * Human guidance: Real per-app stereo meters and spectrum; in-memory only.
 * Copyright (c) 2026 Umberto Giacobbi
 * SPDX-License-Identifier: MIT
 * VBWR E */

using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using NAudio.Wasapi.CoreAudioApi.Interfaces;
using NAudio.Wave;

namespace VoiceDucker.Audio;

internal sealed class ProcessAudioVisualizer : IDisposable
{
    private readonly int _processId;
    private readonly CancellationTokenSource _stop = new();
    private AudioVisualizationFrame _frame = AudioVisualizationFrame.Waiting;
    private int _disposed;

    public ProcessAudioVisualizer(int processId)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(processId);
        _processId = processId;
        Completion = Task.Run(CaptureAsync);
    }

    public Task Completion { get; }
    public AudioVisualizationFrame Frame => Volatile.Read(ref _frame);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _stop.Cancel();
        _ = Completion.ContinueWith(_ => _stop.Dispose(), TaskScheduler.Default);
    }

    private async Task CaptureAsync()
    {
        AudioClient? client = null;
        var started = false;
        try
        {
            using var sampleReady = new AutoResetEvent(false);
            client = await ActivateAsync(_processId, _stop.Token);
            _stop.Token.ThrowIfCancellationRequested();
            // Microsoft process-loopback sample format; no physical endpoint or master volume changes.
            client.Initialize(AudioClientShareMode.Shared,
                AudioClientStreamFlags.Loopback | AudioClientStreamFlags.EventCallback |
                AudioClientStreamFlags.AutoConvertPcm,
                0, 0, new WaveFormat(AudioSpectrum.SampleRate, 16, 2), Guid.Empty);
            client.SetEventHandle(sampleReady.SafeWaitHandle.DangerousGetHandle());
            var capture = client.AudioCaptureClient;
            var samples = new short[client.BufferSize * 2];
            var analyzer = new AudioSpectrum();
            var waits = new WaitHandle[] { _stop.Token.WaitHandle, sampleReady };
            client.Start();
            started = true;
            while (WaitHandle.WaitAny(waits, 100) != 0)
            {
                while (!_stop.IsCancellationRequested && capture.GetNextPacketSize() > 0)
                {
                    var pointer = capture.GetBuffer(out var frames, out var flags);
                    try
                    {
                        if (frames == 0) continue;
                        if ((flags & AudioClientBufferFlags.DataDiscontinuity) != 0) analyzer.Reset();
                        var count = checked(frames * 2);
                        if (count > samples.Length) Array.Resize(ref samples, count);
                        if ((flags & AudioClientBufferFlags.Silent) != 0)
                            Array.Clear(samples, 0, count);
                        else
                            Marshal.Copy(pointer, samples, 0, count);
                        Volatile.Write(ref _frame, analyzer.Process(samples.AsSpan(0, count)));
                    }
                    finally
                    {
                        capture.ReleaseBuffer(frames);
                    }
                }
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        catch (Exception exception)
        {
            Volatile.Write(ref _frame, AudioVisualizationFrame.Waiting with
            {
                Error = $"Visualizer unavailable: {exception.Message}"
            });
        }
        finally
        {
            try { if (started) client?.Stop(); }
            catch (Exception) { /* Release still runs if the target process disappears. */ }
            client?.Dispose();
        }
    }

    // ABI follows the Windows SDK audioclientactivationparams.h and Microsoft's sample:
    // https://learn.microsoft.com/en-us/samples/microsoft/windows-classic-samples/applicationloopbackaudio-sample/
    private static async Task<AudioClient> ActivateAsync(int processId, CancellationToken cancellation)
    {
        var parameters = new ActivationParameters { Type = 1, ProcessId = (uint)processId, Mode = 0 };
        var memory = Marshal.AllocHGlobal(Marshal.SizeOf<ActivationParameters>());
        Marshal.StructureToPtr(parameters, memory, false);
        var handler = new ActivationHandler(memory);
        var variant = new ActivationVariant
        {
            VariantType = 65, // VT_BLOB
            Blob = new ActivationBlob { Size = (uint)Marshal.SizeOf<ActivationParameters>(), Data = memory }
        };
        var iid = typeof(IAudioClient).GUID;
        IActivateAudioInterfaceAsyncOperation? operation = null;
        try
        {
            Marshal.ThrowExceptionForHR(ActivateAudioInterfaceAsync(@"VAD\Process_Loopback",
                ref iid, ref variant, handler, out operation));
            return await handler.Result.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellation);
        }
        catch
        {
            handler.Abandon();
            if (operation is null) handler.FreeParameters();
            throw;
        }
        finally
        {
            if (operation is not null) Marshal.ReleaseComObject(operation);
            GC.KeepAlive(handler);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ActivationParameters { public uint Type; public uint ProcessId; public uint Mode; }
    [StructLayout(LayoutKind.Sequential)]
    private struct ActivationBlob { public uint Size; public IntPtr Data; }
    [StructLayout(LayoutKind.Sequential)]
    private struct ActivationVariant
    {
        public ushort VariantType;
        public ushort Reserved1, Reserved2, Reserved3;
        public ActivationBlob Blob;
    }

    [DllImport("Mmdevapi.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern int ActivateAudioInterfaceAsync(string deviceInterfacePath,
        ref Guid iid, ref ActivationVariant activationParameters,
        IActivateAudioInterfaceCompletionHandler completionHandler,
        out IActivateAudioInterfaceAsyncOperation operation);

    [ComImport, Guid("94ea2b94-e9cc-49e0-c0ff-ee64ca8f5b90"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAgileCompletion { }

    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    private sealed class ActivationHandler(IntPtr parameters) :
        IActivateAudioInterfaceCompletionHandler, IAgileCompletion
    {
        private readonly object _lock = new();
        private IntPtr _parameters = parameters;
        private bool _abandoned;
        public TaskCompletionSource<AudioClient> Result { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void ActivateCompleted(IActivateAudioInterfaceAsyncOperation operation)
        {
            try
            {
                operation.GetActivateResult(out var result, out var native);
                if (result < 0)
                {
                    if (native is not null) Marshal.ReleaseComObject(native);
                    Marshal.ThrowExceptionForHR(result);
                }
                var client = new AudioClient((IAudioClient)(native ??
                    throw new InvalidOperationException("Windows did not return an audio client.")));
                lock (_lock)
                {
                    if (_abandoned) client.Dispose();
                    else Result.TrySetResult(client);
                }
            }
            catch (Exception exception) { Result.TrySetException(exception); }
            finally { FreeParameters(); }
        }

        public void Abandon()
        {
            lock (_lock)
            {
                _abandoned = true;
                if (Result.Task.IsCompletedSuccessfully) Result.Task.Result.Dispose();
            }
        }

        public void FreeParameters()
        {
            var memory = Interlocked.Exchange(ref _parameters, IntPtr.Zero);
            if (memory != IntPtr.Zero) Marshal.FreeHGlobal(memory);
        }
    }
}
