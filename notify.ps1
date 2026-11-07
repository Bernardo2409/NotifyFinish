# NotifyFinish - a UFO flies down from the top of the screen, beams down a
# "Claude Finish" banner, then flies back up. Plays a sound too.
# Called by the Stop / Notification hooks. Receives the hook JSON on stdin.
param(
    [int]$Seconds = 6,  # how long the banner stays (click it to send the UFO away earlier)
    [switch]$Force      # show even when alerts are turned off (used by "Test alert")
)

$ErrorActionPreference = 'SilentlyContinue'

# --- Turned off from the tray app (or `notify.sh off`)? ---------------------------
if (-not $Force -and (Test-Path (Join-Path $env:USERPROFILE '.claude\notifyfinish.off'))) { exit 0 }

# --- Read the hook JSON -------------------------------------------------------
$raw = [Console]::In.ReadToEnd()
$data = $null
if ($raw) { $data = $raw | ConvertFrom-Json }

if (-not $data -and $raw) {
    # Malformed JSON (e.g. unescaped backslashes): pull out the fields we need by hand
    $data = [pscustomobject]@{}
    foreach ($f in 'hook_event_name', 'message', 'cwd') {
        if ($raw -match ('"' + $f + '"\s*:\s*"((?:[^"\\]|\\.)*)"')) {
            $data | Add-Member $f ($matches[1] -replace '\\(.)', '$1')
        }
    }
}

$hookEvent = if ($data.hook_event_name) { $data.hook_event_name } else { 'Stop' }
$project   = if ($data.cwd) { Split-Path $data.cwd -Leaf } else { 'Claude Code' }

if ($hookEvent -eq 'Notification') {
    $title = 'Claude needs you'
    $msg   = if ($data.message) { "$project  -  $($data.message)" } else { "$project  -  Waiting for your input" }
    $rgb   = 235, 140, 0     # orange
} else {
    $title = 'Claude Finish'
    $msg   = ''
    $rgb   = 30, 170, 80     # green
}

# --- Sound --------------------------------------------------------------------
[System.Media.SystemSounds]::Exclamation.Play()

# --- The UFO window (C#, compiled once and cached in %TEMP%) -------------------
$src = @'
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;

public class UfoAlert : Form
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
    const float W = 520f;
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

    // ---- Smoke particles ----
    class Puff { public float X, Y, Vx, Vy, R, Grow, Life, MaxLife, Alpha; public int Shade; }
    readonly System.Collections.Generic.List<Puff> puffs = new System.Collections.Generic.List<Puff>();
    readonly Random rng = new Random();
    readonly Bitmap[] smoke = new Bitmap[2];
    double lastT = -1, lastWisp;
    bool landed, tookOff, gone;

    // ---- Fuzzy text (inspired by React Bits' FuzzyText) ----
    const float FUZZ_RANGE = 30f;       // max horizontal jitter (px) at intensity 1
    const float BASE_INTENSITY = 0.18f; // idle vibration
    const float HOVER_INTENSITY = 0.5f; // mouse over the banner
    const double GLITCH_EVERY = 2200, GLITCH_FOR = 180;
    Bitmap titleBmp, subBmp;
    SizeF titleSize, subSize;
    float fuzz = 2f;                    // current intensity (starts high: text "materialises")
    bool hovering;

    public static void Run(string title, string subtitle, int r, int g, int b, int seconds)
    {
        try { SetProcessDPIAware(); } catch { }
        Application.Run(new UfoAlert(title, subtitle, Color.FromArgb(r, g, b), seconds));
    }

    UfoAlert(string title, string subtitle, Color accent, int seconds)
    {
        this.title = title;
        this.subtitle = subtitle ?? "";
        this.accent = accent;
        stayMs = Math.Max(1, seconds) * 1000.0;

        using (Graphics sg = Graphics.FromHwnd(IntPtr.Zero)) scale = sg.DpiX / 96f;

        titleFont = new Font("Segoe UI Black", 30f, FontStyle.Regular, GraphicsUnit.Pixel);
        if (titleFont.Name != "Segoe UI Black")
            titleFont = new Font("Segoe UI", 28f, FontStyle.Bold, GraphicsUnit.Pixel);
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

        // Pre-render the text once; each frame it's redrawn as jittered 1px slices (fuzzy text)
        if (this.subtitle.Length > 0)
        {
            titleBmp = MakeTextBmp(this.title, titleFont, bannerW - 24f, 44f, out titleSize);
            subBmp = MakeTextBmp(this.subtitle, subFont, bannerW - 32f, 24f, out subSize);
        }
        else
        {
            titleBmp = MakeTextBmp(this.title, titleFont, bannerW - 24f, bannerH, out titleSize);
        }

        MouseEnter += delegate { hovering = true; };
        MouseLeave += delegate { hovering = false; };

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        Rectangle sb = Screen.PrimaryScreen.Bounds;
        int pw = (int)Math.Ceiling(W * scale), ph = (int)Math.Ceiling(H * scale);
        Bounds = new Rectangle(sb.X + (sb.Width - pw) / 2, sb.Y, pw, ph);

        smoke[0] = MakeSmoke(Color.FromArgb(246, 246, 250));
        smoke[1] = MakeSmoke(Color.FromArgb(200, 204, 214));

        MouseDown += delegate { SendAway(); };

        timer = new Timer();
        timer.Interval = 15;
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

    float Rnd(float a, float b) { return a + (float)rng.NextDouble() * (b - a); }

    void Emit(float x, float y, float vx, float vy, float r, float grow, float life, float alpha)
    {
        Puff p = new Puff();
        p.X = x; p.Y = y; p.Vx = vx; p.Vy = vy; p.R = r; p.Grow = grow;
        p.Life = life; p.MaxLife = life; p.Alpha = alpha; p.Shade = rng.Next(2);
        puffs.Add(p);
    }

    // A ring of smoke rolling out sideways from under the saucer (landing / lift-off)
    void Burst(float y, int n, float spread, float power, float size)
    {
        float cx = W / 2f;
        for (int i = 0; i < n; i++)
        {
            float dx = Rnd(-spread, spread);
            float dir = dx < 0 ? -1f : 1f;
            Emit(cx + dx * 0.6f, y + Rnd(-3f, 6f),
                 dir * Rnd(0.8f, power) * (0.35f + Math.Abs(dx) / spread),
                 Rnd(-0.45f, 0.55f), Rnd(size * 0.55f, size), Rnd(0.25f, 0.5f),
                 Rnd(750f, 1300f), Rnd(0.55f, 0.8f));
        }
    }

    void Frame()
    {
        double t = (DateTime.Now - start).TotalMilliseconds;
        float step = lastT < 0 ? 1f : (float)Math.Min(4.0, (t - lastT) / 16.0);
        lastT = t;
        if (!leaving && t >= DESC + BEAM + stayMs) SendAway();

        float cx = W / 2f;
        float y, beam;
        bool ascending = false;
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
            else if (u < unbeam + ASC) { y = Lerp(leaveY, ABOVE - 40f, EaseInBack(Clamp01((u - unbeam) / ASC))); beam = 0f; ascending = true; }
            else { y = ABOVE - 40f; beam = 0f; gone = true; }
        }

        // ---- Emit smoke ----
        if (!gone)
        {
            if (!leaving && t < DESC)                       // thruster trail on the way down
                for (int i = 0; i < 2; i++)
                    Emit(cx + Rnd(-48f, 48f), y - 4f + Rnd(-4f, 4f), Rnd(-0.4f, 0.4f), Rnd(-0.7f, -0.1f),
                         Rnd(6f, 11f), 0.25f, Rnd(500f, 800f), 0.45f);

            if (!landed && !leaving && t >= DESC * 0.72)    // landing cloud
            {
                landed = true;
                Burst(y + 14f, 46, 85f, 3.6f, 20f);
            }

            if (!leaving && t >= DESC && t - lastWisp > 110) // idle wisps while hovering
            {
                lastWisp = t;
                Emit(cx + Rnd(-30f, 30f), y + 18f, Rnd(-0.3f, 0.3f), Rnd(0.3f, 0.8f),
                     Rnd(4f, 7f), 0.15f, Rnd(500f, 800f), 0.3f);
            }

            if (ascending)                                  // lift-off: burst + plume
            {
                if (!tookOff) { tookOff = true; Burst(y + 16f, 38, 85f, 3.8f, 18f); }
                if (y > -30f)
                    for (int i = 0; i < 3; i++)
                        Emit(cx + Rnd(-26f, 26f), y + 16f, Rnd(-0.8f, 0.8f), Rnd(1.0f, 2.4f),
                             Rnd(7f, 13f), 0.4f, Rnd(800f, 1300f), 0.55f);
            }
        }

        // ---- Update smoke ----
        float dragX = (float)Math.Pow(0.955, step), dragY = (float)Math.Pow(0.97, step);
        for (int i = puffs.Count - 1; i >= 0; i--)
        {
            Puff p = puffs[i];
            p.Life -= 16f * step;
            if (p.Life <= 0) { puffs.RemoveAt(i); continue; }
            p.X += p.Vx * step; p.Y += p.Vy * step;
            p.Vx *= dragX; p.Vy *= dragY;
            p.R += p.Grow * step;
        }

        if (gone && puffs.Count == 0) { timer.Stop(); Close(); return; }

        // ---- Fuzzy text intensity ----
        float goal;
        if (leaving) goal = 1.6f;                                        // dissolve back into the beam
        else if (t < DESC + BEAM * 0.6) goal = 2f;                       // still materialising
        else if (((t - DESC) % GLITCH_EVERY) > GLITCH_EVERY - GLITCH_FOR) goal = 1f; // glitch burst
        else goal = hovering ? HOVER_INTENSITY : BASE_INTENSITY;
        float rate = goal > fuzz ? 0.45f : 0.07f;                        // fast attack, slow settle
        fuzz += (goal - fuzz) * (1f - (float)Math.Pow(1f - rate, step));

        lastY = y; lastBeam = beam;
        Render(y, beam, t);
    }

    // Text rendered once at device resolution, centred in a box of logical size w x h
    Bitmap MakeTextBmp(string text, Font font, float w, float h, out SizeF logical)
    {
        logical = new SizeF(w, h);
        Bitmap b = new Bitmap(Math.Max(1, (int)Math.Ceiling(w * scale)), Math.Max(1, (int)Math.Ceiling(h * scale)), PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(b))
        using (StringFormat sf = new StringFormat())
        {
            g.Clear(Color.Transparent);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAlias;
            g.ScaleTransform(scale, scale);
            sf.Alignment = StringAlignment.Center;
            sf.LineAlignment = StringAlignment.Center;
            sf.Trimming = StringTrimming.EllipsisCharacter;
            sf.FormatFlags = StringFormatFlags.NoWrap;
            using (SolidBrush shadow = new SolidBrush(Color.FromArgb(70, 0, 0, 0)))
                g.DrawString(text, font, shadow, new RectangleF(0, 1.5f, w, h), sf);
            g.DrawString(text, font, Brushes.White, new RectangleF(0, 0, w, h), sf);
        }
        return b;
    }

    // FuzzyText: every 1px row of the text is drawn with its own random horizontal offset
    void DrawFuzzy(Graphics g, Bitmap bmp, RectangleF dest, float amp, float alpha)
    {
        if (alpha < 0.01f) return;
        ColorMatrix cm = new ColorMatrix();
        cm.Matrix33 = alpha;
        using (ImageAttributes ia = new ImageAttributes())
        {
            ia.SetColorMatrix(cm);
            InterpolationMode im = g.InterpolationMode;
            PixelOffsetMode pm = g.PixelOffsetMode;
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;

            if (amp * scale < 0.5f)
            {
                PointF[] whole = { dest.Location, new PointF(dest.Right, dest.Top), new PointF(dest.Left, dest.Bottom) };
                g.DrawImage(bmp, whole, new RectangleF(0, 0, bmp.Width, bmp.Height), GraphicsUnit.Pixel, ia);
            }
            else
            {
                float rowH = dest.Height / bmp.Height;
                for (int row = 0; row < bmp.Height; row++)
                {
                    float off = (float)Math.Round((rng.NextDouble() - 0.5) * amp * scale) / scale;
                    float ry = dest.Top + row * rowH;
                    PointF[] dst = { new PointF(dest.Left + off, ry), new PointF(dest.Right + off, ry), new PointF(dest.Left + off, ry + rowH) };
                    g.DrawImage(bmp, dst, new RectangleF(0, row, bmp.Width, 1), GraphicsUnit.Pixel, ia);
                }
            }

            g.InterpolationMode = im;
            g.PixelOffsetMode = pm;
        }
    }

    static Bitmap MakeSmoke(Color c)
    {
        Bitmap b = new Bitmap(64, 64, PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(b))
        using (GraphicsPath p = new GraphicsPath())
        {
            g.Clear(Color.Transparent);
            p.AddEllipse(0, 0, 64, 64);
            using (PathGradientBrush pg = new PathGradientBrush(p))
            {
                pg.CenterColor = Color.FromArgb(215, c);
                pg.SurroundColors = new Color[] { Color.FromArgb(0, c) };
                Blend bl = new Blend();
                bl.Factors = new float[] { 0f, 0.55f, 0.88f, 1f };
                bl.Positions = new float[] { 0f, 0.35f, 0.7f, 1f };
                pg.Blend = bl;
                g.FillEllipse(pg, 0, 0, 64, 64);
            }
        }
        return b;
    }

    void DrawSmoke(Graphics g)
    {
        RectangleF src = new RectangleF(0, 0, 64, 64);
        foreach (Puff p in puffs)
        {
            float lifeK = p.Life / p.MaxLife;
            float fadeIn = Math.Min(1f, (p.MaxLife - p.Life) / 90f);
            float a = p.Alpha * fadeIn * (float)Math.Pow(lifeK, 1.3);
            if (a < 0.01f) continue;
            ColorMatrix cm = new ColorMatrix();
            cm.Matrix33 = a;
            using (ImageAttributes ia = new ImageAttributes())
            {
                ia.SetColorMatrix(cm);
                PointF[] dst = { new PointF(p.X - p.R, p.Y - p.R), new PointF(p.X + p.R, p.Y - p.R), new PointF(p.X - p.R, p.Y + p.R) };
                g.DrawImage(smoke[p.Shade], dst, src, GraphicsUnit.Pixel, ia);
            }
        }
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

                    if (subtitle.Length > 0)
                    {
                        DrawFuzzy(g, titleBmp, new RectangleF(r.X + 12, r.Y + 4, titleSize.Width, titleSize.Height), fuzz * FUZZ_RANGE, p);
                        DrawFuzzy(g, subBmp, new RectangleF(r.X + 16, r.Y + 47, subSize.Width, subSize.Height), fuzz * FUZZ_RANGE * 0.3f, p);
                    }
                    else
                    {
                        DrawFuzzy(g, titleBmp, new RectangleF(r.X + 12, r.Y, titleSize.Width, titleSize.Height), fuzz * FUZZ_RANGE, p);
                    }
                    g.Restore(st);
                }

                // ---------- Smoke (behind the saucer, in front of the banner) ----------
                DrawSmoke(g);

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
'@

Add-Type -AssemblyName System.Windows.Forms, System.Drawing

$md5  = [System.Security.Cryptography.MD5]::Create()
$hash = -join ($md5.ComputeHash([Text.Encoding]::UTF8.GetBytes($src))[0..5] | ForEach-Object { $_.ToString('x2') })
$dll  = Join-Path $env:TEMP "NotifyFinishUfo-$hash.dll"

if (-not (Test-Path $dll)) {
    $tmp = Join-Path $env:TEMP "NotifyFinishUfo-$hash-$PID.dll"
    Add-Type -TypeDefinition $src -ReferencedAssemblies System.Windows.Forms, System.Drawing `
             -OutputAssembly $tmp -OutputType Library -ErrorAction Stop
    Move-Item $tmp $dll -Force
}
if (-not ('UfoAlert' -as [type])) { Add-Type -Path $dll -ErrorAction Stop }

[UfoAlert]::Run($title, $msg, $rgb[0], $rgb[1], $rgb[2], $Seconds)
