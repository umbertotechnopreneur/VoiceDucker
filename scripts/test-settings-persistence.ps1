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
  </ItemGroup>
</Project>
"@ | Set-Content -LiteralPath (Join-Path $testRoot 'PersistenceTests.csproj') -Encoding utf8

@'
using System.Diagnostics;
using VoiceDucker;
using VoiceDucker.Audio;

var path = Path.Combine(AppContext.BaseDirectory, "isolated-settings", args[1], "settings.json");
var expected = new DuckingSettings(73, 450, 12300, true, ShowVisualizers: false)
    { BlacklistedProcesses = ["Spotify", "FluentFlyout"] };
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

var replacement = new DuckingSettings(0, 5000, 30000, false, ShowVisualizers: true);
SettingsStore.Save(replacement, path);
Check(SameSettings(SettingsStore.Load(path, out error), replacement) && error is null,
    "An immediate replacement did not persist all settings.");
Check(!SettingsStore.Load(path, out error).IsProcessExcluded("Spotify"),
    "Restoring a hidden process did not survive reloading settings.");
Check(!File.Exists(path + ".tmp"), "A successful save left its temporary file behind.");

File.WriteAllText(path, "{\"ReductionPercent\":42,\"FadeDownMilliseconds\":200,\"FadeUpMilliseconds\":10000}");
Check(SameSettings(SettingsStore.Load(path, out error), new DuckingSettings(42, 200, 10000, false))
    && error is null, "Existing settings files are no longer compatible.");

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
Console.WriteLine("PASS: abrupt restart including blacklist, exact process-name exclusions, restore, interrupted write, invalid values, replacement, legacy files, corrupt/missing files, failed-write cleanup.");
'@ | Set-Content -LiteralPath (Join-Path $testRoot 'Program.cs') -Encoding utf8

& dotnet build (Join-Path $testRoot 'PersistenceTests.csproj') -c Release -p:ImportDirectoryBuildProps=false --nologo
if ($LASTEXITCODE -ne 0) { throw 'Persistence test build failed.' }
$testAssembly = Join-Path $testRoot 'bin\Release\net10.0\PersistenceTests.dll'
$runId = [Guid]::NewGuid().ToString('N')
& dotnet $testAssembly write-and-kill $runId
if ($LASTEXITCODE -eq 0) { throw 'The write test did not terminate abruptly.' }
& dotnet $testAssembly verify $runId
if ($LASTEXITCODE -ne 0) { throw 'Persistence checks failed.' }
