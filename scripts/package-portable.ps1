# VBWR B
# Project: VoiceDucker
# Repository: https://github.com/umbertotechnopreneur/VoiceDucker
# Creator: Umberto Giacobbi | https://umbertogiacobbi.biz
# VibeWare: Human intent. AI implementation. Accountable human review.
# Manifesto: https://umbertogiacobbi.biz/vibeware/manifesto
# Created with AI: OpenAI Codex; portable release packaging, 2026-10-09.
# Human guidance: Umberto Giacobbi requested public Windows x64 and ARM64 ZIPs.
# Evidence: Git history and GitHub Actions; interactive audio checks are separate.
# Copyright (c) 2026 Umberto Giacobbi
# License: MIT - see LICENSE
# VBWR E

[CmdletBinding()]
param(
    [ValidateSet('win-x64', 'win-arm64')]
    [string]$Runtime = 'win-x64'
)

$ErrorActionPreference = 'Stop'
$root = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$folder = Join-Path $root "artifacts/app/$Runtime"
if (-not (Test-Path -LiteralPath (Join-Path $folder 'VoiceDucker.exe'))) {
    throw 'Build the portable Release app before packaging.'
}
Copy-Item -LiteralPath (Join-Path $root 'README.md'), (Join-Path $root 'LICENSE') -Destination $folder
New-Item -ItemType Directory -Force -Path (Join-Path $folder 'docs/screenshots') | Out-Null
Copy-Item -LiteralPath (Join-Path $root 'docs/RELEASING.md') -Destination (Join-Path $folder 'docs')
Copy-Item -LiteralPath (Join-Path $root 'docs/screenshots/main-window.png') -Destination (Join-Path $folder 'docs/screenshots')
& python (Join-Path $PSScriptRoot 'collect-notices.py') --mode nuget --target $Runtime --output (Join-Path $folder 'third-party-notices')
if ($LASTEXITCODE -ne 0) { throw 'Dependency notice collection failed.' }
$zip = Join-Path $root "artifacts/VoiceDucker-$Runtime.zip"
if (Test-Path -LiteralPath $zip) { throw "Refusing to overwrite an existing package: $zip" }
Compress-Archive -Path (Join-Path $folder '*') -DestinationPath $zip
$hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $([System.IO.Path]::GetFileName($zip))" | Set-Content -LiteralPath "$zip.sha256" -Encoding ascii
Write-Host "Package ready: $zip"
