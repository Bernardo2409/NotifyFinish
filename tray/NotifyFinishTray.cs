// NotifyFinish tray app (Windows).
// Lives in the system tray as the NotifyFinish UFO:
//   green alien = alerts ON, grey UFO with red lights = alerts OFF. Left-click the icon to toggle.
// The on/off state is a flag file (~/.claude/notifyfinish.off) that
// notify.ps1 / notify.sh check before showing anything.

using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;
using System.Reflection;

[assembly: AssemblyTitle("NotifyFinish")]
[assembly: AssemblyProduct("NotifyFinish")]
[assembly: AssemblyDescription("Know when your agent is done - alerts for AI coding agents")]
[assembly: AssemblyCopyright("Copyright (c) 2026 Bernardo Coelho - MIT License")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]
[assembly: AssemblyInformationalVersion("1.0.0")]

static class Program
{
    [DllImport("user32.dll")] static extern bool SetProcessDpiAwarenessContext(IntPtr value);
    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();

    // Without this Windows renders the app at 100% and stretches it -> blurry menus
    static void EnableHighDpi()
    {
        try { if (SetProcessDpiAwarenessContext(new IntPtr(-4))) return; } // Per-monitor v2 (Win10 1703+)
        catch (EntryPointNotFoundException) { }
        try { SetProcessDPIAware(); } catch { }
    }

    public static string PipeName { get { return "NotifyFinish_" + Environment.UserName; } }

    [STAThread]
    static void Main(string[] args)
    {
        if (Array.IndexOf(args, "--alert") >= 0) { AlertClient(args); return; }

        EnableHighDpi();
        bool created;
        using (Mutex mutex = new Mutex(true, "NotifyFinishTray_SingleInstance", out created))
        {
            if (!created) return; // already running
            Application.EnableVisualStyles();
            Application.Run(new TrayContext());
        }
    }

    // `NotifyFinish.exe --alert [--seconds N] [--force]`  (what the agent hooks run)
    // Hands the hook JSON to the running tray app and exits at once; if the tray app
    // isn't running, shows the alert itself. No PowerShell involved either way.
    static void AlertClient(string[] args)
    {
        bool force = Array.IndexOf(args, "--force") >= 0;
        if (Alerts.IsOff && !force) return;

        int seconds = 6;
        int si = Array.IndexOf(args, "--seconds");
        if (si >= 0 && si + 1 < args.Length) int.TryParse(args[si + 1], out seconds);

        string json = "";
        try
        {
            using (StreamReader r = new StreamReader(Console.OpenStandardInput(), System.Text.Encoding.UTF8))
                json = r.ReadToEnd();
        }
        catch { }

        string msg = seconds + " " + (force ? "1" : "0") + "\n" + json;
        try
        {
            using (System.IO.Pipes.NamedPipeClientStream c =
                   new System.IO.Pipes.NamedPipeClientStream(".", PipeName, System.IO.Pipes.PipeDirection.Out))
            {
                c.Connect(400);
                byte[] data = System.Text.Encoding.UTF8.GetBytes(msg);
                c.Write(data, 0, data.Length);
                c.Flush();
            }
            return;   // the tray app will show it
        }
        catch { }     // tray app not running: show it ourselves

        EnableHighDpi();
        Application.EnableVisualStyles();
        Alerts.Run(json, seconds, AppDomain.CurrentDomain.BaseDirectory);
    }
}

class TrayContext : ApplicationContext
{
    static readonly string FlagPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "notifyfinish.off");
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string RunName = "NotifyFinish";

    static readonly string StylePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "notifyfinish.style");

    readonly NotifyIcon icon;
    readonly ToolStripMenuItem enabledItem;
    readonly ToolStripMenuItem startupItem;
    readonly ToolStripMenuItem ufoItem;
    readonly ToolStripMenuItem classicItem;
    readonly ToolStripMenuItem simpleItem;
    readonly Icon onIcon;
    readonly Icon offIcon;
    readonly System.Windows.Forms.Timer refresh;

    public TrayContext()
    {
        onIcon = MakeIcon(true);    // the brand UFO: green alien, yellow lights
        offIcon = MakeIcon(false);  // greyed out with red lights

        enabledItem = new ToolStripMenuItem("Alerts enabled", null, delegate { Toggle(); });
        startupItem = new ToolStripMenuItem("Start with Windows", null, delegate { ToggleStartup(); });

        // Style ids on disk stay "ufo" / "ufo-classic" / "simple"; these are just the display names
        ufoItem = new ToolStripMenuItem("Mothership - the full show", null, delegate { SetStyle("ufo"); });
        ufoItem.ToolTipText = "Animated UFO, 3D spiral beam, smoke and orbiting text (uses the most CPU)";
        classicItem = new ToolStripMenuItem("Scout Ship - light UFO", null, delegate { SetStyle("ufo-classic"); });
        classicItem.ToolTipText = "The original UFO: simple beam and a rounded banner";
        simpleItem = new ToolStripMenuItem("Beacon - minimal banner", null, delegate { SetStyle("simple"); });
        simpleItem.ToolTipText = "A flat banner that blinks 3 times (lightest)";
        ToolStripMenuItem styleMenu = new ToolStripMenuItem("Style");
        styleMenu.DropDownItems.Add(ufoItem);
        styleMenu.DropDownItems.Add(classicItem);
        styleMenu.DropDownItems.Add(simpleItem);

        ContextMenuStrip menu = new ContextMenuStrip();
        ToolStripMenuItem about = new ToolStripMenuItem("NotifyFinish " + Version);
        about.Enabled = false;
        menu.Items.Add(about);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(enabledItem);
        menu.Items.Add(styleMenu);
        menu.Items.Add(new ToolStripMenuItem("Test alert", null, delegate { Test(); }));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(startupItem);
        menu.Items.Add(new ToolStripMenuItem("Exit", null, delegate { Quit(); }));

        icon = new NotifyIcon();
        icon.ContextMenuStrip = menu;
        icon.MouseClick += delegate(object s, MouseEventArgs e) { if (e.Button == MouseButtons.Left) Toggle(); };
        icon.Visible = true;

        // Pick up changes made elsewhere (e.g. `notify.sh off`)
        refresh = new System.Windows.Forms.Timer();
        refresh.Interval = 2000;
        refresh.Tick += delegate { UpdateUi(); };
        refresh.Start();

        UpdateUi();

        // Alerts arrive from the hooks (`NotifyFinish.exe --alert`) over a named pipe
        ui = new Control();
        IntPtr forceHandle = ui.Handle;   // create it on the UI thread so BeginInvoke works
        Thread listener = new Thread(PipeLoop);
        listener.IsBackground = true;
        listener.Start();
    }

    readonly Control ui;

    void PipeLoop()
    {
        while (true)
        {
            try
            {
                using (System.IO.Pipes.NamedPipeServerStream s = new System.IO.Pipes.NamedPipeServerStream(
                    Program.PipeName, System.IO.Pipes.PipeDirection.In, 4))
                {
                    s.WaitForConnection();
                    string msg;
                    using (StreamReader r = new StreamReader(s, System.Text.Encoding.UTF8)) msg = r.ReadToEnd();
                    ui.BeginInvoke((MethodInvoker)delegate { ShowAlert(msg); });
                }
            }
            catch { Thread.Sleep(500); }
        }
    }

    // msg = "<seconds> <force 0|1>\n<hook json>"
    void ShowAlert(string msg)
    {
        try
        {
            int nl = msg.IndexOf('\n');
            string head = nl >= 0 ? msg.Substring(0, nl) : msg;
            string json = nl >= 0 ? msg.Substring(nl + 1) : "";
            string[] parts = head.Split(' ');
            int seconds = 6;
            if (parts.Length > 0) int.TryParse(parts[0], out seconds);
            bool force = parts.Length > 1 && parts[1] == "1";
            if (!force && Alerts.IsOff) return;

            Form f = Alerts.Create(json, seconds, AppDomain.CurrentDomain.BaseDirectory);
            f.FormClosed += delegate { f.Dispose(); GC.Collect(); };   // give the memory back right away
            f.Show();
        }
        catch (Exception ex)
        {
            icon.ShowBalloonTip(3000, "NotifyFinish", "Could not show alert: " + ex.Message, ToolTipIcon.Error);
        }
    }

    static readonly string Version = ((AssemblyInformationalVersionAttribute)Attribute.GetCustomAttribute(
        typeof(TrayContext).Assembly, typeof(AssemblyInformationalVersionAttribute))).InformationalVersion;

    static bool IsEnabled { get { return !File.Exists(FlagPath); } }

    static string Style
    {
        get
        {
            try { return File.Exists(StylePath) ? File.ReadAllText(StylePath).Trim().ToLowerInvariant() : "ufo"; }
            catch { return "ufo"; }
        }
    }

    void SetStyle(string style)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StylePath));
            File.WriteAllText(StylePath, style);
        }
        catch (Exception ex)
        {
            icon.ShowBalloonTip(3000, "NotifyFinish", "Could not change style: " + ex.Message, ToolTipIcon.Error);
        }
        UpdateUi();
    }

    void Toggle()
    {
        try
        {
            if (IsEnabled)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FlagPath));
                File.WriteAllText(FlagPath, "off");
            }
            else
            {
                File.Delete(FlagPath);
            }
        }
        catch (Exception ex)
        {
            icon.ShowBalloonTip(3000, "NotifyFinish", "Could not change state: " + ex.Message, ToolTipIcon.Error);
        }
        UpdateUi();
    }

    void UpdateUi()
    {
        bool on = IsEnabled;
        icon.Icon = on ? onIcon : offIcon;
        icon.Text = "NotifyFinish " + Version + (on ? ": ON (click to turn off)" : ": OFF (click to turn on)");
        enabledItem.Checked = on;
        startupItem.Checked = IsStartup;
        string style = Style;
        ufoItem.Checked = style != "simple" && style != "ufo-classic";
        classicItem.Checked = style == "ufo-classic";
        simpleItem.Checked = style == "simple";
    }

    void Test()
    {
        ShowAlert("6 1\n{\"hook_event_name\":\"Stop\"}");
    }

    static bool IsStartup
    {
        get
        {
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey))
                return k != null && k.GetValue(RunName) != null;
        }
    }

    void ToggleStartup()
    {
        using (RegistryKey k = Registry.CurrentUser.CreateSubKey(RunKey))
        {
            if (IsStartup) k.DeleteValue(RunName, false);
            else k.SetValue(RunName, "\"" + Application.ExecutablePath + "\"");
        }
        UpdateUi();
    }

    void Quit()
    {
        refresh.Stop();
        icon.Visible = false;
        icon.Dispose();
        ExitThread();
    }

    // The brand UFO (src/Logo.cs), drawn at the exact tray size for the current DPI so it stays sharp.
    static Icon MakeIcon(bool on)
    {
        int s = Math.Max(16, SystemInformation.SmallIconSize.Width);
        using (Bitmap bmp = Logo.Mark(s, on))
            return Icon.FromHandle(bmp.GetHicon());
    }
}
