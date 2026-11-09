#!/usr/bin/env bash
# NotifyFinish - notification + sound when your agent finishes or needs your input.
# macOS and Linux version. Called by the Stop / Notification hooks.
# Receives the hook JSON on stdin.
#
# Usage: notify.sh [seconds]          show the alert (default 8 s) - used by the hooks
#        notify.sh on|off|toggle      turn alerts on/off (same switch as the Windows tray app)
#        notify.sh status             print ON or OFF

FLAG="$HOME/.claude/notifyfinish.off"

cmd="$1"
if [ "$cmd" = "toggle" ]; then
    if [ -f "$FLAG" ]; then cmd=on; else cmd=off; fi
fi

case "$cmd" in
    on)     rm -f "$FLAG"; echo "NotifyFinish: ON"; exit 0 ;;
    off)    mkdir -p "$HOME/.claude" && echo off > "$FLAG"; echo "NotifyFinish: OFF"; exit 0 ;;
    status) if [ -f "$FLAG" ]; then echo "NotifyFinish: OFF"; else echo "NotifyFinish: ON"; fi; exit 0 ;;
esac

# Alerts turned off? Do nothing.
[ -f "$FLAG" ] && exit 0

SECONDS_UP="${1:-8}"

# --- Read the hook JSON (no jq needed) ----------------------------------------
raw="$(cat 2>/dev/null)"

# Portable (GNU and BSD/macOS sed): plain BRE, no \| alternation.
# Only used for simple values like hook_event_name, which never contain quotes.
json_field() {
    printf '%s' "$raw" | tr -d '\n' | sed -n 's/.*"'"$1"'"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p'
}

event="$(json_field hook_event_name)"

# Short messages (max 4 words, max 16 characters); one is picked at random for each alert
# (keep in sync with src/Alerts.cs)
FINISHED_LINES=("Agent Finish" "Agent is done" "Mission done" "Agent has landed"
                "Task complete" "Ready for orders" "All done here" "Job done")
NEEDS_YOU_LINES=("Agent needs you" "Awaiting orders" "Input needed"
                 "Need your OK" "Your call, boss")

if [ "$event" = "Notification" ]; then
    title="${NEEDS_YOU_LINES[RANDOM % ${#NEEDS_YOU_LINES[@]}]}"
    body=""
    urgent=1
else
    title="${FINISHED_LINES[RANDOM % ${#FINISHED_LINES[@]}]}"
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
        terminal-notifier -title "$title" -message "${body:- }" -group notify-finish >/dev/null 2>&1
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
        notify-send -u critical -t "$((SECONDS_UP * 1000))" -a "NotifyFinish" \
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
    if [ -x "$script_dir/NotifyFinish.exe" ] || [ -f "$script_dir/NotifyFinish.exe" ]; then
        # fast path: the built app (hands off to the tray app if it's running)
        printf '%s' "$raw" | "$script_dir/NotifyFinish.exe" --alert --seconds "$SECONDS_UP"
    else
        printf '%s' "$raw" | powershell -NoProfile -ExecutionPolicy Bypass \
            -File "$script_dir/notify.ps1" -Seconds "$SECONDS_UP"
    fi
}

case "$(uname -s)" in
    Darwin)               notify_macos ;;
    Linux)                notify_linux ;;
    MINGW*|MSYS*|CYGWIN*) notify_windows ;;
esac

exit 0
