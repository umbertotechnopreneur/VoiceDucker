# VBWR B
# Project: VoiceDucker
# Repository: https://github.com/umbertotechnopreneur/VoiceDucker
# Creator: Umberto Giacobbi | https://umbertogiacobbi.biz
# VibeWare: Human intent, AI execution, and plenty of tokens
# Manifesto: https://umbertogiacobbi.biz/vibeware/manifesto
# Created with AI: OpenAI Codex; release dependency scanning, 2026-10-19.
# Human guidance: Umberto Giacobbi requires explicit Defender scans before publication.
# Evidence: The generated security scan report and GitHub Actions logs.
# Copyright (c) 2026 Umberto Giacobbi
# SPDX-License-Identifier: MIT
# License: MIT - see LICENSE
# VBWR E

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('win-x64', 'win-arm64')]
    [string]$Runtime
)

$ErrorActionPreference = 'Stop'
$root = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$assetsFile = Join-Path $root 'artifacts/obj/project.assets.json'
$appDirectory = Join-Path $root "artifacts/app/$Runtime"
$runtimeConfigFile = Join-Path $appDirectory 'VoiceDucker.runtimeconfig.json'
$archive = Join-Path $root "artifacts/VoiceDucker-$Runtime.zip"
$report = Join-Path $root "artifacts/VoiceDucker-$Runtime.security-scan.txt"
$stageRoot = Join-Path $root "artifacts/security-scan-stage/$Runtime-$([Guid]::NewGuid().ToString('N'))"
$packageArchiveStage = Join-Path $stageRoot 'nuget-archives'
$releaseArchiveStage = Join-Path $stageRoot 'release-archive'

foreach ($requiredPath in @($assetsFile, $appDirectory, $runtimeConfigFile, $archive)) {
    if (-not (Test-Path -LiteralPath $requiredPath)) {
        throw "Release scan input does not exist: $requiredPath"
    }
}

$status = Get-MpComputerStatus
if (-not $status.AMServiceEnabled -or -not $status.AntivirusEnabled) {
    throw 'Microsoft Defender Antivirus is unavailable or disabled.'
}

$platformRoot = Join-Path $env:ProgramData 'Microsoft/Windows Defender/Platform'
$defender = Get-ChildItem -LiteralPath $platformRoot -Directory |
    Sort-Object Name -Descending |
    ForEach-Object { Join-Path $_.FullName 'MpCmdRun.exe' } |
    Where-Object { Test-Path -LiteralPath $_ } |
    Select-Object -First 1
if (-not $defender) {
    throw 'MpCmdRun.exe was not found in the Microsoft Defender platform directory.'
}

New-Item -ItemType Directory -Path $packageArchiveStage, $releaseArchiveStage | Out-Null

$assets = Get-Content -LiteralPath $assetsFile -Raw | ConvertFrom-Json
$packageRoots = @($assets.packageFolders.PSObject.Properties.Name)
$packages = @($assets.libraries.PSObject.Properties |
    Where-Object { $_.Value.type -eq 'package' } |
    ForEach-Object {
        [pscustomobject]@{
            Name = $_.Name
            Path = $_.Value.path
        }
    })

$runtimeConfig = Get-Content -LiteralPath $runtimeConfigFile -Raw | ConvertFrom-Json
$runtimeFramework = @($runtimeConfig.runtimeOptions.includedFrameworks |
    Where-Object { $_.name -eq 'Microsoft.NETCore.App' })
if ($runtimeFramework.Count -ne 1) {
    throw 'The self-contained Microsoft.NETCore.App runtime could not be identified.'
}
$runtimePackageId = "Microsoft.NETCore.App.Runtime.$Runtime"
$runtimeVersion = $runtimeFramework[0].version
$packages += [pscustomobject]@{
    Name = "$runtimePackageId/$runtimeVersion"
    Path = "$($runtimePackageId.ToLowerInvariant())/$($runtimeVersion.ToLowerInvariant())"
}
$packages = @($packages | Sort-Object Name -Unique)
if ($packages.Count -eq 0) {
    throw 'No restored NuGet packages were found in project.assets.json.'
}

$lines = [System.Collections.Generic.List[string]]::new()
$lines.Add('VoiceDucker release security scan')
$lines.Add("Repository: https://github.com/umbertotechnopreneur/VoiceDucker")
$lines.Add("Source commit: $($env:GITHUB_SHA ?? (git -C $root rev-parse HEAD))")
$lines.Add("Runtime: $Runtime")
$lines.Add("Scan time (UTC): $([DateTime]::UtcNow.ToString('o'))")
$lines.Add("Defender product version: $($status.AMProductVersion)")
$lines.Add("Defender engine version: $($status.AMEngineVersion)")
$lines.Add("Defender signature version: $($status.AntivirusSignatureVersion)")
$lines.Add("Defender command: $defender -Scan -ScanType 3 -File <target>")
$lines.Add('')

function Invoke-RequiredDefenderScan {
    param(
        [Parameter(Mandatory)]
        [string]$Label,
        [Parameter(Mandatory)]
        [string]$Path
    )

    Write-Host "Scanning $Label"
    $output = & $defender -Scan -ScanType 3 -File $Path 2>&1 | Out-String
    $exitCode = $LASTEXITCODE
    $summary = ($output.Trim() -replace '\r?\n', ' | ')
    $lines.Add("Target: $Label")
    $lines.Add("Path: $Path")
    $lines.Add("Defender exit code: $exitCode")
    $lines.Add("Defender output: $summary")
    $lines.Add('')
    if ($exitCode -ne 0 -or $output -match '(?i)\bwas skipped\b' -or
            $output -notmatch '(?i)found no threats') {
        $lines | Set-Content -LiteralPath $report -Encoding utf8
        Write-Host $output
        throw "Microsoft Defender did not complete a clean scan for $Label (exit code $exitCode)."
    }
}

foreach ($package in $packages) {
    $packageDirectory = $null
    foreach ($packageRoot in $packageRoots) {
        $candidate = Join-Path $packageRoot $package.Path
        if (Test-Path -LiteralPath $candidate) {
            $packageDirectory = $candidate
            break
        }
    }
    if (-not $packageDirectory) {
        throw "Restored package directory was not found: $($package.Name)"
    }

    $metadataFile = Join-Path $packageDirectory '.nupkg.metadata'
    if (-not (Test-Path -LiteralPath $metadataFile)) {
        throw "NuGet source metadata was not found: $metadataFile"
    }
    $metadata = Get-Content -LiteralPath $metadataFile -Raw | ConvertFrom-Json
    $archiveFile = Get-ChildItem -LiteralPath $packageDirectory -Filter '*.nupkg' -File |
        Select-Object -First 1
    if (-not $archiveFile) {
        throw "Downloaded NuGet archive was not found in $packageDirectory"
    }

    $lines.Add("Package: $($package.Name)")
    $lines.Add("Official source: $($metadata.source)")
    $lines.Add("Archive SHA-256: $((Get-FileHash -LiteralPath $archiveFile.FullName -Algorithm SHA256).Hash.ToLowerInvariant())")
    Copy-Item -LiteralPath $archiveFile.FullName -Destination $packageArchiveStage
    Invoke-RequiredDefenderScan -Label "$($package.Name) extracted files" -Path $packageDirectory
}

Invoke-RequiredDefenderScan -Label 'downloaded NuGet archives' -Path $packageArchiveStage
Invoke-RequiredDefenderScan -Label "$Runtime application files" -Path $appDirectory
Copy-Item -LiteralPath $archive -Destination $releaseArchiveStage
Invoke-RequiredDefenderScan -Label "$Runtime release archive" -Path $releaseArchiveStage
$lines.Add('Result: clean; all targeted scans completed without detections.')
$lines | Set-Content -LiteralPath $report -Encoding utf8
Write-Host "Security scan report: $report"
