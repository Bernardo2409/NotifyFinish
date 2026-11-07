# Generates the brand images in assets/ from the same drawing as the app icon (src/Logo.cs):
#   assets/logo.png    512x512 UFO mark (transparent)
#   assets/banner.png  1280x400 README banner: UFO + "NotifyFinish" wordmark + tagline
# Usage: powershell -ExecutionPolicy Bypass -File .\tools\make-brand.ps1

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Add-Type -AssemblyName System.Drawing
if (-not ('Logo' -as [type])) { Add-Type -Path (Join-Path $root 'src\Logo.cs') -ReferencedAssemblies System.Drawing }
$assets = Join-Path $root 'assets'
New-Item -ItemType Directory -Force $assets | Out-Null

# --- logo.png ---
$logo = New-Object System.Drawing.Bitmap 512, 512
$g = [System.Drawing.Graphics]::FromImage($logo)
$g.Clear([System.Drawing.Color]::Transparent)
[Logo]::DrawMark($g, 0, 0, 512, $true, $true)
$g.Dispose()
$logo.Save((Join-Path $assets 'logo.png'), [System.Drawing.Imaging.ImageFormat]::Png)
$logo.Dispose()

# --- banner.png ---
$W = 1280; $H = 400
$fonts = New-Object System.Drawing.Text.PrivateFontCollection
$fonts.AddFontFile((Join-Path $root 'fonts\SpaceGrotesk-Bold.ttf'))
$fonts.AddFontFile((Join-Path $root 'fonts\SpaceGrotesk-Regular.ttf'))
$fam = $fonts.Families[0]

$b = New-Object System.Drawing.Bitmap $W, $H
$g = [System.Drawing.Graphics]::FromImage($b)
$g.SmoothingMode = 'AntiAlias'
$g.TextRenderingHint = 'AntiAlias'

# night-sky background with a soft green glow behind the UFO
$bg = New-Object System.Drawing.Drawing2D.LinearGradientBrush ((New-Object System.Drawing.Point 0, 0)), ((New-Object System.Drawing.Point 0, $H)),
      ([System.Drawing.Color]::FromArgb(12, 16, 28)), ([System.Drawing.Color]::FromArgb(24, 30, 48))
$g.FillRectangle($bg, 0, 0, $W, $H)
$glow = New-Object System.Drawing.Drawing2D.GraphicsPath
$glow.AddEllipse(60, 40, 380, 380)
$pg = New-Object System.Drawing.Drawing2D.PathGradientBrush $glow
$pg.CenterColor = [System.Drawing.Color]::FromArgb(70, 30, 190, 80)
$pg.SurroundColors = @([System.Drawing.Color]::FromArgb(0, 30, 190, 80))
$g.FillPath($pg, $glow)

# stars
$rnd = New-Object System.Random 7
for ($i = 0; $i -lt 110; $i++) {
    $x = $rnd.Next(0, $W); $y = $rnd.Next(0, $H); $r = 0.6 + $rnd.NextDouble() * 1.6
    $a = 60 + $rnd.Next(0, 160)
    $g.FillEllipse((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb($a, 255, 255, 255))), [float]($x - $r), [float]($y - $r), [float]($r * 2), [float]($r * 2))
}

# the UFO
[Logo]::DrawMark($g, 80, 40, 340, $true, $true)

# wordmark with a soft glow (like the alert text)
$title = New-Object System.Drawing.Font $fam, 104, ([System.Drawing.FontStyle]::Bold), ([System.Drawing.GraphicsUnit]::Pixel)
$tag   = New-Object System.Drawing.Font $fam, 32, ([System.Drawing.FontStyle]::Regular), ([System.Drawing.GraphicsUnit]::Pixel)
$tx = 470; $ty = 118
for ($i = 6; $i -ge 1; $i--) {
    $gb = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(10, 120, 255, 160))
    foreach ($d in @(@(-$i, 0), @($i, 0), @(0, -$i), @(0, $i))) { $g.DrawString('NotifyFinish', $title, $gb, ($tx + $d[0]), ($ty + $d[1])) }
}
$g.DrawString('NotifyFinish', $title, [System.Drawing.Brushes]::White, $tx, $ty)
$g.DrawString('Know when your agent is done.', $tag, (New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(200, 210, 225, 235))), ($tx + 6), ($ty + 132))

$g.Dispose()
$b.Save((Join-Path $assets 'banner.png'), [System.Drawing.Imaging.ImageFormat]::Png)
$b.Dispose()
Write-Host "Wrote assets\logo.png and assets\banner.png"
