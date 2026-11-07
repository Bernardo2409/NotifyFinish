using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;

public class UfoClassic : Form
{
    // ---- Win32: per-pixel-alpha layered window ----
    [StructLayout(LayoutKind.Sequential)] struct PT { public int X, Y; public PT(int x, int y) { X = x; Y = y; } }
    [StructLayout(LayoutKind.Sequential)] struct SZ { public int W, H; public SZ(int w, int h) { W = w; H = h; } }
    [StructLayout(LayoutKind.Sequential, Pack = 1)] struct BLEND { public byte Op, Flags, Alpha, Format; }
    [DllImport("user32.dll")] static extern bool UpdateLayeredWindow(IntPtr h, IntPtr dst, ref PT pos, ref SZ size, IntPtr src, ref PT srcPos, int key, ref BLEND blend, int flags);
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr h);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr h, IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();

    // ---- Layout (logical px at 100% scaling) ----
    const float W = 460f;
    const float REST = 56f;        // saucer centre when hovering
    const float ABOVE = -80f;      // off-screen, above the top edge
    const float BANNER_TOP = 156f;

    // ---- Timeline (ms) ----
    const double DESC = 750, BEAM = 380, UNBEAM = 320, ASC = 650;

    readonly string title, subtitle;
    readonly Color accent;
    readonly double stayMs;
    readonly float H, scale, bannerW, bannerH;
    readonly Font titleFont, subFont;
    readonly Timer timer;
    DateTime start;
    bool leaving; double leaveAt; float leaveY, leaveBeam;
    float lastY = ABOVE, lastBeam;

    public static void Run(string title, string subtitle, int r, int g, int b, int seconds)
    {
        try { SetProcessDPIAware(); } catch { }
        Application.Run(Create(title, subtitle, Color.FromArgb(r, g, b), seconds));
    }

    public static Form Create(string title, string subtitle, Color accent, int seconds)
    {
        return new UfoClassic(title, subtitle, accent, seconds);
    }

    UfoClassic(string title, string subtitle, Color accent, int seconds)
    {
        this.title = title;
        this.subtitle = subtitle ?? "";
        this.accent = accent;
        stayMs = Math.Max(1, seconds) * 1000.0;

        using (Graphics sg = Graphics.FromHwnd(IntPtr.Zero)) scale = sg.DpiX / 96f;

        titleFont = new Font("Segoe UI", 26f, FontStyle.Bold, GraphicsUnit.Pixel);
        subFont = new Font("Segoe UI", 14f, FontStyle.Regular, GraphicsUnit.Pixel);

        bannerH = this.subtitle.Length > 0 ? 78f : 58f;
        using (Bitmap tmp = new Bitmap(1, 1))
        using (Graphics mg = Graphics.FromImage(tmp))
        {
            float tw = mg.MeasureString(this.title, titleFont).Width;
            float sw = this.subtitle.Length > 0 ? mg.MeasureString(this.subtitle, subFont).Width : 0f;
            bannerW = Math.Min(W - 24f, Math.Max(260f, Math.Max(tw, sw) + 64f));
        }
        H = BANNER_TOP + bannerH + 16f;

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        Rectangle sb = Screen.PrimaryScreen.Bounds;
        int pw = (int)Math.Ceiling(W * scale), ph = (int)Math.Ceiling(H * scale);
        Bounds = new Rectangle(sb.X + (sb.Width - pw) / 2, sb.Y, pw, ph);

        MouseDown += delegate { SendAway(); };

        timer = new Timer();
        timer.Interval = 33;   // 30 fps
        timer.Tick += delegate { Frame(); };
    }

    protected override bool ShowWithoutActivation { get { return true; } }
    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams cp = base.CreateParams;
            cp.ExStyle |= 0x00080000; // WS_EX_LAYERED
            cp.ExStyle |= 0x08000000; // WS_EX_NOACTIVATE  (never steals focus from your game)
            cp.ExStyle |= 0x00000008; // WS_EX_TOPMOST
            cp.ExStyle |= 0x00000080; // WS_EX_TOOLWINDOW  (no taskbar button)
            return cp;
        }
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        start = DateTime.Now;
        timer.Start();
        Frame();
    }

    void SendAway()
    {
        if (leaving) return;
        leaving = true;
        leaveAt = (DateTime.Now - start).TotalMilliseconds;
        leaveY = lastY;
        leaveBeam = lastBeam;
    }

    // ---- Easing ----
    static float Lerp(float a, float b, double t) { return a + (b - a) * (float)t; }
    static double Clamp01(double t) { return t < 0 ? 0 : (t > 1 ? 1 : t); }
    static double EaseOutBack(double t) { const double c1 = 1.4, c3 = c1 + 1; t -= 1; return 1 + c3 * t * t * t + c1 * t * t; }
    static double EaseInBack(double t) { const double c1 = 1.6, c3 = c1 + 1; return c3 * t * t * t - c1 * t * t; }
    static double EaseOutCubic(double t) { t = 1 - t; return 1 - t * t * t; }
    static double EaseInCubic(double t) { return t * t * t; }

    void Frame()
    {
        double t = (DateTime.Now - start).TotalMilliseconds;
        if (!leaving && t >= DESC + BEAM + stayMs) SendAway();

        float y, beam;
        if (!leaving)
        {
            if (t < DESC) { y = Lerp(ABOVE, REST, EaseOutBack(Clamp01(t / DESC))); beam = 0f; }
            else
            {
                y = REST + (float)Math.Sin((t - DESC) / 1600.0 * 2 * Math.PI) * 3f; // gentle hover
                beam = (float)EaseOutCubic(Clamp01((t - DESC) / BEAM));
            }
        }
        else
        {
            double u = t - leaveAt;
            double unbeam = leaveBeam > 0.01f ? UNBEAM : 0;
            if (u < unbeam) { y = leaveY; beam = leaveBeam * (float)(1 - EaseInCubic(Clamp01(u / unbeam))); }
            else if (u < unbeam + ASC) { y = Lerp(leaveY, ABOVE - 40f, EaseInBack(Clamp01((u - unbeam) / ASC))); beam = 0f; }
            else { timer.Stop(); Close(); return; }
        }
        lastY = y; lastBeam = beam;
        Render(y, beam, t);
    }

    static Color A(Color c, double a) { return Color.FromArgb(Math.Max(0, Math.Min(255, (int)(a))), c.R, c.G, c.B); }
    static Color Mix(Color c, Color d, float k) { return Color.FromArgb(c.A, (int)(c.R + (d.R - c.R) * k), (int)(c.G + (d.G - c.G) * k), (int)(c.B + (d.B - c.B) * k)); }

    static GraphicsPath RoundRect(RectangleF r, float rad)
    {
        GraphicsPath p = new GraphicsPath();
        float d = rad * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    void Render(float y, float beam, double t)
    {
        using (Bitmap bmp = new Bitmap(Width, Height, PixelFormat.Format32bppArgb))
        {
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAlias;
                g.ScaleTransform(scale, scale);
                float cx = W / 2f;

                // ---------- Tractor beam ----------
                if (beam > 0.01f)
                {
                    float top = y + 14f;
                    float fullBottom = BANNER_TOP + bannerH * 0.5f;
                    float bottom = top + (fullBottom - top) * beam;
                    float tw = 20f, bw = 20f + (bannerW / 2f - 26f) * beam;
                    using (GraphicsPath beamPath = new GraphicsPath())
                    {
                        beamPath.AddPolygon(new PointF[] {
                            new PointF(cx - tw, top), new PointF(cx + tw, top),
                            new PointF(cx + bw, bottom), new PointF(cx - bw, bottom) });
                        using (LinearGradientBrush lb = new LinearGradientBrush(
                            new PointF(0, top), new PointF(0, bottom + 1),
                            A(Mix(accent, Color.White, 0.45f), 190 * beam), A(accent, 40 * beam)))
                            g.FillPath(lb, beamPath);

                        GraphicsState st = g.Save();
                        g.SetClip(beamPath);
                        using (Pen scan = new Pen(Color.FromArgb((int)(70 * beam), 255, 255, 255), 2f))
                            for (float ly = top + (float)((t / 22.0) % 12.0); ly < bottom; ly += 12f)
                                g.DrawLine(scan, cx - bw, ly, cx + bw, ly);
                        g.Restore(st);
                    }
                }

                // ---------- Banner (rides the beam) ----------
                if (beam > 0.02f)
                {
                    float p = beam;
                    float bTop = Lerp(y + 6f, BANNER_TOP, p);
                    float s = 0.25f + 0.75f * p;
                    RectangleF r = new RectangleF(cx - bannerW / 2f, bTop, bannerW, bannerH);
                    GraphicsState st = g.Save();
                    g.TranslateTransform(cx, bTop + bannerH / 2f);
                    g.ScaleTransform(s, s);
                    g.TranslateTransform(-cx, -(bTop + bannerH / 2f));

                    using (GraphicsPath shadow = RoundRect(new RectangleF(r.X, r.Y + 5, r.Width, r.Height), 16f))
                    using (SolidBrush sbr = new SolidBrush(Color.FromArgb((int)(70 * p), 0, 0, 0)))
                        g.FillPath(sbr, shadow);
                    using (GraphicsPath box = RoundRect(r, 16f))
                    {
                        using (LinearGradientBrush fill = new LinearGradientBrush(
                            new PointF(0, r.Top), new PointF(0, r.Bottom + 1),
                            A(Mix(accent, Color.White, 0.25f), 250 * p), A(Mix(accent, Color.Black, 0.12f), 250 * p)))
                            g.FillPath(fill, box);
                        using (Pen edge = new Pen(Color.FromArgb((int)(110 * p), 255, 255, 255), 1.5f))
                            g.DrawPath(edge, box);
                    }

                    StringFormat sf = new StringFormat();
                    sf.Alignment = StringAlignment.Center;
                    sf.LineAlignment = StringAlignment.Center;
                    sf.Trimming = StringTrimming.EllipsisCharacter;
                    sf.FormatFlags = StringFormatFlags.NoWrap;
                    using (SolidBrush white = new SolidBrush(Color.FromArgb((int)(255 * p), 255, 255, 255)))
                    {
                        if (subtitle.Length > 0)
                        {
                            g.DrawString(title, titleFont, white, new RectangleF(r.X + 12, r.Y + 6, r.Width - 24, 40), sf);
                            g.DrawString(subtitle, subFont, white, new RectangleF(r.X + 16, r.Y + 46, r.Width - 32, 24), sf);
                        }
                        else
                        {
                            g.DrawString(title, titleFont, white, new RectangleF(r.X + 12, r.Y, r.Width - 24, r.Height), sf);
                        }
                    }
                    g.Restore(st);
                }

                // ---------- UFO ----------
                // under-glow
                using (SolidBrush glow = new SolidBrush(A(accent, 60 + 110 * beam)))
                    g.FillEllipse(glow, cx - 34, y + 8, 68, 16);

                // glass dome (back)
                RectangleF dome = new RectangleF(cx - 34, y - 46, 68, 64);
                using (LinearGradientBrush db = new LinearGradientBrush(dome,
                    Color.FromArgb(150, 200, 240, 255), Color.FromArgb(120, 70, 150, 200), LinearGradientMode.Vertical))
                    g.FillEllipse(db, dome);

                // little alien
                using (SolidBrush alien = new SolidBrush(Color.FromArgb(255, 125, 215, 95)))
                    g.FillEllipse(alien, cx - 12, y - 37, 24, 22);
                using (SolidBrush eye = new SolidBrush(Color.FromArgb(255, 25, 25, 35)))
                {
                    bool blink = (t % 3200) < 140;
                    float eh = blink ? 1.5f : 9f;
                    g.FillEllipse(eye, cx - 8, y - 30 + (9 - eh) / 2, 6, eh);
                    g.FillEllipse(eye, cx + 2, y - 30 + (9 - eh) / 2, 6, eh);
                }

                // glass highlight + rim
                using (SolidBrush hl = new SolidBrush(Color.FromArgb(120, 255, 255, 255)))
                    g.FillEllipse(hl, cx - 24, y - 40, 16, 11);
                using (Pen dp = new Pen(Color.FromArgb(150, 255, 255, 255), 1.5f))
                    g.DrawArc(dp, dome, 180, 180);

                // saucer body
                RectangleF body = new RectangleF(cx - 82, y - 15, 164, 36);
                using (LinearGradientBrush bb = new LinearGradientBrush(body,
                    Color.FromArgb(255, 238, 241, 248), Color.FromArgb(255, 112, 118, 135), LinearGradientMode.Vertical))
                    g.FillEllipse(bb, body);
                using (SolidBrush under = new SolidBrush(Color.FromArgb(255, 80, 86, 102)))
                    g.FillEllipse(under, cx - 64, y + 4, 128, 14);
                using (Pen outline = new Pen(Color.FromArgb(255, 70, 75, 90), 1.2f))
                    g.DrawEllipse(outline, body);

                // running lights
                int phase = (int)(t / 130);
                for (int i = -3; i <= 3; i++)
                {
                    float lx = cx + i * 21f;
                    float ly = y + 1f + (1f - (i * i) / 12f) * 2.5f;
                    bool on = ((phase + i + 30) % 3) == 0;
                    if (on)
                        using (SolidBrush halo = new SolidBrush(Color.FromArgb(110, 255, 225, 90)))
                            g.FillEllipse(halo, lx - 7, ly - 7, 14, 14);
                    using (SolidBrush lb = new SolidBrush(on ? Color.FromArgb(255, 255, 238, 120) : Color.FromArgb(255, 150, 130, 70)))
                        g.FillEllipse(lb, lx - 3.5f, ly - 3.5f, 7, 7);
                }
            }
            Push(bmp);
        }
    }

    void Push(Bitmap bmp)
    {
        IntPtr screen = GetDC(IntPtr.Zero);
        IntPtr mem = CreateCompatibleDC(screen);
        IntPtr hbmp = bmp.GetHbitmap(Color.FromArgb(0));
        IntPtr old = SelectObject(mem, hbmp);
        try
        {
            PT pos = new PT(Left, Top), src = new PT(0, 0);
            SZ size = new SZ(bmp.Width, bmp.Height);
            BLEND bl = new BLEND(); bl.Op = 0; bl.Flags = 0; bl.Alpha = 255; bl.Format = 1; // AC_SRC_ALPHA
            UpdateLayeredWindow(Handle, screen, ref pos, ref size, mem, ref src, 0, ref bl, 2); // ULW_ALPHA
        }
        finally
        {
            SelectObject(mem, old);
            DeleteObject(hbmp);
            DeleteDC(mem);
            ReleaseDC(IntPtr.Zero, screen);
        }
    }
}
