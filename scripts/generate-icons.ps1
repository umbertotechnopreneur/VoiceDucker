#requires -Version 7.0
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
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing.Common

$root = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$assets = Join-Path $root 'Assets'
$simplePath = Join-Path $assets 'icon-simple.png'
$detailedPath = Join-Path $assets 'voice-mixer-off.png'
$iconPath = Join-Path $assets 'AppIcon.ico'

function New-SizedPng([string]$sourcePath, [int]$size) {
    $source = [System.Drawing.Image]::FromFile($sourcePath)
    try {
        $bitmap = [System.Drawing.Bitmap]::new($size, $size)
        try {
            $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
            try {
                $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
                $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
                $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
                $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
                $graphics.DrawImage($source, 0, 0, $size, $size)
            }
            finally { $graphics.Dispose() }
            $stream = [System.IO.MemoryStream]::new()
            try {
                $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
                return $stream.ToArray()
            }
            finally { $stream.Dispose() }
        }
        finally { $bitmap.Dispose() }
    }
    finally { $source.Dispose() }
}

# Windows selects the closest ICO layer. Use the simplified art only below 32 px.
$layers = @()
foreach ($size in @(16, 24, 32, 48, 64, 256)) {
    $source = if ($size -lt 32) { $simplePath } else { $detailedPath }
    $layers += [pscustomobject]@{ Size = $size; Data = (New-SizedPng $source $size) }
}

$stream = [System.IO.File]::Create($iconPath)
try {
    $writer = [System.IO.BinaryWriter]::new($stream)
    try {
        $writer.Write([uint16]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]$layers.Count)
        $offset = 6 + 16 * $layers.Count
        foreach ($layer in $layers) {
            $writer.Write([byte]($layer.Size % 256))
            $writer.Write([byte]($layer.Size % 256))
            $writer.Write([byte]0)
            $writer.Write([byte]0)
            $writer.Write([uint16]1)
            $writer.Write([uint16]32)
            $writer.Write([uint32]$layer.Data.Length)
            $writer.Write([uint32]$offset)
            $offset += $layer.Data.Length
        }
        foreach ($layer in $layers) {
            $writer.Write([byte[]]$layer.Data)
        }
    }
    finally { $writer.Dispose() }
}
finally { $stream.Dispose() }

$targets = @(
    @{ Name = 'Square44x44Logo.targetsize-24_altform-unplated.png'; Size = 24; Simple = $true },
    @{ Name = 'Square44x44Logo.targetsize-48_altform-lightunplated.png'; Size = 48; Simple = $false },
    @{ Name = 'Square44x44Logo.scale-200.png'; Size = 88; Simple = $false },
    @{ Name = 'Square150x150Logo.scale-200.png'; Size = 300; Simple = $false },
    @{ Name = 'StoreLogo.png'; Size = 50; Simple = $false }
)
foreach ($target in $targets) {
    $source = if ($target.Simple) { $simplePath } else { $detailedPath }
    [System.IO.File]::WriteAllBytes(
        (Join-Path $assets $target.Name), (New-SizedPng $source $target.Size))
}

Write-Host "Adaptive icon and tile assets generated in $assets"
