// NotifyFinish tray app (Windows).
// Lives in the system tray as a little traffic light:
//   green = alerts ON, red = alerts OFF. Left-click the icon to toggle.
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

    [STAThread]
    static void Main()
    {
        EnableHighDpi();
        bool created;
        using (Mutex mutex = new Mutex(true, "NotifyFinishTray_SingleInstance", out created))
        {
            if (!created) return; // already running
            Application.EnableVisualStyles();
            Application.Run(new TrayContext());
        }
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
    readonly ToolStripMenuItem simpleItem;
    readonly Icon onIcon;
    readonly Icon offIcon;
    readonly System.Windows.Forms.Timer refresh;

    public TrayContext()
    {
        onIcon = MakeIcon(Color.FromArgb(30, 190, 80));
        offIcon = MakeIcon(Color.FromArgb(220, 50, 50));

        enabledItem = new ToolStripMenuItem("Alerts enabled", null, delegate { Toggle(); });
        startupItem = new ToolStripMenuItem("Start with Windows", null, delegate { ToggleStartup(); });

        ufoItem = new ToolStripMenuItem("UFO (animated, more CPU)", null, delegate { SetStyle("ufo"); });
        simpleItem = new ToolStripMenuItem("Simple (light)", null, delegate { SetStyle("simple"); });
        ToolStripMenuItem styleMenu = new ToolStripMenuItem("Style");
        styleMenu.DropDownItems.Add(ufoItem);
        styleMenu.DropDownItems.Add(simpleItem);

        ContextMenuStrip menu = new ContextMenuStrip();
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
    }

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
        icon.Text = on ? "NotifyFinish: ON (click to turn off)" : "NotifyFinish: OFF (click to turn on)";
        enabledItem.Checked = on;
        startupItem.Checked = IsStartup;
        bool simple = Style == "simple";
        ufoItem.Checked = !simple;
        simpleItem.Checked = simple;
    }

    void Test()
    {
        string script = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "notify.ps1");
        if (!File.Exists(script))
        {
            icon.ShowBalloonTip(3000, "NotifyFinish", "notify.ps1 not found next to the app.", ToolTipIcon.Error);
            return;
        }
        ProcessStartInfo psi = new ProcessStartInfo("powershell.exe",
            "-NoProfile -ExecutionPolicy Bypass -File \"" + script + "\" -Force");
        psi.UseShellExecute = false;
        psi.CreateNoWindow = true;
        psi.RedirectStandardInput = true;
        Process p = Process.Start(psi);
        p.StandardInput.Write("{\"hook_event_name\":\"Stop\"}");
        p.StandardInput.Close();
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

    // Draw a simple traffic-light dot so no .ico file is needed.
    // Drawn at the exact tray size for the current DPI so it stays sharp.
    static Icon MakeIcon(Color color)
    {
        int s = Math.Max(16, SystemInformation.SmallIconSize.Width);
        float k = s / 32f;
        Bitmap bmp = new Bitmap(s, s);
        using (Graphics g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using (SolidBrush back = new SolidBrush(Color.FromArgb(40, 40, 40)))
                g.FillEllipse(back, 1 * k, 1 * k, 30 * k, 30 * k);
            using (SolidBrush fill = new SolidBrush(color))
                g.FillEllipse(fill, 5 * k, 5 * k, 22 * k, 22 * k);
            using (SolidBrush shine = new SolidBrush(Color.FromArgb(90, 255, 255, 255)))
                g.FillEllipse(shine, 9 * k, 7 * k, 10 * k, 7 * k);
        }
        return Icon.FromHandle(bmp.GetHicon());
    }
}
