# VoiceDucker - Copyright (c) 2026 Umberto Giacobbi. MIT license.
# Exercises native Core Audio callbacks on this test process's silent session only.
[CmdletBinding()]
param(
    [string]$InstalledAppDiagnosticsPath,
    [int]$ShutdownInstalledAppProcessId
)

$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$testRoot = Join-Path $projectRoot 'artifacts\volume-notification-tests'
New-Item -ItemType Directory -Path $testRoot -Force | Out-Null
$sourceRoot = [Security.SecurityElement]::Escape($projectRoot)
$packageRoot = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $env:USERPROFILE '.nuget\packages' }
$wasapi = Join-Path $packageRoot 'naudio.wasapi\2.2.1\lib\netstandard2.0\NAudio.Wasapi.dll'
$core = Join-Path $packageRoot 'naudio.core\2.2.1\lib\netstandard2.0\NAudio.Core.dll'
foreach ($dependency in @($wasapi, $core)) {
    if (-not (Test-Path -LiteralPath $dependency)) {
        throw "Restore the application's existing pinned NAudio dependency first: $dependency"
    }
}
$wasapiXml = [Security.SecurityElement]::Escape($wasapi)
$coreXml = [Security.SecurityElement]::Escape($core)
@"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0-windows</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="Program.cs" />
    <Reference Include="NAudio.Wasapi"><HintPath>$wasapiXml</HintPath></Reference>
    <Reference Include="NAudio.Core"><HintPath>$coreXml</HintPath></Reference>
    <Compile Include="$sourceRoot\Audio\PlaybackVolumeEvents.cs" Link="PlaybackVolumeEvents.cs" />
    <Compile Include="$sourceRoot\Audio\NativePlaybackSession.cs" Link="NativePlaybackSession.cs" />
  </ItemGroup>
</Project>
"@ | Set-Content -LiteralPath (Join-Path $testRoot 'NotificationTests.csproj') -Encoding utf8

@'
using NAudio.CoreAudioApi;
using NAudio.Wave;
using VoiceDucker.Audio;
using System.Diagnostics;

void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

void WaitFor(Func<bool> condition, string message)
{
    Check(SpinWait.SpinUntil(condition, TimeSpan.FromSeconds(5)), message);
}

string ReadDiagnostics()
{
    if (args.Length == 0) return string.Empty;
    using var stream = new FileStream(args[0], FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
    using var reader = new StreamReader(stream);
    return reader.ReadToEnd();
}

// This produces zero samples. No microphone is opened and no other process's
// playback or system master volume is changed.
using var devices = new MMDeviceEnumerator();
using var endpoint = devices.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
using var output = new WasapiOut(endpoint, AudioClientShareMode.Shared, false, 100);
output.Init(new SilentProvider());
output.Play();
AudioSessionControl? session = null;
WaitFor(() =>
{
    endpoint.AudioSessionManager.RefreshSessions();
    var sessions = endpoint.AudioSessionManager.Sessions;
    for (var index = 0; index < sessions.Count; index++)
    {
        var candidate = sessions[index];
        if (candidate.GetProcessID == Environment.ProcessId)
        {
            session = candidate;
            return true;
        }
    }
    return false;
}, "The isolated silent playback session did not appear.");

var id = session!.GetSessionInstanceIdentifier;
var key = endpoint.ID + ":" + id;
var original = session.SimpleAudioVolume.Volume;
var originalMute = session.SimpleAudioVolume.Mute;
var events = new PlaybackVolumeEvents();
using var writer = NativePlaybackSession.Open(endpoint.ID, id);
using var observer = NativePlaybackSession.Open(endpoint.ID, id, events);
var externalContext = Guid.NewGuid();
try
{
    if (args.Length > 0)
    {
        WaitFor(() => ReadDiagnostics().Contains("volume-events-subscribed session=" + key),
            "The installed app did not subscribe to the new session while its microphone was off.");
    }
    writer.SetMute(false, events.EventContext);
    writer.SetVolume(0.25f, events.EventContext);
    Thread.Sleep(250);
    Check(events.Latest(key) is null, "Our native EventContext was classified as an external change.");
    writer.SetVolume(0.63f, externalContext);
    WaitFor(() => events.Latest(key) is { Volume: 0.63f, Muted: false },
        "Native OnSimpleVolumeChanged did not deliver the external volume change.");
    var volumeChange = events.Latest(key)!;
    writer.SetMute(true, externalContext);
    WaitFor(() => events.Latest(key) is { Muted: true } change && change.Version > volumeChange.Version,
        "Native OnSimpleVolumeChanged did not deliver the external mute change.");
    var muteChange = events.Latest(key)!;
    writer.SetMute(false, externalContext);
    WaitFor(() => events.Latest(key) is { Muted: false } change && change.Version > muteChange.Version,
        "Native OnSimpleVolumeChanged did not deliver the external unmute change.");

    if (args.Length > 0)
    {
        WaitFor(() => ReadDiagnostics().Contains("external-volume-change session=" + key),
            "The installed app did not receive an external volume/mute notification.");
        Console.WriteLine("PASS: installed app subscribed to a new session and received external changes with microphone off.");
    }
    if (args.Length == 3)
    {
        // Keep external notifications arriving while the UI unregisters and exits.
        using var stopChanges = new CancellationTokenSource();
        var changes = Task.Run(() =>
        {
            var high = false;
            while (!stopChanges.Token.WaitHandle.WaitOne(20))
            {
                writer.SetVolume(high ? 0.64f : 0.62f, externalContext);
                high = !high;
            }
        });
        try
        {
            var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                "System32", "WindowsPowerShell", "v1.0", "powershell.exe"))
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-STA", "-File", args[1],
                "-AppProcessId", args[2], "-DiagnosticsPath", args[0] }) start.ArgumentList.Add(argument);
            using var shutdown = Process.Start(start) ?? throw new InvalidOperationException("Could not start the shutdown check.");
            if (!shutdown.WaitForExit(25000))
            {
                shutdown.Kill(entireProcessTree: true);
                shutdown.WaitForExit();
                throw new InvalidOperationException("The shutdown test helper did not exit promptly.");
            }
            Console.Write(shutdown.StandardOutput.ReadToEnd());
            var error = shutdown.StandardError.ReadToEnd();
            Check(shutdown.ExitCode == 0, "Installed app shutdown failed: " + error);
        }
        finally
        {
            stopChanges.Cancel();
            Check(changes.Wait(TimeSpan.FromSeconds(5)), "The isolated notification generator did not stop.");
        }
    }
    observer.Dispose();
    var lastChange = events.Latest(key);
    writer.SetVolume(0.71f, externalContext);
    Thread.Sleep(250);
    Check(events.Latest(key) == lastChange, "A disposed native subscription still accepted events.");
    Console.WriteLine("PASS: real Core Audio callbacks, own/external EventContext, volume, mute, unmute and unregistration.");
}
finally
{
    writer.SetVolume(original, events.EventContext);
    writer.SetMute(originalMute, events.EventContext);
    output.Stop();
}

sealed class SilentProvider : IWaveProvider
{
    public WaveFormat WaveFormat { get; } = new(48000, 16, 2);
    public int Read(byte[] buffer, int offset, int count)
    {
        Array.Clear(buffer, offset, count);
        return count;
    }
}
'@ | Set-Content -LiteralPath (Join-Path $testRoot 'Program.cs') -Encoding utf8

$arguments = @('run', '--project', (Join-Path $testRoot 'NotificationTests.csproj'), '-c', 'Debug',
    "-p:BaseOutputPath=$testRoot\bin\", "-p:BaseIntermediateOutputPath=$testRoot\obj\",
    "-p:MSBuildProjectExtensionsPath=$testRoot\obj\", '--')
if ($InstalledAppDiagnosticsPath) { $arguments += $InstalledAppDiagnosticsPath }
if ($ShutdownInstalledAppProcessId) {
    if (-not $InstalledAppDiagnosticsPath) { throw 'Installed app diagnostics are required for the shutdown check.' }
    $arguments += @((Join-Path $PSScriptRoot 'test-app-lifetime.ps1'), $ShutdownInstalledAppProcessId.ToString())
}
& dotnet @arguments
if ($LASTEXITCODE -ne 0) { throw 'Native volume notification checks failed.' }
