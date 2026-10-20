# VoiceDucker - Copyright (c) 2026 Umberto Giacobbi. MIT license.
# Run in Windows PowerShell (-STA) for the built-in Windows UI Automation assemblies.
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][int]$AppProcessId,
    [Parameter(Mandatory = $true)][string]$DiagnosticsPath
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$appProcess = Get-Process -Id $AppProcessId -ErrorAction Stop
if ($appProcess.ProcessName -ne 'VoiceDucker') { throw 'The target is not VoiceDucker.' }
$lines = @(Get-Content -LiteralPath $DiagnosticsPath | Where-Object { $_ -match "pid=$AppProcessId " })
if (-not ($lines | Where-Object { $_ -match 'startup-volume-snapshot capture=off' })) {
    throw 'The installed app did not confirm its startup snapshot with capture off.'
}
if ($lines | Where-Object { $_ -match 'engine-start ' }) {
    throw 'Disable microphone monitoring before this isolated shutdown check.'
}
if (-not $appProcess.CloseMainWindow()) { throw 'The app did not accept a normal window-close request.' }
$condition = New-Object System.Windows.Automation.AndCondition @(
    (New-Object System.Windows.Automation.PropertyCondition @(
        [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $AppProcessId)),
    (New-Object System.Windows.Automation.PropertyCondition @(
        [System.Windows.Automation.AutomationElement]::NameProperty, 'Close app'))
)
$deadline = [DateTime]::UtcNow.AddSeconds(5)
$button = $null
do {
    $button = [System.Windows.Automation.AutomationElement]::RootElement.FindFirst(
        [System.Windows.Automation.TreeScope]::Descendants, $condition)
    if ($null -eq $button) { Start-Sleep -Milliseconds 50 }
} while ($null -eq $button -and [DateTime]::UtcNow -lt $deadline)
if ($null -eq $button) { throw 'The Close app confirmation button was not found.' }
$invoke = $button.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
$invoke.Invoke()
if (-not $appProcess.WaitForExit(15000)) { throw 'VoiceDucker remained running after Close app.' }
if (Get-Process -Name VoiceDucker -ErrorAction SilentlyContinue) { throw 'A VoiceDucker process remained after UI exit.' }
$lines = @(Get-Content -LiteralPath $DiagnosticsPath | Where-Object { $_ -match "pid=$AppProcessId " })
foreach ($marker in @('volume-events-unsubscribed count=', 'engine-disposed worker=stopped', 'application-shutdown cleanup=finished')) {
    if (-not ($lines | Select-String -SimpleMatch $marker)) { throw "Shutdown marker missing: $marker" }
}
if ($lines | Select-String -SimpleMatch 'shutdown-failed') { throw 'The app reported a resource cleanup failure.' }
Write-Output 'PASS: normal Close app released notifications, stopped the worker and exited the entire process.'
