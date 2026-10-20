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
    <Compile Include="$sourceRoot\AtomicJsonFile.cs" Link="AtomicJsonFile.cs" />
    <Compile Include="$sourceRoot\SettingsStore.cs" Link="SettingsStore.cs" />
    <Compile Include="$sourceRoot\Audio\DuckingSettings.cs" Link="DuckingSettings.cs" />
    <Compile Include="$sourceRoot\Audio\PlaybackIdentity.cs" Link="PlaybackIdentity.cs" />
    <Compile Include="$sourceRoot\Audio\VoiceGate.cs" Link="VoiceGate.cs" />
    <Compile Include="$sourceRoot\Licensing\LicensePolicy.cs" Link="LicensePolicy.cs" />
  </ItemGroup>
</Project>
"@ | Set-Content -LiteralPath (Join-Path $testRoot 'PersistenceTests.csproj') -Encoding utf8

@'
using System.Diagnostics;
using VoiceDucker;
using VoiceDucker.Audio;
using VoiceDucker.Licensing;

var path = Path.Combine(AppContext.BaseDirectory, "isolated-settings", args[1], "settings.json");
var expected = new DuckingSettings(73, 450, 12300, true)
    {
        BlacklistedProcesses = ["Spotify", "FluentFlyout"], MicrophoneGainDecibels = 27,
        MicrophoneDeviceId = "isolated-test-microphone", RestoreDelayMilliseconds = 1750,
        ThresholdDecibels = -47.3, HysteresisDecibels = 9
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

var licenseNow = new DateTimeOffset(2026, 10, 12, 0, 0, 0, TimeSpan.Zero);
var trialEnd = licenseNow.AddDays(30);
Check(LicensePolicy.WindowTitle("VoiceDucker", LicenseState.Trial, licenseNow.AddDays(1), licenseNow) ==
    "VoiceDucker - Free Trial, 1 day remaining" &&
    LicensePolicy.WindowTitle("VoiceDucker", LicenseState.Trial, licenseNow.AddDays(2), licenseNow) ==
    "VoiceDucker - Free Trial, 2 days remaining" &&
    LicensePolicy.WindowTitle("VoiceDucker", LicenseState.Expired, trialEnd, trialEnd) ==
    "VoiceDucker - Trial Expired" &&
    LicensePolicy.WindowTitle("VoiceDucker", LicenseState.Full, trialEnd, licenseNow) == "VoiceDucker",
    "Trial title or full-license title is incorrect.");
Check(LicensePolicy.Evaluate(true, true, trialEnd, licenseNow) == LicenseState.Trial,
    "An active unexpired trial was rejected.");
Check(LicensePolicy.Evaluate(true, true, trialEnd, trialEnd) == LicenseState.Expired &&
    !LicensePolicy.CanUse(LicenseState.Trial, trialEnd, trialEnd),
    "The trial remained usable at its exact expiration boundary.");
Check(LicensePolicy.Evaluate(false, false, trialEnd, licenseNow) == LicenseState.Expired &&
    LicensePolicy.Evaluate(false, true, trialEnd, licenseNow) == LicenseState.Expired,
    "Inactive licenses were granted access.");
Check(LicensePolicy.Evaluate(true, false, licenseNow.AddDays(-1), licenseNow) == LicenseState.Full &&
    LicensePolicy.CanUse(LicenseState.Full, licenseNow.AddDays(-1), licenseNow),
    "A full license was incorrectly constrained by a trial expiration.");
Check(!LicensePolicy.CanUse(LicenseState.Checking, trialEnd, licenseNow) &&
    !LicensePolicy.CanUse(LicenseState.Unavailable, trialEnd, licenseNow) &&
    !LicensePolicy.CanUse(LicenseState.Expired, trialEnd, licenseNow) &&
    LicensePolicy.CanUse(LicenseState.Unrestricted, default, licenseNow),
    "Unverified Store states or local development licensing were handled incorrectly.");
Check(LicensePolicy.CanUse(LicenseState.Trial, trialEnd, trialEnd.AddTicks(-1)) &&
    !LicensePolicy.CanUse(LicenseState.Trial, trialEnd, trialEnd.AddTicks(1)),
    "A running trial failed to enforce the expiration boundary.");

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
    expected with { ThresholdDecibels = -60.1 },
    expected with { HysteresisDecibels = -1 },
    expected with { HysteresisDecibels = 13 },
    expected with { ThresholdDecibels = 0.1 },
    expected with { ThresholdDecibels = double.NaN },
    expected with { ThresholdDecibels = double.PositiveInfinity },
    expected with { ThresholdDecibels = double.NegativeInfinity },
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
foreach (var threshold in new[] { -60.0, 0.0 })
{
    var boundary = expected with { ThresholdDecibels = threshold };
    SettingsStore.Save(boundary, path);
    Check(SameSettings(SettingsStore.Load(path, out error), boundary) && error is null,
        "A threshold boundary did not survive saving and reloading.");
}
SettingsStore.Save(replacement, path);
Check(SameSettings(SettingsStore.Load(path, out error), replacement) && error is null,
    "An immediate replacement did not persist all settings.");
Check(!SettingsStore.Load(path, out error).IsProcessExcluded("Spotify"),
    "Restoring a hidden process did not survive reloading settings.");
Check(!File.Exists(path + ".tmp"), "A successful save left its temporary file behind.");

var defaults = new DuckingSettings();
SettingsStore.Save(defaults, path);
Check(SameSettings(SettingsStore.Load(path, out error), defaults) && error is null,
    "Current defaults did not survive saving and reloading.");

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
    "{\"RestoreDelayMilliseconds\":10001}",
    "{\"ThresholdDecibels\":-60.1}",
    "{\"ThresholdDecibels\":0.1}"
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
gate.Observe(gate.Threshold);
Check(gate.IsSpeaking, "Sound at the threshold did not open the sound gate.");
now += 649;
gate.Observe(gate.Threshold / 2);
Check(gate.IsSpeaking, "A brief pause was not held.");
now += 2;
Check(!gate.IsSpeaking, "Playback remained held after the pause deadline.");
gate.Observe(gate.Threshold * 2);
now += 500;
gate.Observe(gate.Threshold);
gate.Observe(0);
now += 500;
Check(gate.IsSpeaking, "Resumed sound did not reset the pause timer.");
now += 151;
Check(!gate.IsSpeaking, "A renewed pause did not expire.");
gate.HoldMilliseconds = 0;
gate.Observe(gate.Threshold);
Check(gate.IsSpeaking, "Zero delay suppressed an active threshold observation.");
now += 50;
Check(gate.IsSpeaking, "Zero delay released sustained sound between capture buffers.");
gate.Observe(0);
Check(!gate.IsSpeaking, "Zero delay added a pause after sound stopped.");
gate.Observe(gate.Threshold);
gate.Observe(0);
Check(!gate.IsSpeaking, "Zero delay did not release silence in the same clock tick.");
gate.HoldMilliseconds = 650;
gate.Observe(gate.Threshold);
gate.Observe(0);
now += 300;
gate.HoldMilliseconds = 200;
Check(!gate.IsSpeaking, "Shortening the delay did not release an existing pause.");
gate.HoldMilliseconds = 1000;
Check(gate.IsSpeaking, "Lengthening the delay did not apply to the most recent sound.");
now += 701;
Check(!gate.IsSpeaking, "The lengthened pause did not expire.");
// A fixed synthetic input must change classification when the user changes threshold.
var adjustable = new VoiceGate(() => now) { HoldMilliseconds = 0, HysteresisDecibels = 0 };
adjustable.Observe(0.01);
Check(!adjustable.IsSpeaking, "A quiet signal exceeded the default threshold.");
adjustable.Threshold = (expected with { ThresholdDecibels = -46 }).GetThresholdAmplitude();
Check(adjustable.IsSpeaking, "Lowering the threshold did not detect the existing quieter signal.");
adjustable.Threshold = (expected with { ThresholdDecibels = -20 }).GetThresholdAmplitude();
Check(!adjustable.IsSpeaking, "Raising the threshold did not release a quieter signal.");
foreach (var decibels in new[] { -60.0, 0.0 })
{
    adjustable.Threshold = (expected with { ThresholdDecibels = decibels }).GetThresholdAmplitude();
    adjustable.Observe(adjustable.Threshold * 0.99);
    Check(!adjustable.IsSpeaking, "A signal below the selected threshold opened the gate.");
    adjustable.Observe(adjustable.Threshold);
    Check(adjustable.IsSpeaking, "A signal at the selected threshold did not open the gate.");
}
foreach (var invalid in new[] { 0.0, -1.0, 1.01, double.NaN, double.PositiveInfinity })
{
    try
    {
        adjustable.Threshold = invalid;
        throw new InvalidOperationException("An invalid gate threshold was accepted.");
    }
    catch (ArgumentOutOfRangeException) { }
}
double Level(double db) => Math.Pow(10, db / 20);
var stableSettings = new DuckingSettings { ThresholdDecibels = -30, HysteresisDecibels = 6, RestoreDelayMilliseconds = 650 };
var stable = new VoiceGate(() => now);
stable.Configure(stableSettings);
stable.Observe(Level(-33));
Check(!stable.IsSpeaking, "A quiet startup inside the hysteresis band opened detection.");
stable.Observe(Level(-30));
Check(stable.IsSpeaking, "The trigger boundary did not activate detection.");
for (var index = 0; index < 40; index++)
{
    now += 100;
    stable.Observe(Level(index % 2 == 0 ? -31 : -29));
    Check(stable.IsSpeaking, "Jitter around the trigger interrupted active detection.");
}
// Use the same amplitude calculation as the detector to test the exact release boundary.
stable.Observe(stableSettings.GetThresholdAmplitude() * Math.Pow(10, -6 / 20.0));
Check(stable.IsSpeaking, "The release boundary should still sustain detection.");
stable.Observe(Level(-36.1));
Check(stable.IsSpeaking, "Crossing the release threshold skipped the configured hold.");
now += 649;
stable.Configure(stableSettings);
Check(stable.IsSpeaking, "The release hold expired too early.");
now += 2;
stable.Configure(stableSettings);
Check(!stable.IsSpeaking, "Repeated configuration refreshed the hold without new sound.");
stable.Observe(Level(-33));
Check(!stable.IsSpeaking, "Returning into the band reactivated a closed detector.");
stable.Observe(Level(-29));
stable.Observe(Level(-33));
stable.Configure(stableSettings with { HysteresisDecibels = 0, RestoreDelayMilliseconds = 0 });
Check(!stable.IsSpeaking, "Disabling hysteresis did not restore single-threshold detection immediately.");
stable.Configure(stableSettings with { RestoreDelayMilliseconds = 0 });
stable.Observe(Level(-29));
stable.Observe(Level(-33));
stable.Configure(stableSettings with { ThresholdDecibels = -20, RestoreDelayMilliseconds = 0 });
Check(!stable.IsSpeaking, "A live trigger change did not recompute the release threshold.");
foreach (var gap in new[] { 0, 12 })
{
    var boundary = expected with { HysteresisDecibels = gap };
    SettingsStore.Save(boundary, path);
    Check(SameSettings(SettingsStore.Load(path, out error), boundary) && error is null,
        "A valid hysteresis boundary did not survive a settings reload.");
}
Console.WriteLine("PASS: persistence, current defaults, validation and pause behavior; hysteresis quiet startup, trigger/release boundaries, jitter, timed release, no reopening inside the band, live threshold/gap changes and disabled mode. No microphone or playback capture.");
'@ | Set-Content -LiteralPath (Join-Path $testRoot 'Program.cs') -Encoding utf8

& dotnet build (Join-Path $testRoot 'PersistenceTests.csproj') -c Release -p:ImportDirectoryBuildProps=false --nologo
if ($LASTEXITCODE -ne 0) { throw 'Persistence test build failed.' }
$testAssembly = Join-Path $testRoot 'bin\Release\net10.0\PersistenceTests.dll'
$runId = [Guid]::NewGuid().ToString('N')
& dotnet $testAssembly write-and-kill $runId
if ($LASTEXITCODE -eq 0) { throw 'The write test did not terminate abruptly.' }
& dotnet $testAssembly verify $runId
if ($LASTEXITCODE -ne 0) { throw 'Persistence checks failed.' }
