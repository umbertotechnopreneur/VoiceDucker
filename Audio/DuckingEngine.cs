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

internal sealed class DuckingEngine : IDisposable
{
    private readonly object _stateLock = new();
    private Thread? _worker;
    private ManualResetEventSlim? _stop;
    private DuckingSettings _settings;
    private EngineStatus? _lastStatus;
    private double _microphoneLevel;

    public DuckingEngine(DuckingSettings settings)
    {
        _settings = settings.IsValid ? settings : throw new ArgumentOutOfRangeException(nameof(settings));
    }

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

    public void UpdateSettings(DuckingSettings settings)
    {
        if (!settings.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(settings));
        }
        Volatile.Write(ref _settings, settings);
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
            _lastStatus = null;
            var stop = _stop;
            worker = new Thread(() => Run(stop))
            {
                IsBackground = true,
                Name = "VoiceDucker audio"
            };
            _worker = worker;
        }

        Publish(true, false, 0, 0, "Starting microphone...");
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
            Publish(true, false, 0, 0,
                "Stopping is taking longer than expected. Audio restoration is not confirmed.");
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
                var level = MicrophoneLevel.Rms(audio, capture.WaveFormat);
                Volatile.Write(ref _microphoneLevel, level);
                gate.Observe(level);
            };
            playback = new PlaybackSessions();
            capture.StartRecording();

            while (!stop.Wait(50))
            {
                var speaking = gate.IsSpeaking;
                var settings = Volatile.Read(ref _settings);
                var lowered = playback.Update(speaking, settings);
                var message = speaking && lowered > 0
                    ? $"Microphone sound detected. Reducing {lowered} playing session(s) by up to {settings.ReductionPercent}%."
                    : speaking
                        ? settings.IncludeOtherSources
                            ? "Microphone sound detected. No playing audio to lower."
                            : "Microphone sound detected. No Spotify playback to lower."
                        : playback.HasOwnedSessions
                            ? "Listening. Playback is returning to its previous level."
                            : "Listening. Playback is at its previous level.";
                var visualLevel = Math.Round(Math.Clamp(
                    Volatile.Read(ref _microphoneLevel) / 0.08, 0, 1) * 20) / 20;
                Publish(true, speaking, lowered, visualLevel, message);
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

            Volatile.Write(ref _microphoneLevel, 0);
            Publish(false, false, 0, 0, error ?? "Off. Your microphone is not in use.");
        }
    }

    private void Publish(bool running, bool speaking, int affectedSessions,
        double microphoneLevel, string message)
    {
        var status = new EngineStatus(running, speaking, affectedSessions, microphoneLevel, message);
        lock (_stateLock)
        {
            if (_lastStatus == status)
            {
                return;
            }
            _lastStatus = status;
        }

        StatusChanged?.Invoke(status);
    }
}
