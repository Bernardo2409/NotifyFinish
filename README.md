# NotifyFinish

A tiny alert for [Claude Code](https://claude.com/claude-code) on **Windows, macOS and Linux**: when Claude finishes a response (or needs your input), a sound plays and a notification pops up, even while you're watching a video or playing a game.

- **"Claude Finish"**: Claude finished its turn.
- **"Claude needs you"**: Claude is waiting for a permission or an answer.

| OS | Script | What you get |
|----|--------|--------------|
| Windows | `notify.ps1` | 🛸 A UFO flies down from the top of the screen, opens a spinning 3D spiral vortex with the message slowly orbiting around it (smoke on landing and lift-off; the text tunes in like an old TV and turns sharp), hovers for 6 s (or until clicked) and flies back up. Always on top, **doesn't steal focus**, plus a system sound |
| macOS | `notify.sh` | Notification Center banner + system sound (Glass / Sosumi) |
| Linux | `notify.sh` | Desktop notification with critical urgency (`notify-send`, fallback `zenity` / `kdialog`) + sound |

## Requirements

- **Windows:** Windows 10/11 with Windows PowerShell 5.1 (built in).
- **macOS:** nothing extra. Optional: `brew install terminal-notifier` for nicer notifications.
- **Linux:** `notify-send` (package `libnotify-bin` on Debian/Ubuntu, `libnotify` on Fedora/Arch). Sound uses `canberra-gtk-play` or `paplay` if available.

## Setup

1. Clone the repo somewhere permanent:

   ```sh
   git clone https://github.com/Bernardo2409/NotifyFinish.git
   ```

2. Add the hooks to `~/.claude/settings.json` (merge with your existing settings). Replace `COMMAND` with the line for your OS:

   - **Windows:**
     `powershell -NoProfile -ExecutionPolicy Bypass -File \"C:/path/to/NotifyFinish/notify.ps1\"`
   - **macOS / Linux:**
     `bash \"/path/to/NotifyFinish/notify.sh\"`

   ```json
   {
     "hooks": {
       "Stop": [
         {
           "hooks": [
             { "type": "command", "command": "COMMAND", "async": true }
           ]
         }
       ],
       "Notification": [
         {
           "matcher": "permission_prompt|elicitation_dialog",
           "hooks": [
             { "type": "command", "command": "COMMAND", "async": true }
           ]
         }
       ]
     }
   }
   ```

   `notify.sh` also works on Windows under Git Bash (it hands off to `notify.ps1`), so you can use the same command everywhere if you sync your settings between machines.

3. In Claude Code, open `/hooks` once (or restart) so the new config is loaded.

### macOS: allow the notifications

The first time, macOS may attribute the notification to **Script Editor** (or `terminal-notifier`). Make sure it's allowed in **System Settings → Notifications**, and set its style to **Banners** or **Alerts**. Focus modes (Do Not Disturb, Gaming) will hide them.

## Turn alerts on / off

### Windows: tray app

A small app that sits in the system tray as a traffic light:
**green = alerts ON**, **red = alerts OFF**. **Left-click the icon to toggle.**

Right-click menu: *Alerts enabled*, *Test alert*, *Start with Windows*, *Exit*.

Build it once (uses the C# compiler that ships with Windows, nothing to install):

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1
.\NotifyFinish.exe
```

Then right-click the tray icon → **Start with Windows** so it's always there.

### macOS / Linux

```sh
bash notify.sh off      # turn alerts off
bash notify.sh on       # turn alerts on
bash notify.sh toggle
bash notify.sh status
```

Both use the same switch: the file `~/.claude/notifyfinish.off`. While it exists, the hooks still run but show nothing.

## Test it

```sh
# macOS / Linux
echo '{"hook_event_name":"Stop"}' | bash notify.sh
```

```powershell
# Windows
'{"hook_event_name":"Stop"}' | powershell -NoProfile -ExecutionPolicy Bypass -File .\notify.ps1
```

## Customize

- **Duration:** add `-Seconds 15` (Windows) or `15` (macOS/Linux) to the end of the hook command.
- **Windows UFO:** the first alert compiles the UFO window once (~1 s) and caches it in `%TEMP%`; after that it starts instantly.
- **Colors, size, position, text:** edit the top of `notify.ps1` / `notify.sh`.

## Limitations

- **Windows:** games in **exclusive fullscreen** don't let any window draw on top of them, so you'll only hear the sound. Use **borderless / windowed fullscreen** to see the banner.
- **macOS:** notifications follow your Focus / Do Not Disturb settings.
- **Linux:** how notifications look and whether they show over fullscreen apps depends on your desktop environment.

## License

[MIT](LICENSE)

The UFO text uses [Space Grotesk](https://github.com/floriankarsten/space-grotesk) (bundled in `fonts/`), licensed under the [SIL Open Font License 1.1](fonts/OFL.txt).
