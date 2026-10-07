/* VBWR B
 * Project: VoiceDucker
 * Repository: https://github.com/umbertotechnopreneur/VoiceDucker
 * Creator: Umberto Giacobbi | https://umbertogiacobbi.biz
 * VibeWare initiative: Human intent. AI implementation. Accountable human review.
 * Manifesto: https://umbertogiacobbi.biz/vibeware/manifesto
 * Created with AI: OpenAI Codex assisted the initial implementation; Git history
 * Modified with AI: OpenAI Codex; all audible playback sessions, 2026-10-08
 * Human guidance: Umberto Giacobbi defined the purpose and audio behavior
 * Evidence: Git history
 * Copyright (c) 2026 Umberto Giacobbi
 * License: MIT
 * VBWR E */

using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace VoiceDucker.Audio;

public sealed class DuckingEngine : IDisposable
{
    private readonly object _stateLock = new();
    private Thread? _worker;
    private ManualResetEventSlim? _stop;
    private string? _lastMessage;

    public event Action<EngineStatus>? StatusChanged;

    public bool IsRunning
    {
        get
        {
            lock (_stateLock)
            {
                return _worker?.IsAlive == true;
            }
        }
    }

    public void Start()
    {
        Thread worker;
        lock (_stateLock)
        {
            if (_worker?.IsAlive == true)
            {
                return;
            }

            _stop?.Dispose();
            _stop = new ManualResetEventSlim(false);
            _lastMessage = null;
            var stop = _stop;
            worker = new Thread(() => Run(stop))
            {
                IsBackground = true,
                Name = "VoiceDucker audio"
            };
            _worker = worker;
        }

        Publish(true, "Starting microphone...");
        worker.Start();
    }

    public void Stop()
    {
        Thread? worker;
        lock (_stateLock)
        {
            worker = _worker;
            _stop?.Set();
        }

        if (worker is not null && worker.IsAlive && !worker.Join(TimeSpan.FromSeconds(5)))
        {
            Publish(true, "Stopping is taking longer than expected. Audio restoration is not confirmed.");
        }
    }

    public void Dispose()
    {
        Stop();
    }

    private void Run(ManualResetEventSlim stop)
    {
        PlaybackSessions? playback = null;
        string? error = null;
        try
        {
            using var devices = new MMDeviceEnumerator();
            using var microphone = devices.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
            using var capture = new WasapiCapture(microphone);
            if (!MicrophoneLevel.IsSupported(capture.WaveFormat))
            {
                throw new NotSupportedException($"Unsupported microphone format: {capture.WaveFormat}");
            }

            var gate = new VoiceGate();
            capture.DataAvailable += (_, args) =>
            {
                var audio = args.Buffer.AsSpan(0, args.BytesRecorded);
                gate.Observe(MicrophoneLevel.Rms(audio, capture.WaveFormat));
            };
            playback = new PlaybackSessions();
            capture.StartRecording();

            while (!stop.Wait(150))
            {
                var speaking = gate.IsSpeaking;
                var lowered = playback.Update(speaking);
                var message = speaking && lowered > 0
                    ? $"Microphone sound detected. {lowered} playing audio session(s) are 50% quieter."
                    : speaking
                        ? "Microphone sound detected. No playing audio to lower."
                        : "Listening. Playback is at its previous level.";
                Publish(true, message);
            }

            capture.StopRecording();
        }
        catch (Exception exception)
        {
            error = $"Stopped: {exception.Message}";
        }
        finally
        {
            if (playback is not null)
            {
                try
                {
                    playback.RestoreAll();
                }
                catch (Exception exception)
                {
                    error = $"Audio volume could not be restored: {exception.Message}";
                }
                playback.Dispose();
            }

            Publish(false, error ?? "Off. Your microphone is not in use.");
        }
    }

    private void Publish(bool running, string message)
    {
        lock (_stateLock)
        {
            if (_lastMessage == message)
            {
                return;
            }
            _lastMessage = message;
        }

        StatusChanged?.Invoke(new EngineStatus(running, message));
    }
}
