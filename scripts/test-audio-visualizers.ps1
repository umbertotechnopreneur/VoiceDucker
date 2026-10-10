# VBWR B
# Project: VoiceDucker
# Creator: Umberto Giacobbi | https://umbertogiacobbi.biz
# Modified with AI: OpenAI Codex; stereo spectrum and loopback validation, 2026-10-10.
# Human guidance: Cassette 1984 instruments using real app audio.
# Copyright (c) 2026 Umberto Giacobbi
# SPDX-License-Identifier: MIT
# VBWR E

[CmdletBinding()]
param([switch]$Live)

$ErrorActionPreference = 'Stop'
$projectRoot = [System.IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$testRoot = Join-Path $projectRoot 'artifacts\audio-visualizer-tests'
New-Item -ItemType Directory -Path $testRoot -Force | Out-Null
$sourceRoot = [System.Security.SecurityElement]::Escape($projectRoot)
@"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0-windows</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="NAudio.Wasapi" Version="2.2.1" />
    <Compile Include="$sourceRoot\Audio\AudioSpectrum.cs" Link="AudioSpectrum.cs" />
    <Compile Include="$sourceRoot\Audio\ProcessAudioVisualizer.cs" Link="ProcessAudioVisualizer.cs" />
    <Compile Include="$sourceRoot\Audio\PlaybackMonitor.cs" Link="PlaybackMonitor.cs" />
    <Compile Include="$sourceRoot\Audio\PlaybackIdentity.cs" Link="PlaybackIdentity.cs" />
  </ItemGroup>
</Project>
"@ | Set-Content -LiteralPath (Join-Path $testRoot 'VisualizerTests.csproj') -Encoding utf8

@'
using VoiceDucker.Audio;

try
{
    CheckSignalAnalysis();
    if (args.Contains("--live")) await CheckLiveCapture();
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception);
    return 1;
}

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static short[] Tone(int frames, double leftHz, double rightHz, double leftAmplitude,
    double rightAmplitude, int start = 0)
{
    var samples = new short[frames * 2];
    for (var frame = 0; frame < frames; frame++)
    {
        var time = (start + frame) / (double)AudioSpectrum.SampleRate;
        samples[frame * 2] = (short)(32767 * leftAmplitude * Math.Sin(2 * Math.PI * leftHz * time));
        samples[frame * 2 + 1] = (short)(32767 * rightAmplitude * Math.Sin(2 * Math.PI * rightHz * time));
    }
    return samples;
}

static int Band(double frequency) => Math.Clamp(
    (int)(Math.Log(frequency / 40) / Math.Log(16000.0 / 40) * AudioSpectrum.BandCount),
    0, AudioSpectrum.BandCount - 1);

static void CheckSignalAnalysis()
{
    var analyzer = new AudioSpectrum();
    var frame = AudioVisualizationFrame.Waiting;
    const int packet = 4410;
    for (var index = 0; index < 20; index++)
        frame = analyzer.Process(Tone(packet, 440, 1760, 0.2, 0.05, index * packet));
    Check(Math.Abs(frame.LeftRms - 0.2 / Math.Sqrt(2)) < 0.002, "Left RMS is incorrect.");
    Check(Math.Abs(frame.RightRms - 0.05 / Math.Sqrt(2)) < 0.002, "Right RMS is incorrect.");
    var strongest = Array.IndexOf(frame.Bands, frame.Bands.Max());
    Check(Math.Abs(strongest - Band(440)) <= 1, "The spectrum placed 440 Hz in the wrong band.");
    Check(frame.Bands[Band(1760)] > 0.45, "The right-channel tone was lost from the spectrum.");

    analyzer.Reset();
    for (var index = 0; index < 10; index++)
        frame = analyzer.Process(Tone(packet, 1000, 1000, 0.2, -0.2, index * packet));
    Check(frame.Bands.Max() > 0.7, "Antiphase stereo cancelled out of the spectrum.");
    Check(Math.Abs(frame.LeftRms - frame.RightRms) < 0.0001, "Antiphase channels have different RMS.");

    for (var index = 0; index < 12; index++) frame = analyzer.Process(new short[packet * 2]);
    Check(frame.LeftRms < 0.001 && frame.RightRms < 0.001, "VU levels failed to return to silence.");
    Check(frame.Bands.All(level => level == 0), "Spectrum bands remained active in silence.");
    analyzer.Reset();
    frame = analyzer.Process(new short[packet * 2]);
    Check(frame.LeftRms == 0 && frame.Bands.All(level => level == 0), "A discontinuity reset retained stale audio.");
    try
    {
        analyzer.Process(new short[3]);
        throw new InvalidOperationException("An incomplete stereo frame was accepted.");
    }
    catch (ArgumentException) { }
    Console.WriteLine("PASS: stereo RMS, independent frequencies, antiphase, silence, discontinuity reset, PCM frame validation.");
}

static async Task CheckLiveCapture()
{
    using var monitor = new PlaybackMonitor();
    var stream = monitor.Read().Where(stream => !stream.Muted && stream.Peak > 0.001)
        .OrderByDescending(stream => stream.IsSpotify).ThenByDescending(stream => stream.Peak).FirstOrDefault();
    Check(stream is not null, "No app is currently playing audible audio; live capture was not tested.");
    var capture = new ProcessAudioVisualizer(stream!.ProcessId);
    double left = 0, right = 0;
    float spectrum = 0;
    var ready = false;
    try
    {
        for (var index = 0; index < 40; index++)
        {
            await Task.Delay(100);
            var frame = capture.Frame;
            Check(frame.Error is null, frame.Error ?? "Capture error.");
            ready |= frame.Ready;
            left = Math.Max(left, frame.LeftRms);
            right = Math.Max(right, frame.RightRms);
            spectrum = Math.Max(spectrum, frame.Bands.Max());
        }
        Check(ready && left + right > 0 && spectrum > 0, "Process capture did not produce real stereo audio and spectrum data.");
    }
    finally
    {
        capture.Dispose();
        await capture.Completion.WaitAsync(TimeSpan.FromSeconds(5));
    }
    Console.WriteLine($"PASS: live process capture ({stream.Name}); L={left:F4}, R={right:F4}, spectrum={spectrum:F3}; capture stopped cleanly.");
    var cancelled = new ProcessAudioVisualizer(stream.ProcessId);
    cancelled.Dispose();
    await cancelled.Completion.WaitAsync(TimeSpan.FromSeconds(5));
    cancelled.Dispose();
    Console.WriteLine("PASS: cancellation during activation and repeated disposal.");
}
'@ | Set-Content -LiteralPath (Join-Path $testRoot 'Program.cs') -Encoding utf8

& dotnet build (Join-Path $testRoot 'VisualizerTests.csproj') -c Release -p:ImportDirectoryBuildProps=false --nologo
if ($LASTEXITCODE -ne 0) { throw 'Audio visualizer test build failed.' }
$testAssembly = Join-Path $testRoot 'bin\Release\net10.0-windows\VisualizerTests.dll'
if ($Live) { & dotnet $testAssembly --live }
else { & dotnet $testAssembly }
if ($LASTEXITCODE -ne 0) { throw 'Audio visualizer checks failed.' }
