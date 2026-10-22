// VoiceDucker - Copyright (c) 2026 Umberto Giacobbi. MIT license.

using System.Collections.Concurrent;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace VoiceDucker.Audio;

/// <summary>
/// One app-lifetime worker owns playback COM, microphone capture, gate evaluation and commands.
/// UI callers exchange commands and immutable snapshots, never native audio objects.
/// </summary>
internal sealed class DuckingEngine : IDisposable
{
    private readonly ConcurrentQueue<Command> _commands = new();
    private readonly ConcurrentQueue<CaptureFrame> _frames = new();
    private readonly AutoResetEvent _wake = new(false);
    private readonly object _commandSync = new();
    private readonly Thread _worker;
    private readonly PlaybackVolumeEvents _volumeEvents = new();
    private readonly VoiceGate _gate = new();
    private DuckingSettings _settings;
    private EngineStatus _state;
    private PlaybackSessions? _playback;
    private WasapiCapture? _capture;
    private MMDeviceEnumerator? _captureDevices;
    private MMDevice? _captureDevice;
    private CapturedMicrophoneSample? _latestSample;
    private CaptureFailure? _captureFailure;
    private GateState _detection;
    private IReadOnlyList<MicrophoneChoice> _microphones = [];
    private string _microphoneName = string.Empty;
    private string? _error;
    private long _sampleSequence;
    private int _captureGeneration;
    private int _frameCount;
    private int _disposed;
    private bool _ready;
    private bool _running;
    private bool _exit;
    private bool _workerStopped;

    public DuckingEngine(DuckingSettings settings)
    {
        _settings = settings.IsValid ? settings : throw new ArgumentOutOfRangeException(nameof(settings));
        _detection = GateState.Off(settings);
        _state = EngineStatus.Initial(settings);
        _worker = new Thread(Run) { IsBackground = true, Name = "VoiceDucker audio" };
        _worker.Start();
        AudioDiagnostics.Write(() => "engine-ready capture=off worker=app-lifetime");
    }

    public event Action<EngineStatus>? StatusChanged;
    public EngineStatus State => Volatile.Read(ref _state);

    public Task InitializeAsync() => Enqueue(() =>
    {
        if (_ready) return;
        _playback ??= new PlaybackSessions(new NativePlaybackSource(_volumeEvents), _volumeEvents);
        if (!_playback.Update(false, false, _settings))
            throw new InvalidOperationException("The startup playback volume snapshot could not be captured.");
        _ready = true;
        RefreshMicrophones();
        AudioDiagnostics.Write(() => "startup-volume-snapshot capture=off");
    });

    public Task SetMonitoringAsync(bool enabled) => Enqueue(() =>
    {
        if (enabled)
        {
            if (!_ready) throw new InvalidOperationException("Playback initialization has not completed.");
            if (_running) return;
            _error = null;
            StartCapture();
            _playback!.MonitoringEnabled();
        }
        else
        {
            StopCapture();
            _playback?.RequestStartupRestoration();
            AudioDiagnostics.Write(() => "monitoring-disabled restoration=requested");
        }
    });

    public Task UpdateSettingsAsync(DuckingSettings settings) => Enqueue(() =>
    {
        if (!settings.IsValid) throw new ArgumentOutOfRangeException(nameof(settings));
        var changed = settings != _settings;
        var restart = _running && settings.MicrophoneDeviceId != _settings.MicrophoneDeviceId;
        if (restart) StopCapture();
        _settings = settings;
        if (changed) LogSettings("settings-applied");
        if (!_running) _detection = GateState.Off(settings);
        if (restart)
        {
            try { StartCapture(); }
            catch
            {
                _playback?.RequestStartupRestoration();
                throw;
            }
        }
        else if (!_running)
        {
            _microphoneName = _microphones.FirstOrDefault(choice => choice.Id == settings.MicrophoneDeviceId)?.Name
                ?? "Unavailable microphone — choose another input";
        }
    });

    public Task SetNormalVolumeAsync(string key, float normal)
    {
        var intentVersion = _volumeEvents.NextIntentVersion();
        return Enqueue(() =>
        {
            if (!_ready) throw new InvalidOperationException("Playback initialization has not completed.");
            _playback!.SetNormalVolume(key, normal, intentVersion);
        });
    }

    public async Task SetPlaybackVolumeAsync(string key, float actual)
    {
        var intentVersion = _volumeEvents.NextIntentVersion();
        Task applied = Task.CompletedTask;
        await Enqueue(() =>
        {
            if (!_ready) throw new InvalidOperationException("Playback initialization has not completed.");
            applied = _playback!.SetPlaybackVolume(key, actual, intentVersion);
        }).ConfigureAwait(false);
        await applied.ConfigureAwait(false);
    }

    public Task RefreshMicrophonesAsync() => Enqueue(RefreshMicrophones);

    private Task Enqueue(Action action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_commandSync)
        {
            if (_disposed != 0 || _workerStopped)
                return Task.FromException(new ObjectDisposedException(nameof(DuckingEngine)));
            _commands.Enqueue(new Command(action, completion));
        }
        Signal();
        return completion.Task;
    }

    private void Run()
    {
        var completed = new List<TaskCompletionSource>();
        try
        {
            while (!_exit)
            {
                completed.Clear();
                while (_commands.TryDequeue(out var command))
                {
                    try
                    {
                        if (_exit) throw new ObjectDisposedException(nameof(DuckingEngine));
                        command.Action();
                        completed.Add(command.Completion);
                    }
                    catch (Exception exception)
                    {
                        // Command failures belong to their awaiting application-model command.
                        // They must not become a permanent capture fault in later snapshots.
                        command.Completion.TrySetException(exception);
                    }
                }
                if (!_exit) Tick();
                foreach (var completion in completed) completion.TrySetResult();
                completed.Clear();
                if (!_exit) _wake.WaitOne(50);
            }
        }
        catch (Exception exception)
        {
            _error = $"The audio worker stopped unexpectedly: {exception.Message}";
        }
        finally
        {
            lock (_commandSync) _workerStopped = true;
            foreach (var completion in completed)
                completion.TrySetException(new InvalidOperationException(_error ?? "The audio worker stopped."));
            try { ShutdownAudio(); }
            catch (Exception exception)
            {
                _error = $"Audio shutdown could not complete: {exception.Message}";
                AudioDiagnostics.Write(() => $"engine-shutdown-error restoration=unconfirmed error={exception.Message}");
            }
            while (_commands.TryDequeue(out var command))
                command.Completion.TrySetException(new ObjectDisposedException(nameof(DuckingEngine)));
            AudioDiagnostics.Write(() => $"engine-disposed worker=stopped result={_error ?? "restoration-complete"}");
            _wake.Dispose();
        }
    }

    private void Tick()
    {
        try
        {
            if (_running)
            {
                var failure = Interlocked.Exchange(ref _captureFailure, null);
                if (failure is not null && failure.Generation == _captureGeneration)
                    throw new InvalidOperationException($"Microphone capture failed: {failure.Message}");

                while (_frames.TryDequeue(out var frame))
                {
                    Interlocked.Decrement(ref _frameCount);
                    if (frame.Generation != _captureGeneration) continue;
                    _latestSample = frame.Sample;
                    _detection = _gate.Evaluate(frame.Sample, _settings);
                }
                _detection = _gate.Evaluate(_latestSample, _settings);
                if (_detection.Phase == DetectionPhase.Stale)
                {
                    var stale = _detection;
                    StopCapture();
                    _detection = stale;
                    _playback?.RequestStartupRestoration();
                    _error = "The microphone stopped delivering fresh audio. Monitoring is off; startup volumes are being restored.";
                    AudioDiagnostics.Write(() => "capture-stale monitoring=off restoration=requested");
                }
            }
        }
        catch (Exception exception)
        {
            StopCapture();
            _playback?.RequestStartupRestoration();
            _error = exception.Message;
            AudioDiagnostics.Write(() => $"capture-error error={exception.Message} restoration=requested");
        }

        if (_ready && _playback is not null)
        {
            try { _playback.Update(_running, _running && _detection.Speaking, _settings); }
            catch (Exception exception) { _error = $"Playback could not be updated: {exception.Message}"; }
        }
        Publish();
    }

    private void StartCapture()
    {
        try
        {
            _captureDevices = new MMDeviceEnumerator();
            _captureDevice = MicrophoneDevices.OpenSelected(_captureDevices, _settings.MicrophoneDeviceId);
            _microphoneName = _captureDevice.FriendlyName;
            var capture = new WasapiCapture(_captureDevice);
            _capture = capture;
            if (!MicrophoneLevel.IsSupported(capture.WaveFormat))
                throw new NotSupportedException($"Unsupported microphone format: {capture.WaveFormat}");
            var generation = Interlocked.Increment(ref _captureGeneration);
            _gate.Reset();
            _latestSample = null;
            _detection = GateState.Off(_settings);
            capture.DataAvailable += (_, args) =>
            {
                if (generation != Volatile.Read(ref _captureGeneration)) return;
                try
                {
                    var rms = MicrophoneLevel.Rms(args.Buffer.AsSpan(0, args.BytesRecorded), capture.WaveFormat);
                    var sample = new CapturedMicrophoneSample(rms, Environment.TickCount64,
                        Interlocked.Increment(ref _sampleSequence));
                    _frames.Enqueue(new CaptureFrame(generation, sample));
                    Interlocked.Increment(ref _frameCount);
                    while (Volatile.Read(ref _frameCount) > 256 && _frames.TryDequeue(out var droppedFrame))
                        Interlocked.Decrement(ref _frameCount);
                }
                catch (Exception exception)
                {
                    Interlocked.Exchange(ref _captureFailure, new CaptureFailure(generation, exception.Message));
                    Signal();
                }
            };
            capture.RecordingStopped += (_, args) =>
            {
                if (generation != Volatile.Read(ref _captureGeneration)) return;
                Interlocked.Exchange(ref _captureFailure, new CaptureFailure(generation,
                    args.Exception?.Message ?? "The microphone stopped unexpectedly."));
                Signal();
            };
            _running = true;
            capture.StartRecording();
            AudioDiagnostics.Write(() => $"engine-start normal-volume-model=true microphone={_microphoneName}");
            LogSettings("monitoring-settings");
        }
        catch
        {
            StopCapture();
            _playback?.RequestStartupRestoration();
            throw;
        }
    }

    private void StopCapture()
    {
        Interlocked.Increment(ref _captureGeneration);
        _running = false;
        _gate.Reset();
        _latestSample = null;
        _detection = GateState.Off(_settings);
        Interlocked.Exchange(ref _captureFailure, null);
        while (_frames.TryDequeue(out _)) Interlocked.Decrement(ref _frameCount);
        var capture = _capture;
        _capture = null;
        var errors = new List<string>();
        try { capture?.StopRecording(); }
        catch (Exception exception) { errors.Add(exception.Message); }
        try { capture?.Dispose(); }
        catch (Exception exception) { errors.Add(exception.Message); }
        var device = _captureDevice;
        _captureDevice = null;
        try { device?.Dispose(); }
        catch (Exception exception) { errors.Add(exception.Message); }
        var devices = _captureDevices;
        _captureDevices = null;
        try { devices?.Dispose(); }
        catch (Exception exception) { errors.Add(exception.Message); }
        if (errors.Count > 0)
            _error = $"Microphone resources could not be released: {string.Join(" ", errors)}";
    }

    private void RefreshMicrophones()
    {
        _microphones = MicrophoneDevices.GetChoices().ToArray();
        if (!_microphones.Any(choice => choice.Id == _settings.MicrophoneDeviceId))
            _microphones = _microphones.Append(new MicrophoneChoice(_settings.MicrophoneDeviceId,
                "Unavailable microphone — choose another input")).ToArray();
        if (!_running)
            _microphoneName = _microphones.First(choice => choice.Id == _settings.MicrophoneDeviceId).Name;
    }

    private void Publish()
    {
        var errors = new[] { _error, _playback?.RestorationError }
            .Where(error => !string.IsNullOrWhiteSpace(error))
            .Concat(_playback?.Errors ?? []).Distinct(StringComparer.Ordinal).ToArray();
        var streams = _playback?.Streams ?? [];
        var state = new EngineStatus(_ready, _running, _running && _detection.Speaking,
            streams.Count(stream => stream.IsLowered), _detection, _microphoneName, streams, _microphones,
            errors.Length == 0 ? null : string.Join(" ", errors), _playback?.HasPendingRestoration == true);
        if (state.Running != _state.Running || state.Speaking != _state.Speaking)
            AudioDiagnostics.Write(() => FormattableString.Invariant(
                $"detection-change running={state.Running} speaking={state.Speaking} phase={state.Detection.Phase} micDb={state.Detection.Decibels:0.0} thresholdDb={state.Detection.ThresholdDecibels:0.0} releaseDb={state.Detection.ReleaseThresholdDecibels:0.0} loweredSessions={state.AffectedSessions}"));
        Volatile.Write(ref _state, state);
        try { StatusChanged?.Invoke(state); }
        catch (Exception exception)
        {
            AudioDiagnostics.Write(() => $"status-publication-error error={exception.Message}");
        }
    }

    private void LogSettings(string reason) => AudioDiagnostics.Write(() => FormattableString.Invariant(
        $"{reason} thresholdDb={_settings.ThresholdDecibels:0.0} sensitivityDb={_settings.MicrophoneGainDecibels} hysteresisDb={_settings.HysteresisDecibels} reductionPercent={_settings.ReductionPercent} fadeDownMs={_settings.FadeDownMilliseconds} holdMs={_settings.RestoreDelayMilliseconds} fadeUpMs={_settings.FadeUpMilliseconds}"));

    private void ShutdownAudio()
    {
        StopCapture();
        if (_playback is not null)
        {
            try
            {
                _playback.RequestStartupRestoration(2000);
                var deadline = Environment.TickCount64 + 3000;
                do
                {
                    _playback.Update(false, false, _settings);
                    if (!_playback.CanAdvanceRestoration) break;
                    Thread.Sleep(20);
                } while (Environment.TickCount64 < deadline);
                if (_playback.HasPendingRestoration)
                    _error = _playback.RestorationError;
                AudioDiagnostics.Write(() => $"engine-stop result={(_playback.HasPendingRestoration ? "restoration-unconfirmed" : "restoration-complete")}");
            }
            catch (Exception exception) { _error = $"Audio restoration failed: {exception.Message}"; }
            finally
            {
                try { _playback.Dispose(); }
                catch (Exception exception) { _error = $"Playback resources could not be released: {exception.Message}"; }
            }
        }
        _ready = false;
        Publish();
    }

    public void Dispose()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_commandSync)
        {
            if (_disposed != 0) return;
            Volatile.Write(ref _disposed, 1);
            _commands.Enqueue(new Command(() => _exit = true, completion));
        }
        Signal();
        if (Thread.CurrentThread != _worker && !_worker.Join(TimeSpan.FromSeconds(10)))
        {
            AudioDiagnostics.Write(() => "engine-dispose worker-timeout restoration=unconfirmed");
            return;
        }
    }

    private void Signal()
    {
        try { _wake.Set(); }
        catch (ObjectDisposedException) { }
    }

    private sealed record Command(Action Action, TaskCompletionSource Completion);
    private sealed record CaptureFrame(int Generation, CapturedMicrophoneSample Sample);
    private sealed record CaptureFailure(int Generation, string Message);
}
