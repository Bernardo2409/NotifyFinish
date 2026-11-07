# NotifyFinish - visual alert + sound when Claude Code finishes or needs your input.
# Called by the Stop / Notification hooks. Receives the hook JSON on stdin.
param(
    [int]$Seconds = 8   # how long the alert stays on screen (click to close earlier)
)

$ErrorActionPreference = 'SilentlyContinue'

# --- Read the hook JSON -------------------------------------------------------
$raw = [Console]::In.ReadToEnd()
$data = $null
if ($raw) { $data = $raw | ConvertFrom-Json }

$event   = if ($data.hook_event_name) { $data.hook_event_name } else { 'Stop' }
$project = if ($data.cwd) { Split-Path $data.cwd -Leaf } else { 'Claude Code' }

if ($event -eq 'Notification') {
    $title = 'Claude needs you'
    $msg   = if ($data.message) { "$project  -  $($data.message)" } else { "$project  -  Waiting for your input" }
} else {
    $title = 'Claude Finish'
    $msg   = ''
}

# --- Sound --------------------------------------------------------------------
[System.Media.SystemSounds]::Exclamation.Play()

# --- Always-on-top window that does NOT steal focus (won't kick a game out of fullscreen)
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
Add-Type -WarningAction SilentlyContinue -ReferencedAssemblies System.Windows.Forms, System.Drawing -TypeDefinition @'
using System;
using System.Windows.Forms;
public class NoFocusForm : Form {
    protected override bool ShowWithoutActivation { get { return true; } }
    protected override CreateParams CreateParams {
        get {
            CreateParams cp = base.CreateParams;
            cp.ExStyle |= 0x08000000; // WS_EX_NOACTIVATE
            cp.ExStyle |= 0x00000008; // WS_EX_TOPMOST
            cp.ExStyle |= 0x00000080; // WS_EX_TOOLWINDOW (no taskbar icon)
            return cp;
        }
    }
}
'@

if ($event -eq 'Notification') { $color = [System.Drawing.Color]::FromArgb(230, 140, 0) }  # orange
else                           { $color = [System.Drawing.Color]::FromArgb(30, 160, 70) }  # green

$screen = [System.Windows.Forms.Screen]::PrimaryScreen.WorkingArea
$w = 520
$h = if ($msg) { 130 } else { 80 }

$form = New-Object NoFocusForm
$form.FormBorderStyle = 'None'
$form.StartPosition   = 'Manual'
$form.TopMost         = $true
$form.ShowInTaskbar   = $false
$form.BackColor       = $color
$form.Opacity         = 0.95
$form.Size            = New-Object System.Drawing.Size($w, $h)
$form.Location        = New-Object System.Drawing.Point(($screen.X + ($screen.Width - $w) / 2), ($screen.Y + 40))

$lblTitle = New-Object System.Windows.Forms.Label
$lblTitle.Text      = $title
$lblTitle.ForeColor = 'White'
$lblTitle.Font      = New-Object System.Drawing.Font('Segoe UI', 22, [System.Drawing.FontStyle]::Bold)
$lblTitle.AutoSize  = $false
$lblTitle.TextAlign = 'MiddleCenter'
if ($msg) { $lblTitle.SetBounds(0, 12, $w, 50) } else { $lblTitle.SetBounds(0, 0, $w, $h) }
$form.Controls.Add($lblTitle)
$labels = @($lblTitle)

if ($msg) {
    $lblMsg = New-Object System.Windows.Forms.Label
    $lblMsg.Text      = $msg
    $lblMsg.ForeColor = 'White'
    $lblMsg.Font      = New-Object System.Drawing.Font('Segoe UI', 12)
    $lblMsg.AutoSize  = $false
    $lblMsg.TextAlign = 'MiddleCenter'
    $lblMsg.SetBounds(10, 68, ($w - 20), 50)
    $form.Controls.Add($lblMsg)
    $labels += $lblMsg
}

# Click anywhere to close
$close = { $form.Close() }
$form.Add_Click($close)
foreach ($l in $labels) { $l.Add_Click($close) }

# Blink + auto-close
$script:ticks = 0
$timer = New-Object System.Windows.Forms.Timer
$timer.Interval = 500
$timer.Add_Tick({
    $script:ticks++
    if ($script:ticks -le 6) {
        $form.BackColor = if ($script:ticks % 2) { [System.Drawing.Color]::White } else { $color }
        $fg = if ($script:ticks % 2) { $color } else { [System.Drawing.Color]::White }
        foreach ($l in $labels) { $l.ForeColor = $fg }
    }
    if ($script:ticks -ge ($Seconds * 2)) { $timer.Stop(); $form.Close() }
})
$timer.Start()

[System.Windows.Forms.Application]::Run($form)
