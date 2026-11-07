# Builds NotifyFinish.exe (tray app + alerts) with the C# compiler that ships with Windows.
# Usage: powershell -ExecutionPolicy Bypass -File .\build.ps1

$ErrorActionPreference = 'Stop'
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
if (-not (Test-Path $csc)) { throw 'C# compiler (csc.exe) from .NET Framework 4 not found.' }

$out = Join-Path $PSScriptRoot 'NotifyFinish.exe'
$sources = @(
    (Join-Path $PSScriptRoot 'tray\NotifyFinishTray.cs'),
    (Join-Path $PSScriptRoot 'src\Alerts.cs'),
    (Join-Path $PSScriptRoot 'src\UfoAlert.cs'),
    (Join-Path $PSScriptRoot 'src\UfoClassic.cs')
)
& $csc /nologo /target:winexe /optimize+ "/out:$out" `
    /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Core.dll `
    $sources
if ($LASTEXITCODE -ne 0) { throw "Build failed ($LASTEXITCODE)" }
Write-Host "Built $out"
