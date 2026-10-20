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
    public enum AudioSessionState { AudioSessionStateActive }
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
        public string GetSessionInstanceIdentifier => "test-session";
        public uint GetProcessID => (uint)Environment.ProcessId;
        public Interfaces.AudioSessionState State => Interfaces.AudioSessionState.AudioSessionStateActive;
        public SimpleAudioVolume SimpleAudioVolume { get; } = new();
        public AudioMeterInformation AudioMeterInformation { get; } = new();
    }
    public sealed class SimpleAudioVolume
    {
        private float _volume = 1f;
        public bool Mute { get; set; }
        public float Volume { get => _volume; set { _volume = value; Writes++; } }
        public int Writes { get; private set; }
    }
    public sealed class AudioMeterInformation
    {
        public float MasterPeakValue { get; set; } = 0.5f;
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
    Check(volume.Volume == 0.42f && !playback.HasOwnedSessions, "A manual mixer edit was overwritten.");
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
Console.WriteLine("PASS: manual edits, muted/silent sessions and immediate Disable restoration.");
'@ | Set-Content (Join-Path $testRoot 'Program.cs') -Encoding utf8

& dotnet run --project (Join-Path $testRoot 'RestorationTests.csproj') -c Debug `
    "-p:BaseOutputPath=$testRoot\bin\" "-p:BaseIntermediateOutputPath=$testRoot\obj\" `
    "-p:MSBuildProjectExtensionsPath=$testRoot\obj\"
if ($LASTEXITCODE -ne 0) { throw 'Playback restoration regression checks failed.' }
