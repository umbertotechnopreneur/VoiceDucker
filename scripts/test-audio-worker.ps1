# VoiceDucker - Copyright (c) 2026 Umberto Giacobbi. MIT license.
# Runs the production worker, controller and detector against fake audio transports.
# No microphone, playback API, application window, registry, or external service is used.
[CmdletBinding()]
param([switch]$SettingsOnly)
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$testRoot = Join-Path $projectRoot 'artifacts\audio-worker-tests'
New-Item -ItemType Directory -Path $testRoot -Force | Out-Null
$sourceRoot = [Security.SecurityElement]::Escape($projectRoot)
@"
<Project Sdk="Microsoft.NET.Sdk">
 <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework>
 <ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable><TreatWarningsAsErrors>true</TreatWarningsAsErrors>
 <EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup>
 <ItemGroup><Compile Include="Program.cs"/><Compile Include="FakeAudio.cs"/>
 <Compile Include="$sourceRoot\Audio\DuckingEngine.cs" Link="DuckingEngine.cs"/>
 <Compile Include="$sourceRoot\Audio\PlaybackSessions.cs" Link="PlaybackSessions.cs"/>
 <Compile Include="$sourceRoot\Audio\PlaybackVolumeEvents.cs" Link="PlaybackVolumeEvents.cs"/>
 <Compile Include="$sourceRoot\Audio\EngineStatus.cs" Link="EngineStatus.cs"/>
 <Compile Include="$sourceRoot\Audio\VoiceGate.cs" Link="VoiceGate.cs"/>
 <Compile Include="$sourceRoot\Audio\MicrophoneLevel.cs" Link="MicrophoneLevel.cs"/>
 <Compile Include="$sourceRoot\Audio\DuckingSettings.cs" Link="DuckingSettings.cs"/>
 <Compile Include="$sourceRoot\Audio\PlaybackIdentity.cs" Link="PlaybackIdentity.cs"/>
 </ItemGroup></Project>
"@ | Set-Content -LiteralPath (Join-Path $testRoot 'WorkerTests.csproj') -Encoding utf8
@'
using System.Collections.Concurrent;
using NAudio.Wave;
using VoiceDucker.Audio;

internal static class FakeAudio
{
    public const string Key = "fake-spotify";
    public static readonly ConcurrentQueue<string> Trace = new();
    public static readonly ConcurrentBag<int> NativeThreads = [];
    public static readonly ConcurrentQueue<WasapiCapture> Captures = new();
    public static float Volume = 1;
    public static int Writes;
    public static int Starts;
    public static int Stops;
    public static int CaptureDisposals;
    public static int DeviceCreations;
    public static int DeviceDisposals;
    public static int EnumeratorCreations;
    public static int EnumeratorDisposals;
    public static bool SourceDisposed;
    public static bool FailRead;
    public static bool FailStart;
    public static bool FailCaptureDispose;
    public static bool FailDeviceDispose;
    public static bool UnsupportedFormat;
    public static ManualResetEventSlim? ReadBlock;
    public static TaskCompletionSource ReadEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public static NativePlaybackSource? Source;
    public static WasapiCapture? Latest => Captures.LastOrDefault();
    public static void Native(string name)
    {
        NativeThreads.Add(Environment.CurrentManagedThreadId); Trace.Enqueue(name);
    }
    public static void Reset()
    {
        Trace.Clear(); NativeThreads.Clear(); Captures.Clear();
        Volume = 1; Writes = Starts = Stops = CaptureDisposals = 0;
        DeviceCreations = DeviceDisposals = EnumeratorCreations = EnumeratorDisposals = 0;
        SourceDisposed = FailRead = FailStart = FailCaptureDispose = FailDeviceDispose = UnsupportedFormat = false;
        ReadBlock = null;
        ReadEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Source = null;
    }
}
namespace NAudio.CoreAudioApi.Interfaces
{
    public enum AudioSessionState { AudioSessionStateActive, AudioSessionStateInactive, AudioSessionStateExpired }
    public enum AudioSessionDisconnectReason { DisconnectReasonDeviceRemoval }
    public interface IAudioSessionEvents
    {
        int OnSimpleVolumeChanged(float volume, bool mute, ref Guid context);
        int OnDisplayNameChanged(string value, ref Guid context);
        int OnIconPathChanged(string value, ref Guid context);
        int OnChannelVolumeChanged(uint count, IntPtr values, uint index, ref Guid context);
        int OnGroupingParamChanged(ref Guid id, ref Guid context);
        int OnStateChanged(AudioSessionState state);
        int OnSessionDisconnected(AudioSessionDisconnectReason reason);
    }
}
namespace NAudio.CoreAudioApi
{
    public sealed class MMDeviceEnumerator : IDisposable
    {
        public MMDeviceEnumerator() { FakeAudio.Native("enumerator-create"); Interlocked.Increment(ref FakeAudio.EnumeratorCreations); }
        public void Dispose() { FakeAudio.Native("enumerator-dispose"); Interlocked.Increment(ref FakeAudio.EnumeratorDisposals); }
    }
    public sealed class MMDevice(string id) : IDisposable
    {
        public string FriendlyName { get; } = "Fake microphone " + id;
        public void Dispose()
        {
            FakeAudio.Native("device-dispose"); Interlocked.Increment(ref FakeAudio.DeviceDisposals);
            if (FakeAudio.FailDeviceDispose) throw new InvalidOperationException("synthetic device disposal failure");
        }
    }
}
namespace NAudio.Wave
{
    public enum WaveFormatEncoding { Unknown, Pcm, IeeeFloat, Extensible }
    public class WaveFormat(WaveFormatEncoding encoding, int bits)
    {
        public WaveFormatEncoding Encoding { get; } = encoding;
        public int BitsPerSample { get; } = bits;
        public int Channels => 1;
        public int SampleRate => 48000;
    }
    public sealed class WaveFormatExtensible(int bits, Guid subFormat)
        : WaveFormat(WaveFormatEncoding.Extensible, bits)
    {
        public Guid SubFormat { get; } = subFormat;
    }
    public sealed class WaveInEventArgs(byte[] buffer) : EventArgs
    {
        public byte[] Buffer { get; } = buffer;
        public int BytesRecorded => Buffer.Length;
    }
    public sealed class StoppedEventArgs(Exception? exception) : EventArgs
    {
        public Exception? Exception { get; } = exception;
    }
    public sealed class WasapiCapture : IDisposable
    {
        public bool Started;
        public bool Disposed;
        public WaveFormat WaveFormat { get; }
        public event EventHandler<WaveInEventArgs>? DataAvailable;
        public event EventHandler<StoppedEventArgs>? RecordingStopped;
        public WasapiCapture(NAudio.CoreAudioApi.MMDevice device)
        {
            FakeAudio.Native("capture-create");
            WaveFormat = new(FakeAudio.UnsupportedFormat ? WaveFormatEncoding.Pcm : WaveFormatEncoding.IeeeFloat,
                FakeAudio.UnsupportedFormat ? 8 : 32);
            FakeAudio.Captures.Enqueue(this);
        }
        public void StartRecording()
        {
            FakeAudio.Native("capture-start");
            if (FakeAudio.FailStart) throw new InvalidOperationException("synthetic capture start failure");
            Started = true; Interlocked.Increment(ref FakeAudio.Starts);
        }
        public void StopRecording()
        {
            FakeAudio.Native("capture-stop"); Started = false; Interlocked.Increment(ref FakeAudio.Stops);
            RecordingStopped?.Invoke(this, new(null));
        }
        public void Emit(float level) => DataAvailable?.Invoke(this, new(BitConverter.GetBytes(level)));
        public void UnexpectedStop() => RecordingStopped?.Invoke(this,
            new(new InvalidOperationException("synthetic microphone disconnected")));
        public void Dispose()
        {
            FakeAudio.Native("capture-dispose"); Disposed = true; Started = false;
            Interlocked.Increment(ref FakeAudio.CaptureDisposals);
            if (FakeAudio.FailCaptureDispose) throw new InvalidOperationException("synthetic capture disposal failure");
        }
    }
}
namespace VoiceDucker.Audio
{
    internal static class AudioDiagnostics
    {
        public static void Write(Func<string> message) { } // Never reads environment or writes diagnostics files.
    }
    internal sealed record MicrophoneChoice(string Id, string Name);
    internal static class MicrophoneDevices
    {
        public static IReadOnlyList<MicrophoneChoice> GetChoices()
        {
            FakeAudio.Native("microphones-list");
            return [new("", "Fake default"), new("mic-b", "Fake B")];
        }
        public static NAudio.CoreAudioApi.MMDevice OpenSelected(NAudio.CoreAudioApi.MMDeviceEnumerator devices, string id)
        {
            FakeAudio.Native("device-create"); Interlocked.Increment(ref FakeAudio.DeviceCreations);
            return new(id);
        }
    }
    internal sealed class NativePlaybackSource : IPlaybackSource
    {
        private readonly PlaybackVolumeEvents _events;
        private readonly PlaybackVolumeEvents.VolumeEventSink _sink;
        public NativePlaybackSource(PlaybackVolumeEvents events)
        {
            _events = events; _sink = events.CreateSink(FakeAudio.Key);
            FakeAudio.Source = this; FakeAudio.Native("source-create");
        }
        public PlaybackReadResult Read()
        {
            FakeAudio.Native("playback-read");
            if (FakeAudio.ReadBlock is { } block)
            {
                FakeAudio.ReadEntered.TrySetResult();
                if (!block.Wait(TimeSpan.FromSeconds(3))) throw new TimeoutException("Synthetic read block was not released.");
            }
            return FakeAudio.FailRead ? new([], ["synthetic enumeration failure"], false)
                : new([new(FakeAudio.Key, "Spotify", 101, Volatile.Read(ref FakeAudio.Volume), .5f, false, true)], []);
        }
        public float ReadVolume(string key) { FakeAudio.Native("volume-read"); return Volatile.Read(ref FakeAudio.Volume); }
        public float SetVolume(string key, float volume)
        {
            FakeAudio.Native("volume-write"); Interlocked.Increment(ref FakeAudio.Writes);
            Volatile.Write(ref FakeAudio.Volume, volume);
            var context = _events.EventContext;
            _sink.OnSimpleVolumeChanged(volume, false, ref context);
            return volume;
        }
        public void ExternalVolume(float volume)
        {
            Volatile.Write(ref FakeAudio.Volume, volume);
            var context = Guid.NewGuid();
            _sink.OnSimpleVolumeChanged(volume, false, ref context);
        }
        public void Dispose() { FakeAudio.Native("source-dispose"); _sink.Deactivate(); FakeAudio.SourceDisposed = true; }
    }
}
internal sealed class SyntheticCapturePump : IDisposable
{
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _work;
    public float Level = .1f;
    public SyntheticCapturePump()
    {
        _work = Task.Run(async () =>
        {
            try
            {
                while (!_stop.IsCancellationRequested)
                {
                    if (FakeAudio.Latest is { Started: true } capture) capture.Emit(Volatile.Read(ref Level));
                    await Task.Delay(20, _stop.Token);
                }
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        });
    }
    public void Dispose() { _stop.Cancel(); _work.GetAwaiter().GetResult(); _stop.Dispose(); }
}
'@ | Set-Content -LiteralPath (Join-Path $testRoot 'FakeAudio.cs') -Encoding utf8
@'
using VoiceDucker.Audio;

var checks = 0;
void Check(bool condition, string message)
{
    checks++;
    if (!condition) throw new InvalidOperationException(message);
}
void Near(float actual, float expected, string message) =>
    Check(Math.Abs(actual - expected) < .0001f, $"{message}: expected {expected:R}, actual {actual:R}");
async Task Wait(Func<bool> condition, string message, int milliseconds = 2000)
{
    var deadline = Environment.TickCount64 + milliseconds;
    while (!condition() && Environment.TickCount64 < deadline) await Task.Delay(10);
    Check(condition(), message);
}
async Task Fails<T>(Task command, string message) where T : Exception
{
    try { await command.WaitAsync(TimeSpan.FromSeconds(3)); }
    catch (T) { Check(true, message); return; }
    Check(false, message);
}
var settings = new DuckingSettings(50, 0, 0, true)
{
    ThresholdDecibels = -30, HysteresisDecibels = 3, RestoreDelayMilliseconds = 50
};
async Task VerifyLiveSettingsAsync()
{
    FakeAudio.Reset();
    var current = new DuckingSettings(50, 0, 120, true)
    {
        ThresholdDecibels = -34.9, HysteresisDecibels = 6, RestoreDelayMilliseconds = 250
    };
    using var engine = new DuckingEngine(current);
    await engine.InitializeAsync();
    await engine.SetMonitoringAsync(true);
    var capture = FakeAudio.Latest;
    var signal = (float)Math.Pow(10, -25d / 20);
    using var pump = new SyntheticCapturePump { Level = signal };
    await Wait(() => engine.State.Speaking && engine.State.AffectedSessions == 1 &&
        Math.Abs(engine.State.Detection.Decibels + 25) < .001,
        "A fixed -25 dBFS synthetic signal did not activate the initial -34.9 dBFS threshold.");
    Near(engine.State.Streams.Single().NormalVolume, 1, "Initial normal level");
    Near(FakeAudio.Volume, .5f, "A 50 percent reduction did not apply to the fixed signal");

    var writes = FakeAudio.Writes;
    current = current with { ThresholdDecibels = -56.7 };
    await engine.UpdateSettingsAsync(current);
    Check(engine.State.Detection.ThresholdDecibels == -56.7 && engine.State.Detection.Phase == DetectionPhase.AboveThreshold,
        "A lower threshold edit was not applied by the running worker.");
    Near(FakeAudio.Volume, .5f, "Lowering an already-crossed threshold changed attenuation depth");
    Check(FakeAudio.Writes == writes, "An already-active threshold edit redundantly rewrote an unchanged target.");

    current = current with { ReductionPercent = 80 };
    await engine.UpdateSettingsAsync(current);
    Near(FakeAudio.Volume, .2f, "Changing reduction from 50 to 80 percent did not change the active target");
    Near(engine.State.Streams.Single().NormalVolume, 1, "A reduction setting changed the normal reference");

    current = current with { ThresholdDecibels = -10 };
    await engine.UpdateSettingsAsync(current);
    Check(engine.State.Detection.ThresholdDecibels == -10 && engine.State.Detection.Phase == DetectionPhase.Hold,
        "Raising the threshold above the captured level did not enter the configured release hold.");
    await Wait(() => !engine.State.Speaking && engine.State.Detection.Phase == DetectionPhase.BelowThreshold &&
        Math.Abs(FakeAudio.Volume - 1) < .0001f,
        "A -25 dBFS signal stayed ducked after threshold rose to -10 dBFS and hold/fade completed.");

    current = current with { MicrophoneGainDecibels = 20 };
    await engine.UpdateSettingsAsync(current);
    Check(engine.State.Detection.Phase == DetectionPhase.AboveThreshold &&
        Math.Abs(engine.State.Detection.Decibels + 5) < .001,
        "Live sensitivity gain did not reclassify -25 dBFS input as -5 dBFS.");
    Near(FakeAudio.Volume, .2f, "A sensitivity gain edit did not resume the configured attenuation");

    current = current with { MicrophoneGainDecibels = -20 };
    await engine.UpdateSettingsAsync(current);
    Check(Math.Abs(engine.State.Detection.Decibels + 45) < .001,
        "Lower sensitivity did not apply to the same captured raw signal.");
    await Wait(() => !engine.State.Speaking && Math.Abs(FakeAudio.Volume - 1) < .0001f,
        "Lower sensitivity did not release attenuation after hold/fade.");
    current = current with { ThresholdDecibels = -56.7 };
    await engine.UpdateSettingsAsync(current);
    Check(engine.State.Speaking && engine.State.Detection.Phase == DetectionPhase.AboveThreshold,
        "Lowering threshold did not reactivate the now -45 dBFS gained signal.");
    Near(FakeAudio.Volume, .2f, "The reclassified signal did not resume the current reduction setting");

    var final = current with { MicrophoneGainDecibels = 0, ThresholdDecibels = -56.7, ReductionPercent = 60 };
    await Task.WhenAll(
        engine.UpdateSettingsAsync(current with { ThresholdDecibels = -10 }),
        engine.UpdateSettingsAsync(current with { MicrophoneGainDecibels = 20, ReductionPercent = 90 }),
        engine.UpdateSettingsAsync(final));
    Check(engine.State.Speaking && engine.State.Detection.ThresholdDecibels == -56.7 &&
        Math.Abs(engine.State.Detection.Decibels + 25) < .001,
        "Queued settings snapshots left old gain or threshold active.");
    Near(FakeAudio.Volume, .4f, "The final settings snapshot did not own attenuation depth");
    Check(ReferenceEquals(FakeAudio.Latest, capture) && FakeAudio.Starts == 1,
        "Gain, threshold, or reduction edits restarted microphone capture.");
    await engine.SetMonitoringAsync(false);
    Near(FakeAudio.Volume, 1, "Live settings changes damaged the immutable startup restoration level");
}
await VerifyLiveSettingsAsync();
if (args.Contains("settings-only"))
{
    Console.WriteLine($"Live audio settings: {checks} focused synthetic checks passed; no audio devices were accessed.");
    return;
}
FakeAudio.Reset();
using (var engine = new DuckingEngine(settings))
{
    Check(!engine.State.Ready && !engine.State.Running && FakeAudio.Captures.IsEmpty,
        "Constructing the worker opened capture or claimed ready playback.");
    await Fails<InvalidOperationException>(engine.SetMonitoringAsync(true), "Capture before snapshot was accepted.");
    Check(FakeAudio.Captures.IsEmpty, "A rejected enable still created capture resources.");
    await engine.InitializeAsync();
    Check(engine.State.Ready && !engine.State.Running && FakeAudio.Captures.IsEmpty,
        "Playback initialization opened microphone capture.");
    Check(engine.State.Streams.Count == 1 && FakeAudio.Writes == 0, "Snapshot discovery changed playback.");
    await Task.WhenAll(Enumerable.Range(0, 24).Select(_ => Task.Run(() => engine.SetMonitoringAsync(true))));
    Check(FakeAudio.Captures.Count == 1 && FakeAudio.Starts == 1 && engine.State.Running,
        "Concurrent enable commands opened overlapping captures.");
    Check(FakeAudio.Trace.ToList().IndexOf("playback-read") < FakeAudio.Trace.ToList().IndexOf("capture-start"),
        "Microphone capture preceded the playback snapshot.");
    using var pump = new SyntheticCapturePump();
    await Wait(() => engine.State.AffectedSessions == 1, "Synthetic voice did not attenuate playback.");
    Near(FakeAudio.Volume, .5f, "Initial attenuation");
    Check(engine.State.Detection.Phase == DetectionPhase.AboveThreshold &&
        Math.Abs(engine.State.Detection.Decibels + 20) < .001,
        "Worker microphone metrics disagreed with its detector.");

    await engine.SetPlaybackVolumeAsync(FakeAudio.Key, .2f);
    Near(FakeAudio.Volume, .2f, "An actual slider edit was attenuated twice");
    Near(engine.State.Streams.Single().NormalVolume, .4f, "Actual 20 percent did not retain normal 40 percent");
    var actualWrites = FakeAudio.Writes;
    await engine.SetPlaybackVolumeAsync(FakeAudio.Key, .2f);
    await Task.Delay(180);
    Check(FakeAudio.Writes == actualWrites, "An unchanged actual slider edit or settled worker poll rewrote the mixer.");
    Volatile.Write(ref pump.Level, 0);
    await Wait(() => !engine.State.Speaking && Math.Abs(FakeAudio.Volume - .4f) < .0001f,
        "Releasing voice did not restore the actual slider's selected normal level.");
    Volatile.Write(ref pump.Level, .1f);
    await Wait(() => engine.State.Speaking && Math.Abs(FakeAudio.Volume - .2f) < .0001f,
        "A new voice cycle rebased the actual slider into a second attenuation.");
    await engine.SetPlaybackVolumeAsync(FakeAudio.Key, .9f);
    Near(FakeAudio.Volume, .5f, "A saturated actual edit exceeded the ducked maximum");
    Near(engine.State.Streams.Single().NormalVolume, 1, "A saturated actual edit exceeded unity normal");
    await engine.UpdateSettingsAsync(settings with { ReductionPercent = 100 });
    actualWrites = FakeAudio.Writes;
    foreach (var requested in new[] { 0f, .2f })
        await Fails<InvalidOperationException>(engine.SetPlaybackVolumeAsync(FakeAudio.Key, requested),
            "An actual edit at zero gain was accepted.");
    Near(engine.State.Streams.Single().NormalVolume, 1, "A zero gain actual edit destroyed normal");
    Check(FakeAudio.Writes == actualWrites, "A rejected zero gain edit wrote playback.");
    await Wait(() => engine.State.Error is null,
        "A rejected zero gain edit left a permanent engine error.");
    await engine.UpdateSettingsAsync(settings);
    using (var block = new ManualResetEventSlim(false))
    {
        FakeAudio.ReadEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        FakeAudio.ReadBlock = block;
        await FakeAudio.ReadEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Task actualEdit;
        Task settingsEdit;
        try
        {
            actualEdit = engine.SetPlaybackVolumeAsync(FakeAudio.Key, .2f);
            settingsEdit = engine.UpdateSettingsAsync(settings with { ReductionPercent = 80 });
        }
        finally { FakeAudio.ReadBlock = null; block.Set(); }
        await Task.WhenAll(actualEdit, settingsEdit);
    }
    Near(FakeAudio.Volume, .2f, "Queued actual intent did not use the settings consumed in its update");
    Near(engine.State.Streams.Single().NormalVolume, 1, "Queued actual intent used an old reduction gain");
    await engine.UpdateSettingsAsync(settings);
    var actualBurst = new[] { .1f, .2f, .3f }.Select(actual => engine.SetPlaybackVolumeAsync(FakeAudio.Key, actual)).ToArray();
    await Task.WhenAll(actualBurst).WaitAsync(TimeSpan.FromSeconds(3));
    Near(FakeAudio.Volume, .3f, "Queued actual commands failed to settle with the last requested level");
    await Fails<ArgumentOutOfRangeException>(engine.SetPlaybackVolumeAsync(FakeAudio.Key, float.NaN),
        "A non-finite actual-volume intent was accepted.");

    foreach (var normal in new[] { .13f, .6f, .36f, .8f })
    {
        await engine.SetNormalVolumeAsync(FakeAudio.Key, normal);
        Near(FakeAudio.Volume, normal * .5f, "Normal slider edit stopped ducking during continuous voice");
        Near(engine.State.Streams.Single().NormalVolume, normal, "The normal slider value was not published");
        Check(engine.State.AffectedSessions == 1 && engine.State.Speaking,
            "Editing a normal level introduced a manual detection bypass.");
    }
    var stableWrites = FakeAudio.Writes;
    await Task.Delay(180);
    Check(FakeAudio.Writes == stableWrites, "A stable worker target was rewritten on every poll.");
    var commandBurst = new[] { .2f, .4f, .6f, .9f }
        .Select(normal => engine.SetNormalVolumeAsync(FakeAudio.Key, normal)).ToArray();
    await Task.WhenAll(commandBurst);
    Near(FakeAudio.Volume, .45f, "Queued normal volume commands were reordered");
    await Fails<ArgumentOutOfRangeException>(engine.SetNormalVolumeAsync(FakeAudio.Key, float.NaN),
        "A non-finite normal volume command was accepted.");
    Check(engine.State.Error is null && engine.State.Running,
        "An invalid command poisoned the active capture snapshot with a permanent error.");

    foreach (var externalIsNewer in new[] { true, false })
    {
        using var block = new ManualResetEventSlim(false);
        FakeAudio.ReadEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        FakeAudio.ReadBlock = block;
        await FakeAudio.ReadEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Task command;
        try
        {
            if (externalIsNewer)
            {
                command = engine.SetNormalVolumeAsync(FakeAudio.Key, .7f);
                FakeAudio.Source!.ExternalVolume(.4f);
            }
            else
            {
                FakeAudio.Source!.ExternalVolume(.6f);
                command = engine.SetNormalVolumeAsync(FakeAudio.Key, .8f);
            }
        }
        finally { FakeAudio.ReadBlock = null; block.Set(); }
        await command;
        var expectedNormal = externalIsNewer ? .4f : .8f;
        await Wait(() => Math.Abs(FakeAudio.Volume - expectedNormal * .5f) < .0001f,
            "Worker queue ordering overrode the most recent volume intent.");
        Near(engine.State.Streams.Single().NormalVolume, expectedNormal,
            "The audio worker lost chronological slider/external intent order");
    }
    FakeAudio.Source!.ExternalVolume(.3f);
    await Wait(() => Math.Abs(FakeAudio.Volume - .15f) < .0001f &&
        Math.Abs(engine.State.Streams.Single().NormalVolume - .3f) < .0001f,
        "External volume intent was not projected while voice remained active.");
    Near(engine.State.Streams.Single().NormalVolume, .3f, "External intent did not update the normal volume");

    var previousCapture = FakeAudio.Latest!;
    Volatile.Write(ref pump.Level, 0);
    await engine.UpdateSettingsAsync(settings with { MicrophoneDeviceId = "mic-b" });
    Check(previousCapture.Disposed && FakeAudio.Captures.Count == 2 && FakeAudio.Starts == 2,
        "Selecting a microphone did not release and replace the old capture.");
    await Wait(() => !engine.State.Speaking, "New microphone inherited old detector hold.");
    await engine.UpdateSettingsAsync(settings with { MicrophoneDeviceId = "mic-b", RestoreDelayMilliseconds = 200 });
    FakeAudio.Latest!.Emit(.1f);
    FakeAudio.Latest!.Emit(0);
    await Wait(() => engine.State.Detection.Phase == DetectionPhase.Hold && engine.State.AffectedSessions == 1,
        "A loud capture buffer followed by quiet between worker polls was lost.");
    await Wait(() => !engine.State.Speaking, "A brief captured signal did not release after its hold duration.");
    previousCapture.Emit(1);
    previousCapture.UnexpectedStop();
    await Task.Delay(120);
    Check(engine.State.Running && !engine.State.Speaking && engine.State.Error is null,
        "A retired microphone callback changed the new capture generation.");
    Volatile.Write(ref pump.Level, .1f);
    await Wait(() => engine.State.AffectedSessions == 1, "The replacement microphone did not drive attenuation.");
    FakeAudio.Latest!.UnexpectedStop();
    await Wait(() => !engine.State.Running && Math.Abs(FakeAudio.Volume - 1) < .0001f,
        "Unexpected microphone stop did not restore startup playback.");
    Check(engine.State.Error?.Contains("synthetic microphone disconnected") == true,
        "Unexpected microphone failure was not reported.");
    Check(FakeAudio.CaptureDisposals == 2 && FakeAudio.DeviceDisposals == 2 && FakeAudio.EnumeratorDisposals == 2,
        "Capture failure retained microphone resources.");
    await engine.SetMonitoringAsync(true);
    await Wait(() => engine.State.AffectedSessions == 1, "Capture did not recover after a reported failure.");
    Check(engine.State.Error is null, "A recovered capture retained a stale fault message.");
    await engine.SetMonitoringAsync(false);
    Near(FakeAudio.Volume, 1, "Disable did not restore the immutable startup level");
    stableWrites = FakeAudio.Writes;
    await Task.Delay(120);
    Check(FakeAudio.Writes == stableWrites && engine.State.Detection.Phase == DetectionPhase.Off,
        "Disabled polling changed playback or retained microphone detection.");
    Check(FakeAudio.NativeThreads.Distinct().Count() == 1,
        "A native audio resource was accessed outside its owning audio worker.");
}
Check(FakeAudio.SourceDisposed && FakeAudio.CaptureDisposals == FakeAudio.Captures.Count &&
    FakeAudio.DeviceDisposals == FakeAudio.DeviceCreations &&
    FakeAudio.EnumeratorDisposals == FakeAudio.EnumeratorCreations,
    "Worker disposal retained native resources.");

foreach (var emitOneBuffer in new[] { false, true })
{
    FakeAudio.Reset();
    using var engine = new DuckingEngine(settings);
    await engine.InitializeAsync();
    await engine.SetMonitoringAsync(true);
    if (emitOneBuffer)
    {
        FakeAudio.Latest!.Emit(.1f);
        await Wait(() => engine.State.AffectedSessions == 1, "One synthetic loud buffer did not trigger attenuation.");
    }
    await Wait(() => !engine.State.Running, "Missing capture buffers left monitoring active.", 2500);
    Check(engine.State.Detection.Phase == DetectionPhase.Stale && engine.State.Error?.Contains("fresh audio") == true,
        "Stale capture was not exposed as a distinct reported failure.");
    Near(FakeAudio.Volume, 1, "Stale capture did not restore startup playback");
}

FakeAudio.Reset();
using (var engine = new DuckingEngine(settings))
{
    FakeAudio.FailRead = true;
    await Fails<InvalidOperationException>(engine.InitializeAsync(), "Failed enumeration produced a ready startup snapshot.");
    Check(!engine.State.Ready, "Failed playback initialization claimed readiness.");
    await Fails<InvalidOperationException>(engine.SetMonitoringAsync(true), "Capture started without an available startup snapshot.");
    Check(FakeAudio.Captures.IsEmpty, "Failed playback initialization still opened a microphone.");
    FakeAudio.FailRead = false;
    await engine.InitializeAsync();
    Check(engine.State.Ready && engine.State.Error is null, "Playback initialization could not recover after enumeration resumed.");
    FakeAudio.UnsupportedFormat = true;
    await Fails<NotSupportedException>(engine.SetMonitoringAsync(true), "Unsupported capture format was accepted.");
    Check(!engine.State.Running && FakeAudio.CaptureDisposals == 1 && FakeAudio.DeviceDisposals == 1 &&
        FakeAudio.EnumeratorDisposals == 1, "Unsupported capture format leaked acquired resources.");
    FakeAudio.UnsupportedFormat = false;
    FakeAudio.FailStart = true;
    await Fails<InvalidOperationException>(engine.SetMonitoringAsync(true), "Capture start failure was not propagated to its command.");
    Check(!engine.State.Running && FakeAudio.CaptureDisposals == 2 && FakeAudio.DeviceDisposals == 2 &&
        FakeAudio.EnumeratorDisposals == 2, "Capture start failure retained native resources.");
}

FakeAudio.Reset();
using (var engine = new DuckingEngine(settings))
{
    await engine.InitializeAsync();
    await engine.SetMonitoringAsync(true);
    FakeAudio.FailCaptureDispose = FakeAudio.FailDeviceDispose = true;
    await engine.SetMonitoringAsync(false);
    Check(FakeAudio.CaptureDisposals == 1 && FakeAudio.DeviceDisposals == 1 && FakeAudio.EnumeratorDisposals == 1,
        "One failed capture cleanup prevented the remaining resources from being released.");
    Check(engine.State.Error?.Contains("disposal failure") == true && !engine.State.Running,
        "Cleanup failures were reported as successful microphone shutdown.");
    FakeAudio.FailCaptureDispose = FakeAudio.FailDeviceDispose = false;
}

FakeAudio.Reset();
var disposing = new DuckingEngine(settings);
await disposing.InitializeAsync();
var returned = new System.Collections.Concurrent.ConcurrentBag<Task>();
var producers = Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
{
    for (var index = 0; index < 30; index++) returned.Add(index % 2 == 0
        ? disposing.SetNormalVolumeAsync(FakeAudio.Key, .8f)
        : disposing.SetPlaybackVolumeAsync(FakeAudio.Key, .4f));
})).ToArray();
var closing = Task.Run(disposing.Dispose);
await Task.WhenAll(producers);
await closing.WaitAsync(TimeSpan.FromSeconds(5));
foreach (var command in returned)
{
    try { await command.WaitAsync(TimeSpan.FromSeconds(2)); }
    catch (ObjectDisposedException) { }
    catch (InvalidOperationException exception) when (exception.Message.Contains("monitoring stopped")) { }
    Check(command.IsCompleted, "A queued command remained unresolved after concurrent disposal.");
}
await Fails<ObjectDisposedException>(disposing.SetMonitoringAsync(true), "Disposed worker accepted a new capture command.");
await Fails<ObjectDisposedException>(disposing.InitializeAsync(), "Disposed worker accepted initialization.");
await Fails<ObjectDisposedException>(disposing.UpdateSettingsAsync(settings), "Disposed worker accepted a settings command.");
await Fails<ObjectDisposedException>(disposing.SetPlaybackVolumeAsync(FakeAudio.Key, .2f), "Disposed worker accepted an actual-volume command.");
Check(FakeAudio.SourceDisposed && !disposing.State.Ready && !disposing.State.Running,
    "Concurrent disposal retained a ready worker or native source.");
Near(FakeAudio.Volume, 1, "Concurrent disposal failed startup restoration");
Console.WriteLine($"Audio worker: {checks} synthetic checks passed; no audio devices or platform services were accessed.");
'@ | Set-Content -LiteralPath (Join-Path $testRoot 'Program.cs') -Encoding utf8
dotnet build (Join-Path $testRoot 'WorkerTests.csproj') --nologo --verbosity minimal `
    -p:ImportDirectoryBuildProps=false -p:NuGetAudit=false
if ($LASTEXITCODE -ne 0) { throw 'Synthetic audio worker harness did not build.' }
$testArguments = @()
if ($SettingsOnly) { $testArguments += 'settings-only' }
dotnet (Join-Path $testRoot 'bin\Debug\net10.0\WorkerTests.dll') @testArguments
if ($LASTEXITCODE -ne 0) { throw 'Synthetic audio worker checks failed.' }
