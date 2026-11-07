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
    (Join-Path $PSScriptRoot 'src\UfoClassic.cs'),
    (Join-Path $PSScriptRoot 'src\Logo.cs')
)

# App icon: the brand UFO (src/Logo.cs) at several sizes, packed into an .ico (PNG entries)
$ico = Join-Path $PSScriptRoot 'NotifyFinish.ico'
Add-Type -AssemblyName System.Drawing
if (-not ('Logo' -as [type])) { Add-Type -Path (Join-Path $PSScriptRoot 'src\Logo.cs') -ReferencedAssemblies System.Drawing }
$pngs = foreach ($s in 16, 24, 32, 48, 64, 256) {
    $bmp = [Logo]::Mark($s, $true)
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
    ,@($s, $ms.ToArray())
}
$fs = [System.IO.File]::Create($ico); $w = New-Object System.IO.BinaryWriter $fs
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$pngs.Count)      # ICONDIR
$offset = 6 + 16 * $pngs.Count
foreach ($p in $pngs) {                                                       # ICONDIRENTRY
    $sz = if ($p[0] -ge 256) { 0 } else { $p[0] }
    $w.Write([byte]$sz); $w.Write([byte]$sz); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32); $w.Write([uint32]$p[1].Length); $w.Write([uint32]$offset)
    $offset += $p[1].Length
}
foreach ($p in $pngs) { $w.Write([byte[]]$p[1]) }
$w.Close()

& $csc /nologo /target:winexe /optimize+ "/out:$out" "/win32icon:$ico" `
    /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Core.dll `
    $sources
if ($LASTEXITCODE -ne 0) { throw "Build failed ($LASTEXITCODE)" }
Write-Host "Built $out"
