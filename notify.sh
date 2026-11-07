#!/usr/bin/env bash
# NotifyFinish - notification + sound when Claude Code finishes or needs your input.
# macOS and Linux version. Called by the Stop / Notification hooks.
# Receives the hook JSON on stdin.
#
# Usage: notify.sh [seconds]   (how long the notification stays up, default 8)

SECONDS_UP="${1:-8}"

# --- Read the hook JSON (no jq needed) ----------------------------------------
raw="$(cat 2>/dev/null)"

json_field() {
    printf '%s' "$raw" | tr -d '\n' | sed -n "s/.*\"$1\"[[:space:]]*:[[:space:]]*\"\(\([^\"\\\\]\|\\\\.\)*\)\".*/\1/p" | sed 's/\\"/"/g; s/\\\\/\\/g'
}

event="$(json_field hook_event_name)"
message="$(json_field message)"
cwd="$(json_field cwd)"
project="$(basename "${cwd:-Claude Code}")"

if [ "$event" = "Notification" ]; then
    title="Claude needs you"
    body="$project - ${message:-Waiting for your input}"
    urgent=1
else
    title="Claude Finish"
    body=""
    urgent=0
fi

has() { command -v "$1" >/dev/null 2>&1; }

# --- macOS --------------------------------------------------------------------
notify_macos() {
    if [ "$urgent" = 1 ]; then sound="/System/Library/Sounds/Sosumi.aiff"
    else sound="/System/Library/Sounds/Glass.aiff"; fi
    [ -f "$sound" ] && afplay "$sound" >/dev/null 2>&1 &

    if has terminal-notifier; then
        terminal-notifier -title "$title" -message "${body:- }" -group claude-notify-finish >/dev/null 2>&1
    else
        # Escape for AppleScript string literals
        esc() { printf '%s' "$1" | sed 's/\\/\\\\/g; s/"/\\"/g'; }
        osascript -e "display notification \"$(esc "$body")\" with title \"$(esc "$title")\"" >/dev/null 2>&1
    fi
}

# --- Linux --------------------------------------------------------------------
notify_linux() {
    # Sound: try the freedesktop sound theme first, then fall back
    if [ "$urgent" = 1 ]; then sid="dialog-warning"; else sid="complete"; fi
    if has canberra-gtk-play; then
        canberra-gtk-play -i "$sid" >/dev/null 2>&1 &
    elif has paplay && [ -f "/usr/share/sounds/freedesktop/stereo/$sid.oga" ]; then
        paplay "/usr/share/sounds/freedesktop/stereo/$sid.oga" >/dev/null 2>&1 &
    else
        printf '\a' > /dev/tty 2>/dev/null
    fi

    # Popup: "critical" urgency shows on top of fullscreen apps on most desktops
    if has notify-send; then
        notify-send -u critical -t "$((SECONDS_UP * 1000))" -a "Claude Code" \
            -i dialog-information "$title" "$body" >/dev/null 2>&1
    elif has zenity; then
        zenity --notification --text="$title${body:+\n$body}" >/dev/null 2>&1 &
    elif has kdialog; then
        kdialog --title "$title" --passivepopup "${body:-$title}" "$SECONDS_UP" >/dev/null 2>&1 &
    fi
}

# --- Windows (Git Bash / MSYS / Cygwin): hand off to the PowerShell version ---
notify_windows() {
    script_dir="$(cd "$(dirname "$0")" && pwd)"
    printf '%s' "$raw" | powershell -NoProfile -ExecutionPolicy Bypass \
        -File "$script_dir/notify.ps1" -Seconds "$SECONDS_UP"
}

case "$(uname -s)" in
    Darwin)               notify_macos ;;
    Linux)                notify_linux ;;
    MINGW*|MSYS*|CYGWIN*) notify_windows ;;
esac

exit 0
