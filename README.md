<p align="center">
  <img src="assets/banner.png" alt="NotifyFinish" width="100%">
</p>

# NotifyFinish

A tiny alert for [Claude Code](https://claude.com/claude-code) on **Windows, macOS and Linux**: when Claude finishes a response (or needs your input), a sound plays and a notification pops up, even while you're watching a video or playing a game.

- **Green** ("Agent Finish", "Mission done", "Agent has landed", ...): Claude finished its turn.
- **Orange** ("Agent needs you", "Awaiting orders", "Input needed", ...): Claude is waiting for a permission or an answer.

Each alert picks one short message (max 4 words, max 16 characters so it fits around the spiral) at random. Edit the lists in `src/Alerts.cs` (Windows) and `notify.sh` (macOS/Linux).

| OS | Script | What you get |
|----|--------|--------------|
| Windows | `NotifyFinish.exe` (`notify.ps1` fallback) | 🛸 A UFO flies down from the top of the screen, opens a spinning 3D spiral cone with the message appearing left to right and slowly orbiting around it (smoke on landing and lift-off; the text tunes in like an old TV and turns sharp), hovers for 6 s (or until clicked) and flies back up. Always on top, **doesn't steal focus**, plus a system sound |
| macOS | `notify.sh` | Notification Center banner + system sound (Glass / Sosumi) |
| Linux | `notify.sh` | Desktop notification with critical urgency (`notify-send`, fallback `zenity` / `kdialog`) + sound |

## Requirements

- **Windows:** Windows 10/11. The app is built with the C# compiler that ships with Windows (nothing to install).
- **macOS:** nothing extra. Optional: `brew install terminal-notifier` for nicer notifications.
- **Linux:** `notify-send` (package `libnotify-bin` on Debian/Ubuntu, `libnotify` on Fedora/Arch). Sound uses `canberra-gtk-play` or `paplay` if available.

## Setup

1. Clone the repo somewhere permanent:

   ```sh
   git clone https://github.com/Bernardo2409/NotifyFinish.git
   ```

2. **Windows:** build the app once and start it (then right-click the tray icon > **Start with Windows**):

   ```powershell
   powershell -ExecutionPolicy Bypass -File .\build.ps1
   .\NotifyFinish.exe
   ```

3. Add the hooks to `~/.claude/settings.json` (merge with your existing settings). Replace `COMMAND` with the line for your OS:

   - **Windows** (recommended, no PowerShell per alert):
     `[ -f \"$HOME/.claude/notifyfinish.off\" ] || \"C:/path/to/NotifyFinish/NotifyFinish.exe\" --alert`
   - **Windows** (without building the app):
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

   The recommended Windows command needs Git Bash (which Claude Code uses on Windows): the `[ -f ... ] ||` part skips everything when alerts are turned off, and `NotifyFinish.exe --alert` hands the alert to the running tray app and exits in ~0.2 s. If the tray app isn't running, the exe shows the alert itself.

   `notify.sh` also works on Windows under Git Bash (it uses `NotifyFinish.exe`, or `notify.ps1` if the app isn't built), so you can use the same command everywhere if you sync your settings between machines.

4. In Claude Code, open `/hooks` once (or restart) so the new config is loaded.

### macOS: allow the notifications

The first time, macOS may attribute the notification to **Script Editor** (or `terminal-notifier`). Make sure it's allowed in **System Settings → Notifications**, and set its style to **Banners** or **Alerts**. Focus modes (Do Not Disturb, Gaming) will hide them.

## Turn alerts on / off

### Windows: tray app

A small app that sits in the system tray as a traffic light:
**green = alerts ON**, **red = alerts OFF**. **Left-click the icon to toggle.**

Right-click menu: *Alerts enabled*, *Style*, *Test alert*, *Start with Windows*, *Exit*.

**Style** (Windows):

| Style | Look | CPU per alert (measured) |
|-------|------|--------------------------|
| **UFO** (default) | Animated UFO, 3D spiral cone, smoke, orbiting text | ~4.5 s over the ~8 s animation |
| **UFO classic** | The first UFO: flat beam with scanlines and a rounded green/orange banner, no smoke or text effects | ~1.5 s |
| **Simple** | The original flat green/orange banner that blinks 3 times | ~0.1 s |

Memory: the tray app sits at ~30-37 MB and draws every alert itself, so an alert adds no extra process beyond the ~0.2 s hand-off (~10-20 MB, gone immediately). The UFO only costs CPU while it animates. The choice is saved in `~/.claude/notifyfinish.style` (`ufo`, `ufo-classic` or `simple`).

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

Both use the same switch: the file `~/.claude/notifyfinish.off`. While it exists, nothing is shown (with the recommended Windows hook, nothing even starts).

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
