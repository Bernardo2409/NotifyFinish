# NotifyFinish

A tiny Windows alert for [Claude Code](https://claude.com/claude-code): when Claude finishes a response (or needs your input), a sound plays and a banner pops up at the top of your screen, on top of videos and games.

- **Green "Claude Finish"**: Claude finished its turn.
- **Orange "Claude needs you"**: Claude is waiting for a permission or an answer.

The banner blinks, closes itself after 8 seconds (or when clicked), and **does not steal focus**, so your game or video keeps the keyboard and mouse.

## Requirements

- Windows 10/11
- Windows PowerShell 5.1 (built in)
- Claude Code

## Setup

1. Clone the repo somewhere permanent:

   ```sh
   git clone https://github.com/Bernardo2409/NotifyFinish.git
   ```

2. Add the hooks to `~/.claude/settings.json` (merge with your existing settings), using the path where you cloned it:

   ```json
   {
     "hooks": {
       "Stop": [
         {
           "hooks": [
             {
               "type": "command",
               "command": "powershell -NoProfile -ExecutionPolicy Bypass -File \"D:/path/to/NotifyFinish/notify.ps1\"",
               "async": true
             }
           ]
         }
       ],
       "Notification": [
         {
           "matcher": "permission_prompt|elicitation_dialog",
           "hooks": [
             {
               "type": "command",
               "command": "powershell -NoProfile -ExecutionPolicy Bypass -File \"D:/path/to/NotifyFinish/notify.ps1\"",
               "async": true
             }
           ]
         }
       ]
     }
   }
   ```

3. In Claude Code, open `/hooks` once (or restart) so the new config is loaded.

## Test it

```powershell
'{"hook_event_name":"Stop"}' | powershell -NoProfile -ExecutionPolicy Bypass -File .\notify.ps1
```

## Customize

- **Duration:** add `-Seconds 15` to the end of the hook command.
- **Colors, size, position, text:** edit the top of `notify.ps1`.

## Limitations

Games running in **exclusive fullscreen** don't let any window draw on top of them, so you'll only hear the sound. Switch the game to **borderless / windowed fullscreen** to see the banner.

## License

[MIT](LICENSE)
