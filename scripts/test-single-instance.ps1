# VoiceDucker - Copyright (c) 2026 Umberto Giacobbi. MIT license.
# Separate console processes exercise instance ownership without accessing audio.
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$projectRoot = [System.IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$testRoot = Join-Path $projectRoot 'artifacts\single-instance-tests'
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
    <Compile Include="$sourceRoot\SingleInstanceService.cs" Link="SingleInstanceService.cs" />
  </ItemGroup>
</Project>
"@ | Set-Content -LiteralPath (Join-Path $testRoot 'SingleInstanceTests.csproj') -Encoding utf8

@'
using System.Diagnostics;
using System.Reflection;
using VoiceDucker;

if (args is ["request", var instanceName])
{
    using var secondary = new SingleInstanceService(instanceName);
    if (secondary.IsPrimary) return 10;
    secondary.RequestActivation();
    return 0;
}
if (args is ["hold", var ownerName])
{
    using var holder = new SingleInstanceService(ownerName);
    if (!holder.IsPrimary) return 11;
    Console.WriteLine("PRIMARY");
    Console.ReadLine();
    return 0;
}

void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

Process StartChild(string command, string name)
{
    var start = new ProcessStartInfo("dotnet")
    {
        UseShellExecute = false,
        CreateNoWindow = true,
        RedirectStandardOutput = true,
        RedirectStandardInput = true
    };
    start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
    start.ArgumentList.Add(command);
    start.ArgumentList.Add(name);
    return Process.Start(start) ?? throw new InvalidOperationException("Could not start test process.");
}

void FinishSecondary(Process child)
{
    Check(child.WaitForExit(10_000), "A repeat launch did not exit promptly.");
    Check(child.ExitCode == 0, "A repeat launch became another primary instance.");
}

var name = @"Local\VoiceDucker.SingleInstanceTests." + Guid.NewGuid().ToString("N");
using var activated = new AutoResetEvent(false);
using (var primary = new SingleInstanceService(name))
{
    Check(primary.IsPrimary, "The first process must become primary.");

    // The primary is deliberately not listening yet: its window may still be loading.
    using (var early = StartChild("request", name)) FinishSecondary(early);
    primary.Listen(() => activated.Set());
    Check(activated.WaitOne(5000), "An activation request was lost during primary startup.");

    var burst = Enumerable.Range(0, 6).Select(_ => StartChild("request", name)).ToArray();
    try
    {
        foreach (var child in burst) FinishSecondary(child);
        Check(activated.WaitOne(5000), "Concurrent repeat launches did not notify the primary.");
    }
    finally
    {
        foreach (var child in burst) child.Dispose();
    }
}
using (var restarted = new SingleInstanceService(name))
    Check(restarted.IsPrimary, "Normal exit did not release instance ownership.");

var abandonedName = name + ".Abandoned";
using (var crashed = StartChild("hold", abandonedName))
{
    Check(crashed.StandardOutput.ReadLine() == "PRIMARY", "Crash test owner did not start.");
    // Keep the kernel mutex alive so the next owner actually encounters abandonment.
    using var heldOpen = Mutex.OpenExisting(abandonedName);
    crashed.Kill();
    Check(crashed.WaitForExit(5000), "Crash test process did not exit.");
    using var recovered = new SingleInstanceService(abandonedName);
    Check(recovered.IsPrimary, "An abandoned owner prevented the app from restarting.");
}
Console.WriteLine("PASS: startup race, six concurrent repeat launches, activation delivery, normal ownership release and crash recovery. No microphone or playback access.");
return 0;
'@ | Set-Content -LiteralPath (Join-Path $testRoot 'Program.cs') -Encoding utf8

Push-Location $projectRoot
try {
    & dotnet build (Join-Path $testRoot 'SingleInstanceTests.csproj') --configuration Release -p:ImportDirectoryBuildProps=false --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Single-instance test build failed.' }
    & dotnet (Join-Path $testRoot 'bin\Release\net10.0-windows\SingleInstanceTests.dll')
    if ($LASTEXITCODE -ne 0) { throw 'Single-instance checks failed.' }
}
finally {
    Pop-Location
}
