# VBWR B
# Project: VoiceDucker
# Repository: https://github.com/umbertotechnopreneur/VoiceDucker
# Creator: Umberto Giacobbi | https://umbertogiacobbi.biz
# VibeWare: Human intent. AI implementation. Accountable human review.
# Manifesto: https://umbertogiacobbi.biz/vibeware/manifesto
# Created with AI: See file history for implementation provenance.
# Modified with AI: OpenAI Codex; source attribution and release preparation, 2026-10-09.
# Human guidance: Umberto Giacobbi requested branding and the public 0.1.0 release.
# Evidence: Git history and GitHub Actions; this header does not certify human review.
# Copyright (c) 2026 Umberto Giacobbi
# SPDX-License-Identifier: MIT
# License: MIT - see LICENSE
# VBWR E

[CmdletBinding()]
param(
    [ValidateSet('x64', 'ARM64')]
    [string]$Architecture = 'x64',
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',
    [switch]$Run
)

$ErrorActionPreference = 'Stop'
$projectRoot = [System.IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$artifactsRoot = Join-Path $projectRoot 'artifacts'
$runtime = 'win-' + $Architecture.ToLowerInvariant()
$outputDirectory = Join-Path $artifactsRoot (Join-Path 'app' $runtime)
$executable = Join-Path $outputDirectory 'VoiceDucker.exe'
$projectFile = Join-Path $projectRoot 'VoiceDucker.csproj'

# A build must not silently replace the files of a running portable instance.
$outputPrefix = [System.IO.Path]::GetFullPath($outputDirectory).TrimEnd('\') + '\'
foreach ($process in @(Get-Process VoiceDucker -ErrorAction SilentlyContinue)) {
    try {
        if ($process.Path -and $process.Path.StartsWith(
                $outputPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Close the running VoiceDucker from $outputDirectory before rebuilding."
        }
    }
    finally {
        $process.Dispose()
    }
}

$workspacePrefix = $projectRoot.TrimEnd('\') + '\'
foreach ($directory in @(
        $outputDirectory,
        (Join-Path $artifactsRoot 'bin'),
        (Join-Path $artifactsRoot 'obj'))) {
    $fullPath = [System.IO.Path]::GetFullPath($directory)
    if (-not $fullPath.StartsWith($workspacePrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to clean outside the repository: $fullPath"
    }
    if (Test-Path -LiteralPath $fullPath) {
        Remove-Item -LiteralPath $fullPath -Recurse -Force
    }
}

New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
Push-Location $projectRoot
try {
    & dotnet restore $projectFile --locked-mode "-p:Platform=$Architecture" -p:SelfContained=true
    if ($LASTEXITCODE -ne 0) { throw 'Package restore failed.' }

    & dotnet publish $projectFile -c $Configuration "-p:Platform=$Architecture" `
        -p:SelfContained=true -p:PublishReadyToRun=false -p:PublishSingleFile=false `
        -r $runtime --no-restore -o $outputDirectory -m:8
    if ($LASTEXITCODE -ne 0) { throw 'Portable build failed.' }
}
finally {
    Pop-Location
}

if (-not (Test-Path -LiteralPath $executable)) {
    throw "The build did not create $executable"
}
Write-Host "VoiceDucker ready: $executable"

if ($Run) {
    if (@(Get-Process VoiceDucker -ErrorAction SilentlyContinue).Count -gt 0) {
        throw 'Close the existing VoiceDucker instance before launching this build.'
    }
    $process = Start-Process -FilePath $executable -WorkingDirectory $outputDirectory -PassThru
    Start-Sleep -Seconds 2
    if ($process.HasExited) {
        throw "VoiceDucker exited immediately with code $($process.ExitCode)."
    }
    Write-Host "VoiceDucker running with process ID $($process.Id)."
}
