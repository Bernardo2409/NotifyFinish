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
    $msg   = if ($data.message) { $data.message } else { 'Waiting for your input' }
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
    readonly float H, scale, bannerW, bannerH, discW, discH;
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

    // ---- Old-TV text: starts as a fuzzy, RGB-split, scanlined signal and tunes in to sharp ----
    const float FUZZ_RANGE = 30f;       // max horizontal jitter (px) at intensity 1
    const float TV_START = 1.6f;        // intensity when the banner arrives
    const double TUNE_IN_MS = 1400;     // how long it takes to become sharp
    const float HOVER_INTENSITY = 0.3f; // a little static while the mouse is over it
    Bitmap titleBmp, subBmp, titleRed, titleCyan;
    SizeF titleSize, subSize;
    float fuzz = TV_START, hoverFuzz;
    bool hovering;

    // ---- Spiral vortex beam (inspired by React Bits Pro's Spiral Funnel) ----
    const int STRANDS = 14, SEGS = 36;
    const double TWIST = 1.7 * Math.PI;  // how tightly the strands wind
    const double SPIN = 0.0021;          // radians per ms
    readonly Pen strandPen = new Pen(Color.White, 1f), glowPen = new Pen(Color.White, 4f);

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

        // Condensed, technical display face (ships with Windows 10/11); falls back gracefully
        titleFont = PickFont(34f, "Bahnschrift SemiBold Condensed", "Bahnschrift Condensed", "Segoe UI Black");
        subFont = PickFont(14f, "Bahnschrift SemiCondensed", "Bahnschrift", "Segoe UI");
        this.title = title.ToUpperInvariant();   // cinematic caps (letter-spacing applied when laid out)

        bannerH = this.subtitle.Length > 0 ? 72f : 54f;
        using (Bitmap tmp = new Bitmap(1, 1))
        using (Graphics mg = Graphics.FromImage(tmp))
        {
            float tw = TextWidth(this.title, titleFont, TITLE_TRACKING);
            float sw = TextWidth(this.subtitle, subFont, SUB_TRACKING);
            bannerW = Math.Min(380f, Math.Max(200f, Math.Max(tw, sw) + 36f));
        }
        discW = Math.Min(W - 16f, bannerW * 1.22f);
        discH = bannerH * 1.75f + 6f;
        H = BANNER_TOP + bannerH / 2f + discH * 0.5f * 1.35f + 8f;

        // Pre-render the text once; each frame it's redrawn as jittered 1px slices (fuzzy text)
        float titleH = this.subtitle.Length > 0 ? 46f : bannerH;
        titleBmp  = MakeTextBmp(this.title, titleFont, bannerW - 24f, titleH, Color.FromArgb(16, 18, 24), true, out titleSize);
        titleRed  = MakeTextBmp(this.title, titleFont, bannerW - 24f, titleH, Color.FromArgb(255, 50, 90), false, out titleSize);
        titleCyan = MakeTextBmp(this.title, titleFont, bannerW - 24f, titleH, Color.FromArgb(40, 225, 255), false, out titleSize);
        if (this.subtitle.Length > 0)
            subBmp = MakeTextBmp(this.subtitle, subFont, bannerW - 32f, 20f, Color.FromArgb(28, 30, 38), false, out subSize);
        strandPen.StartCap = strandPen.EndCap = LineCap.Round;
        glowPen.StartCap = glowPen.EndCap = LineCap.Round;

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
    static double EaseInOutCubic(double t) { return t < 0.5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2; }
    static RectangleF Off(RectangleF r, float dx) { return new RectangleF(r.X + dx, r.Y, r.Width, r.Height); }

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

        // ---- Old-TV intensity: tune in -> sharp -> lose signal on the way out ----
        double tuneT = t - (DESC + BEAM * 0.35);
        float intro = tuneT <= 0 ? TV_START : TV_START * (float)(1 - EaseInOutCubic(Clamp01(tuneT / TUNE_IN_MS)));
        float outro = 0f;
        if (leaving) outro = TV_START * (float)EaseInCubic(Clamp01((t - leaveAt) / UNBEAM));
        float hoverGoal = hovering && !leaving ? HOVER_INTENSITY : 0f;
        hoverFuzz += (hoverGoal - hoverFuzz) * (1f - (float)Math.Pow(0.85, step));
        fuzz = Math.Max(intro, Math.Max(outro, hoverFuzz));

        lastY = y; lastBeam = beam;
        Render(y, beam, t);
    }

    // A point on the funnel: s = 0 (UFO) .. 1 (banner), theta = angle around the axis.
    // Returns the 2D point and sin(theta) (>0 = front, <0 = back).
    static PointF Funnel(float cx, float top, float len, float rTop, float rBot, float s, double theta, out float depth)
    {
        float R = rTop + (rBot - rTop) * (float)Math.Pow(s, 1.45);
        depth = (float)Math.Sin(theta);
        return new PointF(cx + R * (float)Math.Cos(theta), top + s * len + depth * R * 0.34f);
    }

    void DrawVortex(Graphics g, float cx, float top, float bottom, float rTop, float rBot, float beam, double t)
    {
        float len = bottom - top;
        if (len < 2f) return;
        // Widen as the beam extends so the funnel "opens up"
        rBot = rTop + (rBot - rTop) * beam;
        Color crown = Mix(accent, Color.White, 0.75f);   // near the UFO
        Color baseC = Mix(accent, Color.White, 0.15f);   // near the banner
        double spin = t * SPIN;

        // Rim glow where the funnel leaves the saucer
        using (SolidBrush rim = new SolidBrush(A(crown, 120 * beam)))
            g.FillEllipse(rim, cx - rTop * 1.7f, top - 3f, rTop * 3.4f, 7f);

        // Strands: back half first (dim, thin), then front half (bright, thick) for a 3D look
        for (int pass = 0; pass < 2; pass++)
            for (int i = 0; i < STRANDS; i++)
            {
                double ph = i * 2 * Math.PI / STRANDS;
                float d0;
                PointF prev = Funnel(cx, top, len, rTop, rBot, 0f, ph + spin, out d0);
                for (int k = 1; k <= SEGS; k++)
                {
                    float s = k / (float)SEGS;
                    float d;
                    PointF pt = Funnel(cx, top, len, rTop, rBot, s, ph + spin + s * TWIST, out d);
                    float dm = (d + d0) * 0.5f;
                    if ((dm >= 0) == (pass == 1))
                    {
                        float near = 0.5f + 0.5f * dm;                         // 0 back .. 1 front
                        float fade = (float)Math.Min(1.0, s * 6.0) * (1f - s * 0.35f);
                        float a = beam * fade * (0.15f + 0.85f * near * near);
                        Color c = Mix(crown, baseC, s);
                        glowPen.Color = A(c, 255 * a * 0.32f);
                        glowPen.Width = 3f + 6f * near;
                        g.DrawLine(glowPen, prev, pt);
                        strandPen.Color = A(c, 255 * a);
                        strandPen.Width = 0.7f + 2.3f * near;
                        g.DrawLine(strandPen, prev, pt);
                    }
                    prev = pt; d0 = d;
                }
            }

        // Light pulses travelling down the strands
        for (int i = 0; i < STRANDS; i += 2)
        {
            double sp = (t * 0.00055 + i * 0.618) % 1.0;
            float s = (float)sp, d;
            PointF pt = Funnel(cx, top, len, rTop, rBot, s, i * 2 * Math.PI / STRANDS + spin + s * TWIST, out d);
            float near = 0.5f + 0.5f * d;
            float a = beam * (0.25f + 0.75f * near) * (float)Math.Sin(Math.PI * s);
            float rr = 1.6f + 2.2f * near;
            using (SolidBrush halo = new SolidBrush(A(Color.White, 90 * a)))
                g.FillEllipse(halo, pt.X - rr * 2.2f, pt.Y - rr * 2.2f, rr * 4.4f, rr * 4.4f);
            using (SolidBrush core = new SolidBrush(A(Color.White, 255 * a)))
                g.FillEllipse(core, pt.X - rr * 0.6f, pt.Y - rr * 0.6f, rr * 1.2f, rr * 1.2f);
        }

        // Dust specks swirling with the flow
        for (int j = 0; j < 70; j++)
        {
            double sp = (j * 0.1373 + t * 0.00028 * (0.7 + (j % 5) * 0.12)) % 1.0;
            float s = (float)sp, d;
            float spreadK = 0.55f + ((j * 37) % 50) / 100f;            // inside / outside the strands
            PointF pt = Funnel(cx, top, len, rTop * spreadK, rBot * spreadK, s, j * 2.399 + spin * 1.25 + s * TWIST, out d);
            float a = beam * (0.2f + 0.6f * (0.5f + 0.5f * d)) * (float)Math.Sin(Math.PI * s);
            using (SolidBrush dot = new SolidBrush(A(crown, 255 * a)))
                g.FillEllipse(dot, pt.X - 0.9f, pt.Y - 0.9f, 1.8f, 1.8f);
        }
    }

    // Soft radial light: colour c at the centre fading to transparent at the ellipse edge
    static void FillGlow(Graphics g, RectangleF r, Color c, float[] factors, float[] positions)
    {
        if (c.A == 0 || r.Width < 1 || r.Height < 1) return;
        using (GraphicsPath p = new GraphicsPath())
        {
            p.AddEllipse(r);
            using (PathGradientBrush pg = new PathGradientBrush(p))
            {
                pg.CenterColor = c;
                pg.SurroundColors = new Color[] { Color.FromArgb(0, c) };
                Blend bl = new Blend();
                bl.Factors = factors;
                bl.Positions = positions;
                pg.Blend = bl;
                g.FillPath(pg, p);
            }
        }
    }

    // Clouds/haze lit by the projector: slowly drifting darker puffs inside the light disc
    void DrawHaze(Graphics g, RectangleF disc, float p, double t)
    {
        RectangleF src = new RectangleF(0, 0, 64, 64);
        float cxd = disc.X + disc.Width / 2f, cyd = disc.Y + disc.Height / 2f;
        for (int k = 0; k < 11; k++)
        {
            float px = cxd + ((k % 4) - 1.5f) * disc.Width * 0.26f + (float)Math.Sin(t * 0.00035 + k * 1.7) * 10f;
            float py = cyd + ((k / 4) - 1f) * disc.Height * 0.3f + (float)Math.Cos(t * 0.00028 + k) * 4f;
            float rr = disc.Height * (0.32f + (k % 3) * 0.08f);
            ColorMatrix cm = new ColorMatrix();
            float tint = 0.62f;
            cm.Matrix00 = Mix(accent, Color.Black, 0.35f).R / 255f * tint;
            cm.Matrix11 = Mix(accent, Color.Black, 0.35f).G / 255f * tint;
            cm.Matrix22 = Mix(accent, Color.Black, 0.35f).B / 255f * tint;
            cm.Matrix33 = 0.2f * p;
            using (ImageAttributes ia = new ImageAttributes())
            {
                ia.SetColorMatrix(cm);
                PointF[] dst = { new PointF(px - rr, py - rr), new PointF(px + rr, py - rr), new PointF(px - rr, py + rr) };
                g.DrawImage(smoke[k % 2], dst, src, GraphicsUnit.Pixel, ia);
            }
        }
    }

    // Text rendered once at device resolution, centred in a box of logical size w x h
    static Font PickFont(float px, params string[] names)
    {
        foreach (string n in names)
        {
            Font f = new Font(n, px, FontStyle.Regular, GraphicsUnit.Pixel);
            if (string.Equals(f.Name, n, StringComparison.OrdinalIgnoreCase)) return f;
            f.Dispose();
        }
        return new Font(FontFamily.GenericSansSerif, px, FontStyle.Bold, GraphicsUnit.Pixel);
    }

    const float TITLE_TRACKING = 2.5f;   // extra space between title letters (px)
    const float SUB_TRACKING = 0.3f;

    // Lays the text out glyph by glyph so we control letter-spacing exactly
    static GraphicsPath BuildTextPath(string text, Font font, float tracking)
    {
        GraphicsPath p = new GraphicsPath();
        StringFormat sf = StringFormat.GenericTypographic;
        using (Bitmap tmp = new Bitmap(1, 1))
        using (Graphics mg = Graphics.FromImage(tmp))
        {
            float x = 0f;
            foreach (char ch in text)
            {
                if (ch == ' ') { x += font.Size * 0.3f + tracking; continue; }
                string sc = ch.ToString();
                p.AddString(sc, font.FontFamily, (int)font.Style, font.Size, new PointF(x, 0f), sf);
                x += mg.MeasureString(sc, font, PointF.Empty, sf).Width + tracking;
            }
        }
        return p;
    }

    static float TextWidth(string text, Font font, float tracking)
    {
        if (string.IsNullOrEmpty(text)) return 0f;
        using (GraphicsPath p = BuildTextPath(text, font, tracking)) return p.GetBounds().Width;
    }

    const int EXTRUDE = 4;   // depth of the 3D letters (px)

    // Text rendered once at device resolution, centred in a box of logical size w x h.
    // threeD = extruded letters (accent-tinted sides, metallic face, lit top edge, soft shadow).
    Bitmap MakeTextBmp(string text, Font font, float w, float h, Color color, bool threeD, out SizeF logical)
    {
        logical = new SizeF(w, h);
        Bitmap b = new Bitmap(Math.Max(1, (int)Math.Ceiling(w * scale)), Math.Max(1, (int)Math.Ceiling(h * scale)), PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(b))
        using (GraphicsPath path = BuildTextPath(text, font, font == titleFont ? TITLE_TRACKING : SUB_TRACKING))
        {
            g.Clear(Color.Transparent);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.ScaleTransform(scale, scale);

            // Centre (and shrink to fit if needed), leaving room for the extrusion
            float depth = threeD ? EXTRUDE * 1.1f + 3f : 0f;
            RectangleF tb = path.GetBounds();
            float k = Math.Min(1f, (w - depth - 4f) / Math.Max(1f, tb.Width));
            using (Matrix m = new Matrix())
            {
                m.Translate(-tb.X, -tb.Y, MatrixOrder.Append);
                m.Scale(k, k, MatrixOrder.Append);
                m.Translate((w - tb.Width * k - depth * 0.6f) / 2f, (h - tb.Height * k - depth) / 2f, MatrixOrder.Append);
                path.Transform(m);
            }

            if (!threeD)
            {
                using (SolidBrush fill = new SolidBrush(color)) g.FillPath(fill, path);
                return b;
            }

            // soft cast shadow
            using (SolidBrush sh = new SolidBrush(Color.FromArgb(45, 0, 0, 0)))
                for (int i = 0; i < 3; i++)
                {
                    GraphicsState s0 = g.Save();
                    g.TranslateTransform(EXTRUDE * 0.7f + i, EXTRUDE * 1.1f + i);
                    g.FillPath(sh, path);
                    g.Restore(s0);
                }

            // extrusion: stacked copies, darkest at the back
            Color near = Mix(accent, Color.Black, 0.35f), far = Mix(accent, Color.Black, 0.7f);
            for (int i = EXTRUDE; i >= 1; i--)
            {
                GraphicsState s1 = g.Save();
                g.TranslateTransform(i * 0.55f, i * 0.9f);
                using (SolidBrush side = new SolidBrush(Mix(near, far, i / (float)EXTRUDE)))
                    g.FillPath(side, path);
                g.Restore(s1);
            }

            // metallic face
            RectangleF bounds = path.GetBounds();
            using (LinearGradientBrush face = new LinearGradientBrush(
                new PointF(0, bounds.Top - 1), new PointF(0, bounds.Bottom + 1),
                Color.FromArgb(255, 78, 84, 100), color))
                g.FillPath(face, path);

            // lit top edge (bevel highlight)
            using (LinearGradientBrush hl = new LinearGradientBrush(
                new PointF(0, bounds.Top - 1), new PointF(0, bounds.Bottom + 1),
                Color.FromArgb(170, 255, 255, 255), Color.FromArgb(0, 255, 255, 255)))
            using (Pen edge = new Pen(hl, 1f))
                g.DrawPath(edge, path);
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
                        // faint volume behind the strands
                        using (LinearGradientBrush lb = new LinearGradientBrush(
                            new PointF(0, top), new PointF(0, bottom + 1),
                            A(Mix(accent, Color.White, 0.45f), 45 * beam), A(accent, 10 * beam)))
                            g.FillPath(lb, beamPath);
                    }
                    DrawVortex(g, cx, top, bottom, 15f, bw - 6f, beam, t);
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

                    // Projection: a Bat-Signal-style disc of light on the "sky", with lit haze,
                    // and the message as a dark silhouette inside it
                    float dcy = bTop + bannerH / 2f;
                    RectangleF disc = new RectangleF(cx - discW / 2f, dcy - discH / 2f, discW, discH);
                    Color light = Mix(accent, Color.White, 0.72f);
                    RectangleF halo = RectangleF.Inflate(disc, discW * 0.17f, discH * 0.17f);
                    FillGlow(g, halo, A(light, 75 * p), new float[] { 0f, 0.6f, 1f }, new float[] { 0f, 0.5f, 1f });
                    FillGlow(g, disc, A(light, 245 * p), new float[] { 0f, 0.72f, 0.95f, 1f }, new float[] { 0f, 0.08f, 0.35f, 1f });
                    using (GraphicsPath hazeClip = new GraphicsPath())
                    {
                        hazeClip.AddEllipse(disc);
                        GraphicsState hs = g.Save();
                        g.SetClip(hazeClip, CombineMode.Intersect);
                        DrawHaze(g, disc, p, t);
                        g.Restore(hs);
                    }
                    using (Pen rimPen = new Pen(A(light, 110 * p), 2f))
                        g.DrawEllipse(rimPen, RectangleF.Inflate(disc, -discW * 0.025f, -discH * 0.025f));

                    RectangleF tr = subtitle.Length > 0
                        ? new RectangleF(r.X + 12, r.Y + 2, titleSize.Width, titleSize.Height)
                        : new RectangleF(r.X + 12, r.Y, titleSize.Width, titleSize.Height);
                    float tv = Math.Min(1f, fuzz);                       // 0 = clean signal, 1 = full old TV
                    float flicker = tv > 0.04f ? 1f - tv * 0.4f * (float)rng.NextDouble() : 1f;
                    float amp = fuzz * FUZZ_RANGE;

                    // RGB split ghosts (chromatic aberration of a badly tuned CRT)
                    if (tv > 0.03f)
                    {
                        float split = 1.5f + tv * 6f;
                        DrawFuzzy(g, titleRed, Off(tr, -split), amp, p * tv * 0.9f);
                        DrawFuzzy(g, titleCyan, Off(tr, split), amp, p * tv * 0.9f);
                    }
                    DrawFuzzy(g, titleBmp, tr, amp, p * flicker);
                    if (subtitle.Length > 0)
                        DrawFuzzy(g, subBmp, new RectangleF(r.X + 16, r.Y + 49, subSize.Width, subSize.Height), amp * 0.3f, p * flicker);

                    // Scanlines + a rolling bright bar while the signal is bad
                    if (tv > 0.03f)
                    {
                        using (GraphicsPath clip = new GraphicsPath())
                        {
                            clip.AddEllipse(disc);
                            GraphicsState cs = g.Save();
                            g.SetClip(clip, CombineMode.Intersect);
                            using (Pen sl = new Pen(Color.FromArgb((int)(70 * tv * p), 0, 0, 0), 1f))
                                for (float sy = disc.Top + (float)((t / 45.0) % 3.0); sy < disc.Bottom; sy += 3f)
                                    g.DrawLine(sl, disc.Left, sy, disc.Right, sy);
                            float bar = disc.Top - 14f + (float)((t / 5.0) % (disc.Height + 28f));
                            using (LinearGradientBrush lb = new LinearGradientBrush(new PointF(0, bar - 13), new PointF(0, bar + 13),
                                Color.FromArgb(0, 255, 255, 255), Color.FromArgb((int)(90 * tv * p), 255, 255, 255)))
                            {
                                lb.SetBlendTriangularShape(0.5f);
                                g.FillRectangle(lb, disc.Left, bar - 12, disc.Width, 24);
                            }
                            g.Restore(cs);
                        }
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
