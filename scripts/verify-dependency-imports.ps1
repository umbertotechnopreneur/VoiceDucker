# VoiceDucker - Copyright (c) 2026 Umberto Giacobbi. MIT license.
# Reuses completed first-import evidence. Never starts an antivirus scan.
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('win-x64', 'win-arm64')]
    [string]$Runtime
)

$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$assets = Get-Content (Join-Path $root 'artifacts/obj/project.assets.json') -Raw | ConvertFrom-Json
$evidence = Get-Content (Join-Path $root 'docs/dependency-imports.json') -Raw | ConvertFrom-Json -AsHashtable
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
$lines = [Collections.Generic.List[string]]::new()
$lines.Add('VoiceDucker dependency verification: reused first-import evidence; no new scans.')
$lines.Add("Runtime: $Runtime")
$lines.Add("Source commit: $(git -C $root rev-parse HEAD)")
foreach ($report in $evidence.evidence) {
    $lines.Add("Evidence: $($report.url)")
    $lines.Add("Evidence report SHA-256: $($report.sha256)")
}
foreach ($package in @($packages | Sort-Object Name -Unique)) {
    $known = $evidence.packages[$package.Name]
    if (-not $known) { throw "No first-import evidence for $($package.Name). Stage and scan this new import before building." }
    $directory = @($assets.packageFolders.PSObject.Properties.Name | ForEach-Object {
        $candidate = Join-Path $_ $package.Path
        if (Test-Path -LiteralPath $candidate) { $candidate }
    }) | Select-Object -First 1
    if (-not $directory) { throw "Package missing: $($package.Name)" }
    $archive = @(Get-ChildItem -LiteralPath $directory -Filter '*.nupkg' -File)
    if ($archive.Count -ne 1) { throw "Expected one archive for $($package.Name)." }
    $hash = (Get-FileHash -LiteralPath $archive[0].FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($hash -cne $known.sha256) { throw "Changed import: $($package.Name). First-import evidence cannot be reused." }
    $lines.Add("Verified: $($package.Name); source=$($known.source); SHA-256=$hash")
}
$lines.Add('Result: every dependency archive matches completed first-import evidence. Generated app output was not scanned.')
$reportPath = Join-Path $root "artifacts/VoiceDucker-$Runtime.dependency-verification.txt"
$lines | Set-Content -LiteralPath $reportPath -Encoding utf8
Write-Host "Dependency evidence verified: $reportPath"
