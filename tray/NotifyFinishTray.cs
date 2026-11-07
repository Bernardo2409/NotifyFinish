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
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

static class Program
{
    [STAThread]
    static void Main()
    {
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

    readonly NotifyIcon icon;
    readonly ToolStripMenuItem enabledItem;
    readonly ToolStripMenuItem startupItem;
    readonly Icon onIcon;
    readonly Icon offIcon;
    readonly System.Windows.Forms.Timer refresh;

    public TrayContext()
    {
        onIcon = MakeIcon(Color.FromArgb(30, 190, 80));
        offIcon = MakeIcon(Color.FromArgb(220, 50, 50));

        enabledItem = new ToolStripMenuItem("Alerts enabled", null, delegate { Toggle(); });
        startupItem = new ToolStripMenuItem("Start with Windows", null, delegate { ToggleStartup(); });

        ContextMenuStrip menu = new ContextMenuStrip();
        menu.Items.Add(enabledItem);
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

    // Draw a simple traffic-light dot so no .ico file is needed
    static Icon MakeIcon(Color color)
    {
        Bitmap bmp = new Bitmap(32, 32);
        using (Graphics g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using (SolidBrush back = new SolidBrush(Color.FromArgb(40, 40, 40)))
                g.FillEllipse(back, 1, 1, 30, 30);
            using (SolidBrush fill = new SolidBrush(color))
                g.FillEllipse(fill, 5, 5, 22, 22);
            using (SolidBrush shine = new SolidBrush(Color.FromArgb(90, 255, 255, 255)))
                g.FillEllipse(shine, 9, 7, 10, 7);
        }
        return Icon.FromHandle(bmp.GetHicon());
    }
}
