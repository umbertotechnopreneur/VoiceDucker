# VoiceDucker - Copyright (c) 2026 Umberto Giacobbi. MIT license.
# Exercises the production session loop against an isolated mixer and clock.
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$testRoot = Join-Path $projectRoot 'artifacts\playback-restoration-tests'
New-Item -ItemType Directory -Path $testRoot -Force | Out-Null
$sourceRoot = [Security.SecurityElement]::Escape($projectRoot)
@"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="Program.cs" />
    <Compile Include="Mixer.cs" />
    <Compile Include="$sourceRoot\Audio\PlaybackSessions.cs" Link="PlaybackSessions.cs" />
    <Compile Include="$sourceRoot\Audio\PlaybackVolumeSnapshot.cs" Link="PlaybackVolumeSnapshot.cs" />
    <Compile Include="$sourceRoot\Audio\PlaybackVolumeEvents.cs" Link="PlaybackVolumeEvents.cs" />
    <Compile Include="$sourceRoot\Audio\AudioDiagnostics.cs" Link="AudioDiagnostics.cs" />
    <Compile Include="$sourceRoot\Audio\DuckingSettings.cs" Link="DuckingSettings.cs" />
    <Compile Include="$sourceRoot\Audio\PlaybackIdentity.cs" Link="PlaybackIdentity.cs" />
    <Compile Include="$sourceRoot\Audio\VoiceGate.cs" Link="VoiceGate.cs" />
  </ItemGroup>
</Project>
"@ | Set-Content (Join-Path $testRoot 'RestorationTests.csproj') -Encoding utf8

@'
namespace NAudio.CoreAudioApi.Interfaces
{
    public enum AudioSessionState { AudioSessionStateActive, AudioSessionStateInactive, AudioSessionStateExpired }
    public enum AudioSessionDisconnectReason { DisconnectReasonDeviceRemoval }
    public interface IAudioSessionEvents
    {
        int OnSimpleVolumeChanged(float volume, bool isMuted, ref Guid eventContext);
        int OnDisplayNameChanged(string displayName, ref Guid eventContext);
        int OnIconPathChanged(string iconPath, ref Guid eventContext);
        int OnChannelVolumeChanged(uint channelCount, IntPtr newVolumes, uint channelIndex, ref Guid eventContext);
        int OnGroupingParamChanged(ref Guid groupingId, ref Guid eventContext);
        int OnStateChanged(AudioSessionState state);
        int OnSessionDisconnected(AudioSessionDisconnectReason reason);
    }
}
namespace NAudio.CoreAudioApi
{
    public enum DataFlow { Render }
    public enum DeviceState { Active }
    public sealed class MMDeviceEnumerator : IDisposable
    {
        public static AudioSessionControl Session { get; set; } = new();
        public IEnumerable<MMDevice> EnumerateAudioEndPoints(DataFlow flow, DeviceState state) => [new()];
        public void Dispose() { }
    }
    public sealed class MMDevice : IDisposable
    {
        public string ID => "test-endpoint";
        public AudioSessionManager AudioSessionManager { get; } = new();
        public void Dispose() { }
    }
    public sealed class AudioSessionManager
    {
        public List<AudioSessionControl> Sessions => [MMDeviceEnumerator.Session];
    }
    public sealed class AudioSessionControl
    {
        public string GetSessionInstanceIdentifier { get; set; } = "test-session";
        public uint GetProcessID => (uint)Environment.ProcessId;
        public Interfaces.AudioSessionState State { get; set; } = Interfaces.AudioSessionState.AudioSessionStateActive;
        public SimpleAudioVolume SimpleAudioVolume { get; } = new();
        public AudioMeterInformation AudioMeterInformation { get; } = new();
    }
    public sealed class SimpleAudioVolume
    {
        private float _volume = 1f;
        public List<Interfaces.IAudioSessionEvents> Sinks { get; } = [];
        public bool Mute { get; set; }
        public Guid LastWriteContext { get; private set; }
        public float Volume { get => _volume; set => SetVolume(value, Guid.Empty); }
        public void SetVolume(float value, Guid context)
        {
            _volume = value;
            Writes++;
            LastWriteContext = context;
            foreach (var sink in Sinks.ToArray()) sink.OnSimpleVolumeChanged(value, Mute, ref context);
        }
        public int Writes { get; private set; }
    }
    public sealed class AudioMeterInformation
    {
        public float MasterPeakValue { get; set; } = 0.5f;
    }
}
namespace VoiceDucker.Audio
{
    internal sealed class NativePlaybackSession : IDisposable
    {
        public static int ActiveCount { get; private set; }
        private bool _disposed;
        private readonly NAudio.CoreAudioApi.SimpleAudioVolume _volume;
        private readonly PlaybackVolumeEvents.VolumeEventSink? _sink;
        private NativePlaybackSession(NAudio.CoreAudioApi.SimpleAudioVolume volume,
            PlaybackVolumeEvents.VolumeEventSink? sink)
        {
            _volume = volume;
            _sink = sink;
            ActiveCount++;
            if (sink is not null) volume.Sinks.Add(sink);
        }
        public static NativePlaybackSession Open(string endpointId, string instanceId,
            PlaybackVolumeEvents? events = null) => new(
                NAudio.CoreAudioApi.MMDeviceEnumerator.Session.SimpleAudioVolume,
                events?.CreateSink(endpointId + ":" + instanceId));
        public void SetVolume(float volume, Guid context) => _volume.SetVolume(volume, context);
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            ActiveCount--;
            _sink?.Deactivate();
            if (_sink is not null) _volume.Sinks.Remove(_sink);
        }
        public static void DisposeAll(IEnumerable<NativePlaybackSession> sessions)
        {
            foreach (var session in sessions) session.Dispose();
        }
    }
}
'@ | Set-Content (Join-Path $testRoot 'Mixer.cs') -Encoding utf8

@'
using NAudio.CoreAudioApi;
using VoiceDucker.Audio;

void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

// The real callback must ignore our context, keep per-session external changes,
// and stop accepting notifications after unregistration.
var events = new PlaybackVolumeEvents();
var sink = events.CreateSink("isolated-session");
var ownContext = events.EventContext;
sink.OnSimpleVolumeChanged(0.1f, false, ref ownContext);
Check(events.Latest("isolated-session") is null, "Our own write was classified as external.");
var externalContext = Guid.NewGuid();
sink.OnSimpleVolumeChanged(0.42f, false, ref externalContext);
Check(events.Latest("isolated-session") is { Volume: 0.42f, Muted: false }, "External volume change was lost.");
sink.OnSimpleVolumeChanged(0.42f, true, ref externalContext);
var mutedChange = events.Latest("isolated-session");
Check(mutedChange is { Muted: true } && mutedChange.Version > 1, "External mute change was lost.");
Check(events.Latest("another-session") is null, "An external change leaked across sessions.");
sink.Deactivate();
sink.OnSimpleVolumeChanged(0.8f, false, ref externalContext);
Check(events.Latest("isolated-session") == mutedChange, "A disposed sink accepted a callback.");

// A genuine external event smaller than the old polling tolerance still restarts
// a fade. Own fade writes must never feed back into that external-event state.
MMDeviceEnumerator.Session = new();
long eventTick = 0;
var fadeEvents = new PlaybackVolumeEvents();
using (var observer = NativePlaybackSession.Open("test-endpoint", "test-session", fadeEvents))
using (var playback = new PlaybackSessions(() => eventTick, volumeEvents: fadeEvents))
{
    var settings = new DuckingSettings(90, 1000, 0, true);
    playback.Update(true, settings);
    eventTick = 1000;
    playback.Update(true, settings);
    var volume = MMDeviceEnumerator.Session.SimpleAudioVolume;
    Check(fadeEvents.Latest("test-endpoint:test-session") is null, "Own fade callback was external.");
    volume.Volume = 0.101f;
    eventTick = 1100;
    playback.Update(true, settings);
    Check(volume.Volume == 0.101f, "The external event below the polling tolerance was ignored.");
    eventTick = 2100;
    playback.Update(true, settings);
    Check(Math.Abs(volume.Volume - 0.1f) < 0.00001f, "External callback prevented fade completion.");
    playback.RestoreAll(0);
    Check(volume.Volume == 1f && volume.LastWriteContext == fadeEvents.EventContext,
        "Restoration did not preserve the startup snapshot and our EventContext.");
}
Console.WriteLine("PASS: own/external EventContext, volume/mute notifications, session isolation, disposal and sub-tolerance external fades.");

MMDeviceEnumerator.Session = new();
using (var playback = new PlaybackSessions())
{
    playback.Update(true, new DuckingSettings(90, 0, 0, true));
    Check(NativePlaybackSession.ActiveCount == 1, "The test did not acquire a native session handle.");
    MMDeviceEnumerator.Session.State = NAudio.CoreAudioApi.Interfaces.AudioSessionState.AudioSessionStateExpired;
    playback.Update(false, new DuckingSettings());
    Check(!playback.HasOwnedSessions && NativePlaybackSession.ActiveCount == 0,
        "An expired session retained a native handle or an unfinished fade.");
}
Console.WriteLine("PASS: expired sessions release native handles without waiting for application exit.");

// Exercise the actual gate and session loop together with the local listening
// profile: room noise must release the gate and every new voice must lower again.
MMDeviceEnumerator.Session = new();
long gateTick = 0;
using (var playback = new PlaybackSessions(() => gateTick))
{
    var settings = new DuckingSettings(90, 300, 2000, true)
    {
        ThresholdDecibels = -37.4, HysteresisDecibels = 3, RestoreDelayMilliseconds = 650
    };
    var gate = new VoiceGate(() => gateTick);
    void Advance(double db, int milliseconds)
    {
        for (var elapsed = 0; elapsed < milliseconds; elapsed += 50, gateTick += 50)
        {
            gate.Configure(settings);
            gate.Observe(Math.Pow(10, db / 20));
            playback.Update(gate.IsSpeaking, settings);
        }
    }
    for (var cycle = 0; cycle < 100; cycle++)
    {
        Advance(-32, 400);
        Check(Math.Abs(MMDeviceEnumerator.Session.SimpleAudioVolume.Volume - 0.1f) < 0.00001f,
            $"Voice trigger {cycle} did not lower playback to 10%.");
        Advance(-43, 1000);
        var rising = MMDeviceEnumerator.Session.SimpleAudioVolume.Volume;
        Check(rising > 0.1f && rising < 1f, "Playback did not start restoring after room noise released the gate.");
        Advance(-32, 100);
        Check(MMDeviceEnumerator.Session.SimpleAudioVolume.Volume < rising,
            "New voice did not reverse an unfinished restoration immediately.");
        Advance(-32, 300);
        Check(Math.Abs(MMDeviceEnumerator.Session.SimpleAudioVolume.Volume - 0.1f) < 0.00001f,
            "An interrupted restoration compounded the ducking target.");
        Advance(-43, 2800);
        Check(MMDeviceEnumerator.Session.SimpleAudioVolume.Volume == 1f && !gate.IsSpeaking,
            "Room noise did not permit exact full restoration.");
    }
    Console.WriteLine("PASS: 100 voice/noise cycles, repeat triggers, immediate reversal during fade back, exact 100% restoration.");
}

// Worker scheduling is not exact. A final step smaller than the ownership
// tolerance must still reach the original level before the next cycle saves it.
MMDeviceEnumerator.Session = new();
long repeatedTick = 0;
using (var playback = new PlaybackSessions(() => repeatedTick))
{
    var settings = new DuckingSettings(95, 3000, 15000, true);
    for (var cycle = 0; cycle < 100; cycle++)
    {
        var end = repeatedTick + 3200;
        for (; repeatedTick < end; repeatedTick += 53) playback.Update(true, settings);
        end = repeatedTick + 15200;
        for (; repeatedTick < end; repeatedTick += 53) playback.Update(false, settings);
    }
    var actual = MMDeviceEnumerator.Session.SimpleAudioVolume.Volume;
    Console.WriteLine($"100 completed cycles: volume={actual:P4}");
    Check(Math.Abs(actual - 1f) < 0.00001f, "Repeated completed fades ratcheted the original volume down.");
}

foreach (var original in new[] { 1f, 0.65f, 0.12f })
foreach (var fadeUp in new[] { 0, 2000, 15000, 30000 })
{
    MMDeviceEnumerator.Session = new();
    var volume = MMDeviceEnumerator.Session.SimpleAudioVolume;
    volume.Volume = original;
    long now = 0;
    using var playback = new PlaybackSessions(() => now);
    var settings = new DuckingSettings(95, 3000, fadeUp, true);
    // Multiple interruptions must never turn a partly restored level into a new baseline.
    for (var cycle = 0; cycle < 4; cycle++)
    {
        for (var elapsed = 0; elapsed <= 3200; elapsed += 50, now += 50)
            playback.Update(true, settings);
        for (var elapsed = 0; elapsed <= (cycle < 3 ? 500 : fadeUp + 100); elapsed += 50, now += 50)
            playback.Update(false, settings);
    }
    Check(Math.Abs(volume.Volume - original) < 0.00001f,
        $"Restore failed: baseline={original:P0}, fade={fadeUp}ms, actual={volume.Volume:P2}");
    Check(!playback.HasOwnedSessions, "Completed restoration still owns the session.");
    Console.WriteLine($"PASS: baseline={original:P0}, fade={fadeUp}ms, interrupted cycles restore exactly.");
}

foreach (var manualDuringRestore in new[] { false, true })
{
    MMDeviceEnumerator.Session = new();
    var volume = MMDeviceEnumerator.Session.SimpleAudioVolume;
    long now = 0;
    using var playback = new PlaybackSessions(() => now);
    var settings = new DuckingSettings(95, 0, 15000, true);
    playback.Update(true, settings);
    if (manualDuringRestore) playback.Update(false, settings);
    volume.Volume = 0.42f;
    now += 50;
    playback.Update(!manualDuringRestore, settings);
    playback.RestoreAll(0);
    Check(volume.Volume == 1f && !playback.HasOwnedSessions,
        "An external mixer edit replaced the app-lifetime snapshot.");
}

// Snapshot inactive and silent sessions at launch, before microphone Enable.
// Worker replacement models Disable/Enable and microphone-device changes.
foreach (var original in new[] { 1f, 0.65f, 0.12f })
{
    MMDeviceEnumerator.Session = new();
    var session = MMDeviceEnumerator.Session;
    var volume = session.SimpleAudioVolume;
    volume.Volume = original;
    session.State = NAudio.CoreAudioApi.Interfaces.AudioSessionState.AudioSessionStateInactive;
    session.AudioMeterInformation.MasterPeakValue = 0;
    var snapshot = new PlaybackVolumeSnapshot();
    using (var startup = new PlaybackSessions(snapshot: snapshot)) startup.CaptureSnapshot();
    volume.Volume = 0.42f;
    session.State = NAudio.CoreAudioApi.Interfaces.AudioSessionState.AudioSessionStateActive;
    session.AudioMeterInformation.MasterPeakValue = 0.5f;
    var settings = new DuckingSettings(90, 0, 0, true);
    for (var enable = 0; enable < 3; enable++)
    {
        using var playback = new PlaybackSessions(snapshot: snapshot);
        playback.CaptureSnapshot(); // Re-observing must never overwrite a captured value.
        playback.Update(true, settings);
        Check(Math.Abs(volume.Volume - original * 0.1f) < 0.00001f,
            "Ducking used a later mixer level instead of the startup snapshot.");
        playback.RestoreAll(0);
        Check(volume.Volume == original, "A worker restart discarded the startup snapshot.");
        volume.Volume = 0.42f;
    }
    Console.WriteLine($"PASS: inactive startup snapshot={original:P0}, external edits, three worker restarts.");
}

// A session created later gets its own first-observed value, independently of
// existing sessions, even when it disappears and is subsequently enumerated again.
MMDeviceEnumerator.Session = new();
using (var playback = new PlaybackSessions())
{
    var first = MMDeviceEnumerator.Session;
    playback.CaptureSnapshot();
    var second = new AudioSessionControl { GetSessionInstanceIdentifier = "late-session" };
    second.SimpleAudioVolume.Volume = 0.65f;
    MMDeviceEnumerator.Session = second;
    playback.CaptureSnapshot();
    second.SimpleAudioVolume.Volume = 0.3f;
    var settings = new DuckingSettings(90, 0, 0, true);
    playback.Update(true, settings);
    playback.RestoreAll(0);
    Check(second.SimpleAudioVolume.Volume == 0.65f, "A new session lost its first-observed volume.");
    MMDeviceEnumerator.Session = first;
    first.SimpleAudioVolume.Volume = 0.4f;
    playback.Update(true, settings);
    playback.RestoreAll(0);
    Check(first.SimpleAudioVolume.Volume == 1f, "Session disappearance discarded the startup snapshot.");
}

foreach (var muted in new[] { false, true })
{
    MMDeviceEnumerator.Session = new();
    var volume = MMDeviceEnumerator.Session.SimpleAudioVolume;
    volume.Mute = muted;
    MMDeviceEnumerator.Session.AudioMeterInformation.MasterPeakValue = muted ? 0.5f : 0;
    using var playback = new PlaybackSessions();
    playback.Update(true, new DuckingSettings(95, 0, 15000, true));
    Check(volume.Writes == 0 && !playback.HasOwnedSessions, "A muted or silent session was changed.");
}

MMDeviceEnumerator.Session = new();
using (var playback = new PlaybackSessions())
{
    playback.Update(true, new DuckingSettings(95, 0, 15000, true));
    playback.RestoreAll(0);
    Check(MMDeviceEnumerator.Session.SimpleAudioVolume.Volume == 1f, "Disable did not restore immediately.");
}
Console.WriteLine("PASS: immutable snapshots, new sessions, external edits, muted/silent sessions and immediate Disable restoration.");
'@ | Set-Content (Join-Path $testRoot 'Program.cs') -Encoding utf8

& dotnet run --project (Join-Path $testRoot 'RestorationTests.csproj') -c Debug `
    "-p:BaseOutputPath=$testRoot\bin\" "-p:BaseIntermediateOutputPath=$testRoot\obj\" `
    "-p:MSBuildProjectExtensionsPath=$testRoot\obj\"
if ($LASTEXITCODE -ne 0) { throw 'Playback restoration regression checks failed.' }
