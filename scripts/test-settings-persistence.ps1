# VBWR B
# Project: VoiceDucker
# Creator: Umberto Giacobbi | https://umbertogiacobbi.biz
# Modified with AI: OpenAI Codex; immediate settings persistence, 2026-10-10.
# Human guidance: Save settings immediately and preserve them across restarts.
# Evidence: Isolated persistence checks; no microphone or playback access.
# Copyright (c) 2026 Umberto Giacobbi
# SPDX-License-Identifier: MIT
# VBWR E

[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$projectRoot = [System.IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$testRoot = Join-Path $projectRoot 'artifacts\settings-persistence-tests'
New-Item -ItemType Directory -Path $testRoot -Force | Out-Null
$sourceRoot = [System.Security.SecurityElement]::Escape($projectRoot)
@"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="$sourceRoot\SettingsStore.cs" Link="SettingsStore.cs" />
    <Compile Include="$sourceRoot\Audio\DuckingSettings.cs" Link="DuckingSettings.cs" />
    <Compile Include="$sourceRoot\Audio\PlaybackIdentity.cs" Link="PlaybackIdentity.cs" />
    <Compile Include="$sourceRoot\Audio\VoiceGate.cs" Link="VoiceGate.cs" />
  </ItemGroup>
</Project>
"@ | Set-Content -LiteralPath (Join-Path $testRoot 'PersistenceTests.csproj') -Encoding utf8

@'
using System.Diagnostics;
using VoiceDucker;
using VoiceDucker.Audio;

var path = Path.Combine(AppContext.BaseDirectory, "isolated-settings", args[1], "settings.json");
var expected = new DuckingSettings(73, 450, 12300, true)
    {
        BlacklistedProcesses = ["Spotify", "FluentFlyout"], MicrophoneGainDecibels = 27,
        MicrophoneDeviceId = "isolated-test-microphone", RestoreDelayMilliseconds = 1750
    };
if (args is ["write-and-kill", _])
{
    SettingsStore.Save(expected, path);
    // No graceful exit or shutdown save is allowed to help this test pass.
    Process.GetCurrentProcess().Kill();
    Thread.Sleep(Timeout.Infinite);
}

void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

var loaded = SettingsStore.Load(path, out var error);
bool SameSettings(DuckingSettings left, DuckingSettings right) =>
    (left with { BlacklistedProcesses = right.BlacklistedProcesses }) == right &&
    left.BlacklistedProcesses.SequenceEqual(right.BlacklistedProcesses);
Check(error is null && SameSettings(loaded, expected), "Settings did not survive abrupt process termination.");
Check(loaded.IsProcessExcluded("SPOTIFY.exe") && loaded.IsProcessExcluded("fluentflyout") &&
    loaded.IsProcessExcluded("VoiceDucker") && loaded.IsProcessExcluded("voiceducker.EXE") &&
    !loaded.IsProcessExcluded("SpotifyHelper") && !loaded.IsProcessExcluded("VoiceDuckerHelper"),
    "Process exclusions did not use exact, case-insensitive executable names.");

File.WriteAllText(path + ".tmp", "interrupted replacement");
Check(SameSettings(SettingsStore.Load(path, out error), expected) && error is null,
    "An interrupted temporary write changed the last saved settings.");

try
{
    SettingsStore.Save(expected with { ReductionPercent = 101 }, path);
    throw new InvalidOperationException("Invalid settings were accepted.");
}
catch (ArgumentOutOfRangeException) { }
Check(SameSettings(SettingsStore.Load(path, out error), expected) && error is null,
    "Rejected values overwrote the previous settings.");

foreach (var invalid in new[]
{
    expected with { RestoreDelayMilliseconds = -1 },
    expected with { RestoreDelayMilliseconds = 10001 },
    expected with { MicrophoneDeviceId = null! }
})
{
    try
    {
        SettingsStore.Save(invalid, path);
        throw new InvalidOperationException("An invalid microphone choice or restore delay was accepted.");
    }
    catch (ArgumentOutOfRangeException) { }
    Check(SameSettings(SettingsStore.Load(path, out error), expected) && error is null,
        "Rejected microphone settings overwrote the previous settings.");
}

foreach (var delay in new[] { 0, 10000 })
{
    var validBoundary = expected with { RestoreDelayMilliseconds = delay, MicrophoneDeviceId = "" };
    SettingsStore.Save(validBoundary, path);
    Check(SameSettings(SettingsStore.Load(path, out error), validBoundary) && error is null,
        "A valid delay boundary or default microphone choice was rejected.");
}

var replacement = new DuckingSettings(0, 5000, 30000, false);
SettingsStore.Save(replacement, path);
Check(SameSettings(SettingsStore.Load(path, out error), replacement) && error is null,
    "An immediate replacement did not persist all settings.");
Check(!SettingsStore.Load(path, out error).IsProcessExcluded("Spotify"),
    "Restoring a hidden process did not survive reloading settings.");
Check(!File.Exists(path + ".tmp"), "A successful save left its temporary file behind.");

File.WriteAllText(path, "{\"ReductionPercent\":42,\"FadeDownMilliseconds\":200,\"FadeUpMilliseconds\":10000}");
Check(SameSettings(SettingsStore.Load(path, out error), new DuckingSettings(42, 200, 10000, false))
    && error is null, "Existing settings files are no longer compatible.");
var legacy = SettingsStore.Load(path, out error);
Check(legacy.MicrophoneDeviceId == "" && legacy.RestoreDelayMilliseconds == 650,
    "Legacy settings did not retain the communications microphone and 650 ms pause defaults.");

File.WriteAllText(path, "invalid JSON");
Check(SameSettings(SettingsStore.Load(path, out error), new DuckingSettings()) && error is not null,
    "Corrupt settings did not report an error and fall back to defaults.");
Check(SameSettings(SettingsStore.Load(path + ".missing", out error), new DuckingSettings()) && error is null,
    "A first launch did not use defaults.");

foreach (var invalidBlacklist in new[] { "null", "[\"\"]", "[null]" })
{
    File.WriteAllText(path, "{\"BlacklistedProcesses\":" + invalidBlacklist + "}");
    Check(SameSettings(SettingsStore.Load(path, out error), new DuckingSettings()) && error is not null,
        "An invalid blacklist did not fall back safely to defaults.");
}

foreach (var invalidAudioSettings in new[]
{
    "{\"MicrophoneDeviceId\":null}",
    "{\"RestoreDelayMilliseconds\":-1}",
    "{\"RestoreDelayMilliseconds\":10001}"
})
{
    File.WriteAllText(path, invalidAudioSettings);
    Check(SameSettings(SettingsStore.Load(path, out error), new DuckingSettings()) && error is not null,
        "Invalid saved microphone settings did not report an error and fall back to defaults.");
}

var blockedPath = Path.Combine(Path.GetDirectoryName(path)!, "blocked.json");
Directory.CreateDirectory(blockedPath);
try
{
    SettingsStore.Save(expected, blockedPath);
    throw new InvalidOperationException("A blocked replacement unexpectedly succeeded.");
}
catch (IOException) { }
catch (UnauthorizedAccessException) { }
Check(!File.Exists(blockedPath + ".tmp"), "A failed save left its temporary file behind.");

// Deterministic time and synthetic levels exercise pause behavior without audio devices.
long now = 1000;
var gate = new VoiceGate(() => now) { HoldMilliseconds = 650 };
Check(!gate.IsSpeaking, "A quiet startup incorrectly lowered playback.");
gate.Observe(0);
Check(!gate.IsSpeaking, "Silence incorrectly opened the sound gate.");
gate.Observe(VoiceGate.Threshold);
Check(gate.IsSpeaking, "Sound at the threshold did not open the sound gate.");
now += 649;
gate.Observe(VoiceGate.Threshold / 2);
Check(gate.IsSpeaking, "A brief pause was not held.");
now += 2;
Check(!gate.IsSpeaking, "Playback remained held after the pause deadline.");
gate.Observe(VoiceGate.Threshold * 2);
now += 500;
gate.Observe(VoiceGate.Threshold);
gate.Observe(0);
now += 500;
Check(gate.IsSpeaking, "Resumed sound did not reset the pause timer.");
now += 151;
Check(!gate.IsSpeaking, "A renewed pause did not expire.");
gate.HoldMilliseconds = 0;
gate.Observe(VoiceGate.Threshold);
Check(gate.IsSpeaking, "Zero delay suppressed an active threshold observation.");
now += 50;
Check(gate.IsSpeaking, "Zero delay released sustained sound between capture buffers.");
gate.Observe(0);
Check(!gate.IsSpeaking, "Zero delay added a pause after sound stopped.");
gate.Observe(VoiceGate.Threshold);
gate.Observe(0);
Check(!gate.IsSpeaking, "Zero delay did not release silence in the same clock tick.");
gate.HoldMilliseconds = 650;
gate.Observe(VoiceGate.Threshold);
gate.Observe(0);
now += 300;
gate.HoldMilliseconds = 200;
Check(!gate.IsSpeaking, "Shortening the delay did not release an existing pause.");
gate.HoldMilliseconds = 1000;
Check(gate.IsSpeaking, "Lengthening the delay did not apply to the most recent sound.");
now += 701;
Check(!gate.IsSpeaking, "The lengthened pause did not expire.");
Console.WriteLine("PASS: abrupt restart including microphone choice and pause delay; delay bounds/null input, legacy defaults, exclusions, interrupted write, replacement, corrupt/missing files, failed-write cleanup; deterministic quiet startup, brief pause, expiry, resumed sound, zero delay, live delay changes. No microphone or playback capture.");
'@ | Set-Content -LiteralPath (Join-Path $testRoot 'Program.cs') -Encoding utf8

& dotnet build (Join-Path $testRoot 'PersistenceTests.csproj') -c Release -p:ImportDirectoryBuildProps=false --nologo
if ($LASTEXITCODE -ne 0) { throw 'Persistence test build failed.' }
$testAssembly = Join-Path $testRoot 'bin\Release\net10.0\PersistenceTests.dll'
$runId = [Guid]::NewGuid().ToString('N')
& dotnet $testAssembly write-and-kill $runId
if ($LASTEXITCODE -eq 0) { throw 'The write test did not terminate abruptly.' }
& dotnet $testAssembly verify $runId
if ($LASTEXITCODE -ne 0) { throw 'Persistence checks failed.' }
