# Builds NotifyFinish.exe and packs a ready-to-use release zip into dist\.
# Usage: powershell -ExecutionPolicy Bypass -File .\tools\package.ps1
# The version is read from tray\NotifyFinishTray.cs (AssemblyInformationalVersion).

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent

$m = Select-String -Path (Join-Path $root 'tray\NotifyFinishTray.cs') -Pattern 'AssemblyInformationalVersion\("([^"]+)"\)'
if (-not $m) { throw 'Version not found in tray\NotifyFinishTray.cs' }
$version = $m.Matches[0].Groups[1].Value

& (Join-Path $root 'build.ps1')

$dist  = Join-Path $root 'dist'
$stage = Join-Path $dist "NotifyFinish-v$version"
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Force $stage | Out-Null

# what a user needs: the app, its font, the scripts (fallback + macOS/Linux), the alert sources, docs
Copy-Item (Join-Path $root 'NotifyFinish.exe') $stage
Copy-Item (Join-Path $root 'fonts') $stage -Recurse
Copy-Item (Join-Path $root 'src') $stage -Recurse
foreach ($f in 'notify.ps1', 'notify.sh', 'build.ps1', 'README.md', 'LICENSE') { Copy-Item (Join-Path $root $f) $stage }
Copy-Item (Join-Path $root 'tray') $stage -Recurse

$zip = Join-Path $dist "NotifyFinish-v$version.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
# Write the zip entry by entry with '/' separators: Compress-Archive and ZipFile.CreateFromDirectory
# in Windows PowerShell 5.1 store '\' paths, which unzip as odd file names on macOS/Linux
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
$fs = [IO.File]::Open($zip, [IO.FileMode]::Create)
$za = New-Object IO.Compression.ZipArchive($fs, [IO.Compression.ZipArchiveMode]::Create)
Get-ChildItem $stage -Recurse -File | ForEach-Object {
    $rel = $_.FullName.Substring($stage.Length + 1).Replace('\', '/')
    [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($za, $_.FullName, $rel, [IO.Compression.CompressionLevel]::Optimal) | Out-Null
}
$za.Dispose(); $fs.Dispose()
Remove-Item $stage -Recurse -Force

Write-Host "Packed $zip"
