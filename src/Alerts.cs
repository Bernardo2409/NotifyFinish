// Shared by NotifyFinish.exe (tray app / --alert) and notify.ps1 (fallback):
// reads the hook JSON + settings and builds the right alert window.
using System;
using System.Drawing;
using System.IO;
using System.Media;
using System.Text.RegularExpressions;
using System.Windows.Forms;

public static class Alerts
{
    static readonly string ClaudeDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude");
    public static readonly string OffFlag = Path.Combine(ClaudeDir, "notifyfinish.off");
    public static readonly string StyleFile = Path.Combine(ClaudeDir, "notifyfinish.style");

    public static bool IsOff { get { return File.Exists(OffFlag); } }

    public static string Style
    {
        get
        {
            try { return File.Exists(StyleFile) ? File.ReadAllText(StyleFile).Trim().ToLowerInvariant() : "ufo"; }
            catch { return "ufo"; }
        }
    }

    // Tolerant field read (works even if the JSON has unescaped backslashes)
    public static string JsonField(string json, string field)
    {
        if (string.IsNullOrEmpty(json)) return null;
        Match m = Regex.Match(json, "\"" + Regex.Escape(field) + "\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
        return m.Success ? Regex.Replace(m.Groups[1].Value, "\\\\(.)", "$1") : null;
    }

    // Short messages (max 4 words); one is picked at random for each alert
    public static readonly string[] FinishedLines = {
        "Agent Finish",
        "Agent is done",
        "Mission accomplished",
        "Agent has landed",
        "Task complete, Agent",
        "Agent standing by",
        "Ready for orders",
        "Transmission complete",
    };
    public static readonly string[] NeedsYouLines = {
        "Agent needs you",
        "Agent awaits orders",
        "Your move, human",
        "Permission requested",
        "Awaiting your command",
        "Agent needs input",
        "Need your approval",
    };

    static readonly Random rnd = new Random();
    static string lastLine;
    static string Pick(string[] lines)
    {
        string s;
        do { s = lines[rnd.Next(lines.Length)]; } while (s == lastLine && lines.Length > 1);   // no immediate repeat
        lastLine = s;
        return s;
    }

    // Builds (but does not show) the alert for a hook event. Plays the sound.
    public static Form Create(string hookJson, int seconds, string appDir)
    {
        string ev = JsonField(hookJson, "hook_event_name") ?? "Stop";
        bool needsYou = ev == "Notification";
        string title = Pick(needsYou ? NeedsYouLines : FinishedLines);
        Color accent = needsYou ? Color.FromArgb(235, 140, 0) : Color.FromArgb(30, 170, 80);

        try { SystemSounds.Exclamation.Play(); } catch { }

        switch (Style)
        {
            case "simple":      return new SimpleAlert(title, accent, seconds);
            case "ufo-classic": return UfoClassic.Create(title, "", accent, seconds);
            default:            return UfoAlert.Create(title, "", accent, seconds, Path.Combine(appDir, "fonts"));
        }
    }

    // Standalone: build the alert and run it in this process until it closes
    public static void Run(string hookJson, int seconds, string appDir)
    {
        Application.Run(Create(hookJson, seconds, appDir));
    }
}

// The original flat banner: green/orange, blinks 3 times, closes itself (or on click)
public class SimpleAlert : Form
{
    protected override bool ShowWithoutActivation { get { return true; } }
    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams cp = base.CreateParams;
            cp.ExStyle |= 0x08000000; // WS_EX_NOACTIVATE  (never steals focus from your game)
            cp.ExStyle |= 0x00000008; // WS_EX_TOPMOST
            cp.ExStyle |= 0x00000080; // WS_EX_TOOLWINDOW  (no taskbar button)
            return cp;
        }
    }

    public SimpleAlert(string title, Color color, int seconds)
    {
        const int w = 520, h = 80;
        Rectangle screen = Screen.PrimaryScreen.WorkingArea;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        ShowInTaskbar = false;
        BackColor = color;
        Opacity = 0.95;
        Bounds = new Rectangle(screen.X + (screen.Width - w) / 2, screen.Y + 40, w, h);

        Label lbl = new Label();
        lbl.Text = title;
        lbl.ForeColor = Color.White;
        lbl.Font = new Font("Segoe UI", 22f, FontStyle.Bold);
        lbl.AutoSize = false;
        lbl.TextAlign = ContentAlignment.MiddleCenter;
        lbl.SetBounds(0, 0, w, h);
        Controls.Add(lbl);

        EventHandler close = delegate { Close(); };
        Click += close;
        lbl.Click += close;

        int ticks = 0;
        Timer timer = new Timer();
        timer.Interval = 500;
        timer.Tick += delegate
        {
            ticks++;
            if (ticks <= 6)
            {
                bool flash = ticks % 2 == 1;
                BackColor = flash ? Color.White : color;
                lbl.ForeColor = flash ? color : Color.White;
            }
            if (ticks >= seconds * 2) { timer.Stop(); Close(); }
        };
        timer.Start();
        FormClosed += delegate { timer.Dispose(); };
    }
}
