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
param(
    [ValidateSet('x64', 'ARM64')]
    [string]$Architecture = 'x64',
    [string]$CertificateThumbprint,
    [switch]$Unsigned,
    [switch]$Install
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ($Install -and $Unsigned) { throw 'An unsigned package cannot be installed by this script.' }
if (-not $Unsigned -and [string]::IsNullOrWhiteSpace($CertificateThumbprint)) {
    throw 'A signing certificate thumbprint is required for a Release MSIX.'
}

$root = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$project = Join-Path $root 'VoiceDucker.csproj'
$manifest = [xml](Get-Content -LiteralPath (Join-Path $root 'Package.appxmanifest') -Raw)
$identity = $manifest.Package.Identity
$version = [version]$identity.Version
$publisher = [string]$identity.Publisher
$runtime = 'win-' + $Architecture.ToLowerInvariant()
$packageDirectory = [System.IO.Path]::GetFullPath(
    (Join-Path $root "artifacts\msix\release\$version\$($Architecture.ToLowerInvariant())"))
$repositoryPrefix = $root.TrimEnd('\') + '\'
if (-not $packageDirectory.StartsWith($repositoryPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Package output is outside the repository: $packageDirectory"
}

if (-not $Unsigned) {
    $thumbprint = $CertificateThumbprint.Replace(' ', '').ToUpperInvariant()
    $certificate = Get-ChildItem Cert:\CurrentUser\My |
        Where-Object { $_.Thumbprint -eq $thumbprint -and $_.HasPrivateKey } |
        Select-Object -First 1
    if ($null -eq $certificate -or $certificate.Subject -cne $publisher -or
        $certificate.NotAfter -le (Get-Date)) {
        throw "A valid private-key certificate with Subject '$publisher' is required."
    }
    if (@($certificate.EnhancedKeyUsageList | Where-Object ObjectId -eq '1.3.6.1.5.5.7.3.3').Count -eq 0) {
        throw 'The certificate must allow code signing.'
    }
}

if (Test-Path -LiteralPath $packageDirectory) {
    if (((Get-Item -LiteralPath $packageDirectory -Force).Attributes -band
            [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "Refusing to clean a linked package directory: $packageDirectory"
    }
    Remove-Item -LiteralPath $packageDirectory -Recurse -Force
}
New-Item -ItemType Directory -Path $packageDirectory -Force | Out-Null

$arguments = @(
    'msbuild', $project, '/restore', '/t:Clean,Build',
    '/p:Configuration=Release', "/p:Platform=$Architecture",
    "/p:RuntimeIdentifier=$runtime", '/p:SelfContained=true',
    '/p:WindowsPackageType=MSIX', '/p:GenerateAppxPackageOnBuild=true',
    '/p:UapAppxPackageBuildMode=SideloadOnly', '/p:AppxBundle=Never',
    "/p:AppxPackageDir=$packageDirectory\", '/p:PublishReadyToRun=false',
    '/p:DebugSymbols=false', '/p:DebugType=None',
    '/p:AppxSymbolPackageEnabled=false'
)
if ($Unsigned) {
    $arguments += '/p:AppxPackageSigningEnabled=false'
}
else {
    $arguments += @('/p:AppxPackageSigningEnabled=true',
        "/p:PackageCertificateThumbprint=$($certificate.Thumbprint)")
}
& dotnet @arguments
if ($LASTEXITCODE -ne 0) { throw 'Release MSIX build failed.' }

$packages = @(Get-ChildItem -LiteralPath $packageDirectory -Filter '*.msix' -Recurse -File |
    Where-Object { $_.DirectoryName -notmatch '[\\/]Dependencies([\\/]|$)' })
if ($packages.Count -ne 1) {
    throw "Expected one application MSIX under $packageDirectory; found $($packages.Count)."
}
$package = $packages[0]
$archive = [System.IO.Compression.ZipFile]::OpenRead($package.FullName)
try {
    $manifestEntry = $archive.GetEntry('AppxManifest.xml')
    if ($null -eq $manifestEntry) { throw 'MSIX does not contain AppxManifest.xml.' }
    $signatureEntry = $archive.GetEntry('AppxSignature.p7x')
    if ($Unsigned -eq ($null -ne $signatureEntry)) {
        throw 'MSIX signing state does not match the requested mode.'
    }
    $reader = [System.IO.StreamReader]::new($manifestEntry.Open())
    try { $packageManifest = [xml]$reader.ReadToEnd() }
    finally { $reader.Dispose() }
    $packageIdentity = $packageManifest.Package.Identity
    if ($packageIdentity.Name -cne $identity.Name -or
        $packageIdentity.Publisher -cne $publisher -or
        $packageIdentity.Version -ne $version.ToString() -or
        $packageIdentity.ProcessorArchitecture -ine $Architecture) {
        throw 'The built MSIX identity, version, or architecture does not match the source manifest.'
    }
}
finally { $archive.Dispose() }

Write-Host "Release MSIX ready: $($package.FullName)"
if ($Install) {
    $signature = Get-AuthenticodeSignature -LiteralPath $package.FullName
    if ($signature.Status -ne 'Valid') {
        throw "MSIX signature is not trusted and valid: $($signature.Status)."
    }
    Add-AppxPackage -Path $package.FullName -ForceApplicationShutdown -ErrorAction Stop
    $installed = @(Get-AppxPackage -Name $identity.Name | Where-Object {
        $_.Version -eq $version -and $_.Architecture.ToString() -ieq $Architecture -and
        $_.Publisher -ceq $publisher -and $_.Status.ToString() -ceq 'Ok'
    })
    if ($installed.Count -ne 1) {
        throw 'MSIX deployment finished without the expected version, architecture, and Ok status.'
    }
    Write-Host "Installed VoiceDucker Release MSIX $version ($Architecture)."
}
