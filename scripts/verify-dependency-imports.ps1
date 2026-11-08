# VoiceDucker - Copyright (c) 2026 Umberto Giacobbi. MIT license.
# Verifies pinned dependency archives without starting an antivirus scan.
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('win-x64', 'win-arm64')]
    [string]$Runtime
)

$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$assets = Get-Content (Join-Path $root 'artifacts/obj/project.assets.json') -Raw | ConvertFrom-Json
$pins = Get-Content (Join-Path $root 'docs/dependency-imports.json') -Raw | ConvertFrom-Json -AsHashtable
$runtimeId = "Microsoft.NETCore.App.Runtime.$Runtime"
$runtimeDownload = @($assets.project.frameworks.PSObject.Properties.Value.downloadDependencies |
    Where-Object { $_.name -ceq $runtimeId })
if ($runtimeDownload.Count -ne 1 -or $runtimeDownload[0].version -notmatch '^\[([^,]+), \1\]$') {
    throw "Could not determine the exact restored runtime version for $runtimeId."
}
$runtimeVersion = $Matches[1]
$packages = @($assets.libraries.PSObject.Properties | Where-Object { $_.Value.type -eq 'package' } |
    ForEach-Object { [pscustomobject]@{ Name = $_.Name; Path = $_.Value.path } })
$packages += [pscustomobject]@{
    Name = "Microsoft.NETCore.App.Runtime.$Runtime/$runtimeVersion"
    Path = "microsoft.netcore.app.runtime.$Runtime/$runtimeVersion"
}
foreach ($package in @($packages | Sort-Object Name -Unique)) {
    $known = $pins.packages[$package.Name]
    if (-not $known) { throw "No pinned hash for $($package.Name). Stage and scan this new import before building." }
    $directory = @($assets.packageFolders.PSObject.Properties.Name | ForEach-Object {
        $candidate = Join-Path $_ $package.Path
        if (Test-Path -LiteralPath $candidate) { $candidate }
    }) | Select-Object -First 1
    if (-not $directory) { throw "Package missing: $($package.Name)" }
    $archive = @(Get-ChildItem -LiteralPath $directory -Filter '*.nupkg' -File)
    if ($archive.Count -ne 1) { throw "Expected one archive for $($package.Name)." }
    $hash = (Get-FileHash -LiteralPath $archive[0].FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($hash -cne $known.sha256) { throw "Changed import: $($package.Name). Verify the official archive and complete a new first-import scan." }
}
Write-Host "Verified pinned dependency archives for $Runtime."
