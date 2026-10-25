# VoiceDucker - Copyright (c) 2026 Umberto Giacobbi. MIT license.
# Synthetic buffers and a fake clock only. Never opens audio devices or the app.
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$testRoot = Join-Path $projectRoot 'artifacts\microphone-pipeline-tests'
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
    <Compile Include="WaveFormats.cs" />
    <Compile Include="$sourceRoot\Audio\MicrophoneLevel.cs" Link="MicrophoneLevel.cs" />
    <Compile Include="$sourceRoot\Audio\VoiceGate.cs" Link="VoiceGate.cs" />
    <Compile Include="$sourceRoot\Audio\DuckingSettings.cs" Link="DuckingSettings.cs" />
    <Compile Include="$sourceRoot\Audio\ListeningProfile.cs" Link="ListeningProfile.cs" />
    <Compile Include="$sourceRoot\Audio\PlaybackIdentity.cs" Link="PlaybackIdentity.cs" />
  </ItemGroup>
</Project>
"@ | Set-Content -LiteralPath (Join-Path $testRoot 'MicrophoneTests.csproj') -Encoding utf8

@'
// Format metadata only; there is deliberately no capture or Core Audio implementation.
namespace NAudio.Wave;
public enum WaveFormatEncoding { Unknown, Pcm, IeeeFloat, Extensible }
public class WaveFormat(WaveFormatEncoding encoding, int bits, int channels = 1, int rate = 48000)
{
    public WaveFormatEncoding Encoding { get; } = encoding;
    public int BitsPerSample { get; } = bits;
    public int Channels { get; } = channels;
    public int SampleRate { get; } = rate;
}
public sealed class WaveFormatExtensible(int bits, Guid subFormat)
    : WaveFormat(WaveFormatEncoding.Extensible, bits)
{
    public Guid SubFormat { get; } = subFormat;
}
'@ | Set-Content -LiteralPath (Join-Path $testRoot 'WaveFormats.cs') -Encoding utf8

@'
using System.Buffers.Binary;
using NAudio.Wave;
using VoiceDucker.Audio;

var checks = 0;
void Check(bool condition, string message)
{
    checks++;
    if (!condition) throw new InvalidOperationException(message);
}
void Near(double actual, double expected, string message, double tolerance = 1e-12) =>
    Check(double.IsFinite(actual) && Math.Abs(actual - expected) <= tolerance,
        $"{message} Expected {expected:R}, observed {actual:R}.");
void Throws<T>(Action action, string message) where T : Exception
{
    try { action(); }
    catch (T) { Check(true, message); return; }
    Check(false, message);
}
byte[] FloatBuffer(params float[] samples)
{
    var buffer = new byte[samples.Length * 4];
    for (var index = 0; index < samples.Length; index++)
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(index * 4),
            BitConverter.SingleToInt32Bits(samples[index]));
    return buffer;
}
var pcm16 = new WaveFormat(WaveFormatEncoding.Pcm, 16);
var pcm24 = new WaveFormat(WaveFormatEncoding.Pcm, 24);
var pcm32 = new WaveFormat(WaveFormatEncoding.Pcm, 32);
var float32 = new WaveFormat(WaveFormatEncoding.IeeeFloat, 32);
var half16 = new byte[] { 0, 64, 0, 192 };
var half24 = new byte[] { 0, 0, 64, 0, 0, 192 };
var half32 = new byte[] { 0, 0, 0, 64, 0, 0, 0, 192 };
foreach (var (format, buffer) in new[]
{
    (pcm16, half16), (pcm24, half24), (pcm32, half32),
    (float32, FloatBuffer(0.5f, -0.5f))
})
{
    Check(MicrophoneLevel.IsSupported(format), "A supported microphone format was rejected.");
    Near(MicrophoneLevel.Rms(buffer, format), 0.5, "Signed half-scale RMS was decoded incorrectly.");
    Near(MicrophoneLevel.Rms([], format), 0, "An empty buffer did not produce silence.");
}
Near(MicrophoneLevel.Rms([0, 128], pcm16), 1, "PCM16 negative full scale was decoded incorrectly.");
Near(MicrophoneLevel.Rms([0, 0, 128], pcm24), 1, "PCM24 sign extension was decoded incorrectly.");
Near(MicrophoneLevel.Rms([0, 0, 0, 128], pcm32), 1, "PCM32 negative full scale was decoded incorrectly.");
Near(MicrophoneLevel.Rms([0, 64, 255], pcm16), 0.5, "An incomplete final sample affected RMS.");
Near(MicrophoneLevel.Rms([0, 64, 0, 0],
    new WaveFormat(WaveFormatEncoding.Pcm, 16, 2)), Math.Sqrt(0.125),
    "Interleaved channels were not measured consistently.");
Near(MicrophoneLevel.Rms(half24,
    new WaveFormatExtensible(24, new Guid("00000001-0000-0010-8000-00aa00389b71"))), 0.5,
    "Extensible PCM was not recognized.");
Near(MicrophoneLevel.Rms(FloatBuffer(-0.5f, 0.5f),
    new WaveFormatExtensible(32, new Guid("00000003-0000-0010-8000-00aa00389b71"))), 0.5,
    "Extensible floating point was not recognized.");
Near(MicrophoneLevel.Rms(FloatBuffer(float.NaN, float.PositiveInfinity,
    float.NegativeInfinity, 0.5f), float32), 0.5,
    "A non-finite float poisoned the remaining valid level samples.");
Near(MicrophoneLevel.Rms(FloatBuffer(float.NaN, float.PositiveInfinity), float32), 0,
    "A wholly invalid float buffer produced a level.");
foreach (var unsupported in new[]
{
    new WaveFormat(WaveFormatEncoding.Pcm, 8),
    new WaveFormat(WaveFormatEncoding.IeeeFloat, 16),
    new WaveFormat(WaveFormatEncoding.Unknown, 32),
    new WaveFormat(WaveFormatEncoding.Pcm, 16, 0),
    new WaveFormat(WaveFormatEncoding.Pcm, 16, 1, 0),
    new WaveFormatExtensible(32, Guid.Empty)
})
{
    Check(!MicrophoneLevel.IsSupported(unsupported), "An unsupported microphone format was accepted.");
    Throws<NotSupportedException>(() => MicrophoneLevel.Rms([0, 0, 0, 0], unsupported),
        "An unsupported format was decoded as ordinary PCM.");
}
Near(MicrophoneLevel.ApplyGain(0.01, 20), 0.1, "Positive gain conversion failed.");
Near(MicrophoneLevel.ApplyGain(0.1, -20), 0.01, "Negative gain conversion failed.");
Check(MicrophoneLevel.ApplyGain(0.5, 36) > 1, "Detection gain was clipped to the visual meter range.");
foreach (var invalid in new[] { -1d, double.NaN, double.PositiveInfinity, double.NegativeInfinity })
    Near(MicrophoneLevel.ApplyGain(invalid, 36), 0, "An invalid level reached detection.");

long now = 0;
var gate = new VoiceGate(() => now);
var settings = new DuckingSettings
{
    ThresholdDecibels = -30, HysteresisDecibels = 6, RestoreDelayMilliseconds = 650
};
long sequence = 0;
CapturedMicrophoneSample Sample(double rms) => new(rms, now, ++sequence);
GateState Observe(double rms, DuckingSettings? options = null) => gate.Evaluate(Sample(rms), options ?? settings);
var trigger = settings.GetThresholdAmplitude();
var release = trigger * Math.Pow(10, -6 / 20.0);
Check(gate.Evaluate(null, settings).Phase == DetectionPhase.BelowThreshold,
    "Waiting for the first buffer was treated as active input.");
var above = Observe(trigger);
Check(above.Phase == DetectionPhase.AboveThreshold && above.Speaking,
    "A sample exactly at the threshold did not trigger.");
Near(above.Decibels, -30, "The displayed level and detection did not use the same gain.");
Near(above.ReleaseThresholdDecibels, -36, "The reported release threshold was wrong.");
now = 100;
Check(Observe(release).Phase == DetectionPhase.Hysteresis,
    "A previously triggered signal was not held at the exact release boundary.");
now = 200;
Check(Observe(release * 0.99).Phase == DetectionPhase.Hold,
    "The release delay did not start below the hysteresis band.");
now = 749;
Check(gate.Evaluate(null, settings).Phase == DetectionPhase.Hold,
    "The release delay ended before its configured duration.");
now = 750;
Check(!gate.Evaluate(null, settings).Speaking,
    "The release delay did not end at its configured boundary.");

gate.Reset(); now = 1000;
var retained = Sample(trigger * 2);
Check(gate.Evaluate(retained, settings).Speaking, "A fresh loud sample failed.");
now = 1600;
Check(gate.Evaluate(retained, settings).Speaking, "A still-fresh loud sample was lost during polling.");
Check(Observe(0).Phase == DetectionPhase.Hold, "A quiet observation did not enter hold.");
now = 1650;
Check(!gate.Evaluate(null, settings).Speaking,
    "Repeated worker polls renewed hold using poll time instead of capture time.");

gate.Reset(); now = 2000;
var quiet = Sample(0.01);
Check(!gate.Evaluate(quiet, settings).Speaking, "Quiet input crossed the original threshold.");
now = 2600;
var gainSettings = settings with { MicrophoneGainDecibels = 20 };
var gained = gate.Evaluate(quiet, gainSettings);
Check(gained.Phase == DetectionPhase.AboveThreshold,
    "A live sensitivity change did not reclassify the captured level.");
Near(gained.Decibels, -20, "Sensitivity gain and reported dBFS disagreed.");
now = 2610;
Check(gate.Evaluate(quiet, settings).Phase == DetectionPhase.Hold,
    "Removing gain did not release the signal coherently.");
now = 2650;
Check(!gate.Evaluate(quiet, settings).Speaking,
    "Settings reclassification renewed hold without another captured sample.");

gate.Reset(); now = 3000;
var fixedLevel = Sample(0.01);
Check(!gate.Evaluate(fixedLevel, settings).Speaking, "The initial threshold was ignored.");
now = 3600;
Check(gate.Evaluate(fixedLevel, settings with { ThresholdDecibels = -50 }).Speaking,
    "A changed threshold was not applied immediately.");
now = 3650;
Check(!gate.Evaluate(fixedLevel, settings).Speaking,
    "A threshold edit extended the release delay from an old capture time.");

gate.Reset(); now = 4000;
Observe(trigger * 2);
now = 4010;
Observe(release);
Check(!gate.Evaluate(null, settings with { HysteresisDecibels = 0, RestoreDelayMilliseconds = 0 }).Speaking,
    "Disabling hysteresis and hold did not release a level below the trigger.");

gate.Reset(); now = 5000;
var loud = Sample(trigger * 2);
gate.Evaluate(loud, settings);
now = 5999;
Check(gate.Evaluate(loud, settings).Speaking, "A fresh sample expired before the stale deadline.");
now = 6000;
var stale = gate.Evaluate(loud, settings);
Check(stale.Phase == DetectionPhase.Stale && !stale.Speaking && stale.Rms == 0 &&
    stale.SampleAgeMilliseconds == 1000, "A missing capture buffer left a loud gate latched.");
Check(!Observe(0).Speaking, "Capture recovery inherited a stale active/hold state.");
gate.Reset(); now = 7000;
gate.Evaluate(null, settings);
now = 8000;
Check(gate.Evaluate(null, settings).Phase == DetectionPhase.Stale,
    "Capture that never delivered a first buffer did not become stale.");
gate.Reset();
Check(gate.Evaluate(null, settings).Phase == DetectionPhase.BelowThreshold,
    "Reset did not clear the startup deadline.");

gate.Reset(); now = 9000;
var first = Sample(trigger * 2);
gate.Evaluate(first, settings);
now = 9010;
var second = Sample(0);
gate.Evaluate(second, settings with { RestoreDelayMilliseconds = 0 });
Check(!gate.Evaluate(first, settings with { RestoreDelayMilliseconds = 0 }).Speaking,
    "An older sequence overwrote a newer capture observation.");
Check(!gate.Evaluate(second with { RawRms = 1 }, settings with { RestoreDelayMilliseconds = 0 }).Speaking,
    "Reusing a sequence changed the captured observation.");

foreach (var threshold in new[] { -60d, DuckingSettings.DefaultThresholdDecibels, -35d, -0.1, 0 })
{
    gate.Reset(); now += 100;
    var boundary = settings with
    {
        ThresholdDecibels = threshold, HysteresisDecibels = 0, RestoreDelayMilliseconds = 0
    };
    Check(!Observe(boundary.GetThresholdAmplitude() * 0.99, boundary).Speaking,
        "A level below a valid threshold triggered detection.");
    Check(Observe(boundary.GetThresholdAmplitude(), boundary).Speaking,
        "An exact valid threshold was missed.");
}
var off = GateState.Off(settings);
Check(off.Phase == DetectionPhase.Off && !off.Speaking && off.Rms == 0 &&
    double.IsNegativeInfinity(off.Decibels), "The off state retained microphone activity.");
Throws<ArgumentOutOfRangeException>(() => gate.Evaluate(null,
    settings with { ThresholdDecibels = double.NaN }), "Invalid settings reached the gate.");
Console.WriteLine($"Microphone pipeline: {checks} synthetic checks passed; no audio devices were accessed.");
'@ | Set-Content -LiteralPath (Join-Path $testRoot 'Program.cs') -Encoding utf8

dotnet build (Join-Path $testRoot 'MicrophoneTests.csproj') --nologo --verbosity minimal `
    -p:ImportDirectoryBuildProps=false -p:NuGetAudit=false
if ($LASTEXITCODE -ne 0) { throw 'Synthetic microphone test harness did not build.' }
dotnet (Join-Path $testRoot 'bin\Debug\net10.0\MicrophoneTests.dll')
if ($LASTEXITCODE -ne 0) { throw 'Synthetic microphone pipeline checks failed.' }
