# NotifyFinish - Windows alert when your agent finishes or needs your input.
#
# Fast path (recommended): the hooks call NotifyFinish.exe --alert directly (see README),
# which hands the alert to the running tray app - no PowerShell at all.
#
# This script is the fallback for when NotifyFinish.exe hasn't been built: it compiles the
# same alert code (src\*.cs) once, caches it in %TEMP%, and shows the alert in-process.
# Receives the hook JSON on stdin.
param(
    [int]$Seconds = 6,  # how long the alert stays (click it to dismiss earlier)
    [switch]$Force      # show even when alerts are turned off
)

$ErrorActionPreference = 'SilentlyContinue'

# Turned off from the tray app (or `notify.sh off`)? Do nothing.
if (-not $Force -and (Test-Path (Join-Path $env:USERPROFILE '.claude\notifyfinish.off'))) { exit 0 }

$raw = [Console]::In.ReadToEnd()

# Built app available? Let it do the work (it also reaches the tray app if it's running).
$exe = Join-Path $PSScriptRoot 'NotifyFinish.exe'
if (Test-Path $exe) {
    $exeArgs = @('--alert', '--seconds', $Seconds)
    if ($Force) { $exeArgs += '--force' }
    $raw | & $exe @exeArgs
    exit 0
}

# Otherwise compile the shared alert code once and run it here.
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
$files = 'Alerts.cs', 'UfoAlert.cs', 'UfoClassic.cs', 'FootballAlert.cs' | ForEach-Object { Join-Path $PSScriptRoot "src\$_" }
$code  = ($files | ForEach-Object { Get-Content $_ -Raw }) -join "`n"

$md5  = [System.Security.Cryptography.MD5]::Create()
$hash = -join ($md5.ComputeHash([Text.Encoding]::UTF8.GetBytes($code))[0..5] | ForEach-Object { $_.ToString('x2') })
$dll  = Join-Path $env:TEMP "NotifyFinishAlerts-$hash.dll"
if (-not (Test-Path $dll)) {
    $tmp = Join-Path $env:TEMP "NotifyFinishAlerts-$hash-$PID.dll"
    Add-Type -Path $files -ReferencedAssemblies System.Windows.Forms, System.Drawing `
             -OutputAssembly $tmp -OutputType Library -ErrorAction Stop
    Move-Item $tmp $dll -Force
}
if (-not ('Alerts' -as [type])) { Add-Type -Path $dll -ErrorAction Stop }

[Alerts]::Run($raw, $Seconds, $PSScriptRoot)
