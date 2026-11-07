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

# App icon: the green traffic-light dot, drawn at several sizes and packed into an .ico
$ico = Join-Path $PSScriptRoot 'NotifyFinish.ico'
if (-not (Test-Path $ico)) {
    Add-Type -AssemblyName System.Drawing
    $pngs = foreach ($s in 16, 24, 32, 48, 64, 256) {
        $bmp = New-Object System.Drawing.Bitmap $s, $s
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        $g.SmoothingMode = 'AntiAlias'
        $k = $s / 32.0
        $g.FillEllipse((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(40, 40, 40))), 1*$k, 1*$k, 30*$k, 30*$k)
        $g.FillEllipse((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(30, 190, 80))), 5*$k, 5*$k, 22*$k, 22*$k)
        $g.FillEllipse((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(90, 255, 255, 255))), 9*$k, 7*$k, 10*$k, 7*$k)
        $g.Dispose()
        $ms = New-Object System.IO.MemoryStream
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
        ,@($s, $ms.ToArray())
    }
    $fs = [System.IO.File]::Create($ico); $w = New-Object System.IO.BinaryWriter $fs
    $w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$pngs.Count)      # ICONDIR
    $offset = 6 + 16 * $pngs.Count
    foreach ($p in $pngs) {                                                       # ICONDIRENTRY (PNG-compressed)
        $sz = if ($p[0] -ge 256) { 0 } else { $p[0] }
        $w.Write([byte]$sz); $w.Write([byte]$sz); $w.Write([byte]0); $w.Write([byte]0)
        $w.Write([uint16]1); $w.Write([uint16]32); $w.Write([uint32]$p[1].Length); $w.Write([uint32]$offset)
        $offset += $p[1].Length
    }
    foreach ($p in $pngs) { $w.Write([byte[]]$p[1]) }
    $w.Close()
}

& $csc /nologo /target:winexe /optimize+ "/out:$out" "/win32icon:$ico" `
    /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Core.dll `
    $sources
if ($LASTEXITCODE -ne 0) { throw "Build failed ($LASTEXITCODE)" }
Write-Host "Built $out"
