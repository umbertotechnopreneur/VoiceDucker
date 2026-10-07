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
            Publish(true, "Stopping is taking longer than expected. Spotify restoration is not confirmed.");
        }
    }

    public void Dispose()
    {
        Stop();
    }

    private void Run(ManualResetEventSlim stop)
    {
        SpotifySessions? spotify = null;
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
            spotify = new SpotifySessions();
            capture.StartRecording();

            while (!stop.Wait(150))
            {
                var speaking = gate.IsSpeaking;
                var found = spotify.Update(speaking);
                var message = !found
                    ? "Listening. Start playback in the Spotify desktop app."
                    : speaking
                        ? "Microphone sound detected. Spotify is 50% quieter."
                        : "Listening. Spotify is at its previous level.";
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
            if (spotify is not null)
            {
                try
                {
                    spotify.RestoreAll();
                }
                catch (Exception exception)
                {
                    error = $"Spotify volume could not be restored: {exception.Message}";
                }
                spotify.Dispose();
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
