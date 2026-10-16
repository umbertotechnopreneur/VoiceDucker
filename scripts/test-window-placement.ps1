# VoiceDucker - Copyright (c) 2026 Umberto Giacobbi. MIT license.
# Geometry and persistence checks use synthetic displays and isolated files.
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$projectRoot = [System.IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$testRoot = Join-Path $projectRoot 'artifacts\window-placement-tests'
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
    <Compile Include="$sourceRoot\WindowPlacementState.cs" Link="WindowPlacementState.cs" />
    <Compile Include="$sourceRoot\WindowPlacementStore.cs" Link="WindowPlacementStore.cs" />
  </ItemGroup>
</Project>
"@ | Set-Content -LiteralPath (Join-Path $testRoot 'PlacementTests.csproj') -Encoding utf8

@'
using System.Text.Json;
using VoiceDucker;

void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

var area = new WindowBounds(1920, 0, 2560, 1400);
var bounds = new WindowBounds(2040, 80, 700, 900);
var saved = new WindowPlacementState(bounds, area, 120, "physical-monitor", "DISPLAY2", false, true);
Check(saved.RestoreBounds(area, 120) == bounds, "Unchanged display must restore exact physical bounds.");
Check(saved.RestoreBounds(area, 144) == new WindowBounds(2064, 96, 840, 1080),
    "Changed DPI must preserve logical size and monitor-relative offset.");
var moved = new WindowBounds(-2560, -200, 2560, 1400);
Check(saved.RestoreBounds(moved, 120) == new WindowBounds(-2440, -120, 700, 900),
    "Moving the monitor in the desktop must not leave the window on a different monitor.");
var primary = new WindowBounds(0, 0, 1920, 1040);
Check(saved.RestoreBounds(primary, 96) == new WindowBounds(96, 64, 560, 720),
    "Fallback to another monitor must adapt to its work area and DPI.");
var oversized = saved with { Bounds = new WindowBounds(4400, 1300, 4000, 3000) };
Check(oversized.RestoreBounds(primary, 120) == primary,
    "An oversized or disconnected-display window must fit fully inside the available work area.");
var taskbarMoved = new WindowBounds(0, 48, 1920, 1032);
var edge = saved with { Bounds = new WindowBounds(4200, 1300, 700, 900) };
Check(edge.RestoreBounds(taskbarMoved, 120) == new WindowBounds(1220, 180, 700, 900),
    "Clamping must respect a nonzero work-area origin, including the taskbar.");
Check(!(saved with { Dpi = 0 }).IsValid, "A zero DPI cannot be restored.");
Check(!(saved with { Bounds = new WindowBounds(0, 0, -1, 700) }).IsValid,
    "Invalid dimensions cannot be restored.");

var isolatedDirectory = Path.Combine(AppContext.BaseDirectory, "isolated", Guid.NewGuid().ToString("N"));
var path = Path.Combine(isolatedDirectory, "window-placement.json");
Check(WindowPlacementStore.Load(path) is null, "First launch must have no saved placement.");
WindowPlacementStore.Save(saved, path);
Check(WindowPlacementStore.Load(path) == saved, "Placement round-trip lost monitor, DPI or pane state.");
var maximized = saved with { Maximized = true };
WindowPlacementStore.Save(maximized, path);
Check(WindowPlacementStore.Load(path) == maximized, "Maximized state or normal bounds were lost.");
Check(!File.Exists(path + ".tmp"), "An atomic save left a temporary file behind.");
File.WriteAllText(path, "null");
Check(WindowPlacementStore.Load(path) is null, "Null placement must fall back to first-launch sizing.");
File.WriteAllText(path, JsonSerializer.Serialize(saved with { Dpi = 0 }));
Check(WindowPlacementStore.Load(path) is null, "Invalid saved placement must be ignored.");
File.WriteAllText(path, "{broken");
try
{
    WindowPlacementStore.Load(path);
    throw new InvalidOperationException("Corrupt JSON was not reported to the window's recovery handler.");
}
catch (JsonException) { }
Console.WriteLine("PASS: exact bounds, DPI changes, negative coordinates, moved/disconnected-display geometry, work-area clamping, validation, atomic persistence and invalid/corrupt files. Synthetic displays only.");
'@ | Set-Content -LiteralPath (Join-Path $testRoot 'Program.cs') -Encoding utf8

Push-Location $projectRoot
try {
    & dotnet build (Join-Path $testRoot 'PlacementTests.csproj') --configuration Release -p:ImportDirectoryBuildProps=false --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Window placement test build failed.' }
    & dotnet (Join-Path $testRoot 'bin\Release\net10.0\PlacementTests.dll')
    if ($LASTEXITCODE -ne 0) { throw 'Window placement checks failed.' }
}
finally {
    Pop-Location
}
