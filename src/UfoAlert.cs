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

    // ---- Old-TV text: starts as a fuzzy, RGB-split, scanlined signal and tunes in to sharp ----
    const float FUZZ_RANGE = 30f;       // max horizontal jitter (px) at intensity 1
    const float TV_START = 1.6f;        // intensity when the banner arrives
    const double TUNE_IN_MS = 1400;     // how long it takes to become sharp
    const float HOVER_INTENSITY = 0.3f; // a little static while the mouse is over it
    Bitmap subBmp;
    Bitmap[] glyphs, glyphsRed, glyphsCyan;
    float[] glyphAdv;
    SizeF[] glyphSize;
    float totalArc;

    // ---- Title orbiting the spiral ----
    const float RING_S = 0.72f;                // height on the cone (0 = UFO, 1 = bottom)
    const float RING_OUT = 1.3f;               // ring radius vs cone radius (>1 = orbits outside the strands)
    const float FUNNEL_R = 115f;               // radius of the cone where it ends
    const float FUNNEL_END = BANNER_TOP + 24f; // y where the spiral ends
    const double ORBIT = 2 * Math.PI / 36000;  // one full turn every 36 s (very slow)
    const double START_IN = 0.5;               // first letter starts this far (radians) past the left edge
    const double REVEAL_STAGGER = 70;          // ms between letters appearing
    const double REVEAL_FADE = 260;            // ms for each letter to fade in
    SizeF subSize;
    float fuzz = TV_START, hoverFuzz;
    bool hovering;

    // ---- Spiral vortex beam (inspired by React Bits Pro's Spiral Funnel) ----
    const int STRANDS = 14, SEGS = 22;
    const double TWIST = 1.7 * Math.PI;  // how tightly the strands wind
    const double SPIN = 0.0021;          // radians per ms
    readonly Pen strandPen = new Pen(Color.White, 1f), glowPen = new Pen(Color.White, 4f);

    // Space Grotesk ships in ./fonts (SIL OFL) and is loaded privately - nothing to install
    static System.Drawing.Text.PrivateFontCollection fontPack;
    static string fontDir;

    static Font SpaceGrotesk(float px, bool bold)
    {
        try
        {
            if (fontPack == null)
            {
                if (string.IsNullOrEmpty(fontDir)) return null;
                fontPack = new System.Drawing.Text.PrivateFontCollection();
                foreach (string f in new string[] { "SpaceGrotesk-Bold.ttf", "SpaceGrotesk-Regular.ttf" })
                {
                    string path = System.IO.Path.Combine(fontDir, f);
                    if (System.IO.File.Exists(path)) fontPack.AddFontFile(path);
                }
            }
            if (fontPack.Families.Length == 0) return null;
            FontFamily fam = fontPack.Families[0];
            FontStyle st = bold && fam.IsStyleAvailable(FontStyle.Bold) ? FontStyle.Bold
                         : (fam.IsStyleAvailable(FontStyle.Regular) ? FontStyle.Regular : FontStyle.Bold);
            return new Font(fam, px, st, GraphicsUnit.Pixel);
        }
        catch { return null; }
    }

    public static void Run(string title, string subtitle, int r, int g, int b, int seconds, string fonts)
    {
        try { SetProcessDPIAware(); } catch { }
        Application.Run(Create(title, subtitle, Color.FromArgb(r, g, b), seconds, fonts));
    }

    // For hosts that already run a message loop (the tray app): just Show() the result
    public static Form Create(string title, string subtitle, Color accent, int seconds, string fonts)
    {
        fontDir = fonts;
        return new UfoAlert(title, subtitle, accent, seconds);
    }

    UfoAlert(string title, string subtitle, Color accent, int seconds)
    {
        this.title = title;
        this.subtitle = subtitle ?? "";
        this.accent = accent;
        stayMs = Math.Max(1, seconds) * 1000.0;

        using (Graphics sg = Graphics.FromHwnd(IntPtr.Zero)) scale = sg.DpiX / 96f;

        // Condensed, technical display face (ships with Windows 10/11); falls back gracefully
        // .ufo-title: Space Grotesk 700, 1.25rem (20px), uppercase, letter-spacing .05em
        // .ufo-subtitle: 0.875rem (14px)
        titleFont = SpaceGrotesk(20f, true) ?? PickFont(20f, "Bahnschrift SemiBold", "Segoe UI Semibold", "Segoe UI");
        subFont = SpaceGrotesk(14f, false) ?? PickFont(14f, "Bahnschrift", "Segoe UI");
        this.title = title.ToUpperInvariant();

        bannerH = this.subtitle.Length > 0 ? 52f : 34f;
        using (Bitmap tmp = new Bitmap(1, 1))
        using (Graphics mg = Graphics.FromImage(tmp))
        {
            float tw = TextWidth(this.title, titleFont, TITLE_TRACKING);
            float sw = TextWidth(this.subtitle, subFont, SUB_TRACKING);
            // funnel width (max-width: 280px); a bit wider minimum so the orbiting title has room
            bannerW = Math.Min(280f, Math.Max(240f, Math.Max(tw, sw) + 24f));
        }
        H = FUNNEL_END + FUNNEL_R * 0.34f + (this.subtitle.Length > 0 ? 40f : 22f);

        // Pre-render each title letter once (white + glow, plus red/cyan for the TV ghosts).
        // Every frame they're placed around the spiral and redrawn as jittered 1px slices.
        int n = this.title.Length;
        glyphs = new Bitmap[n]; glyphsRed = new Bitmap[n]; glyphsCyan = new Bitmap[n];
        glyphAdv = new float[n]; glyphSize = new SizeF[n];
        totalArc = 0f;
        for (int i = 0; i < n; i++)
        {
            char ch = this.title[i];
            if (ch == ' ') { glyphAdv[i] = titleFont.Size * 0.3f + TITLE_TRACKING; totalArc += glyphAdv[i]; continue; }
            string sc = ch.ToString();
            glyphAdv[i] = TextAdvance(sc, titleFont) + TITLE_TRACKING;
            totalArc += glyphAdv[i];
            float boxW = glyphAdv[i] + 8f;
            glyphs[i]     = MakeTextBmp(sc, titleFont, boxW, 26f, Color.FromArgb(225, 236, 240, 246), true, out glyphSize[i]);
            glyphsRed[i]  = MakeTextBmp(sc, titleFont, boxW, 26f, Color.FromArgb(255, 50, 90), false, out glyphSize[i]);
            glyphsCyan[i] = MakeTextBmp(sc, titleFont, boxW, 26f, Color.FromArgb(40, 225, 255), false, out glyphSize[i]);
        }
        if (this.subtitle.Length > 0)
            subBmp = MakeTextBmp(this.subtitle, subFont, bannerW - 32f, 18f, Color.FromArgb(217, 255, 255, 255), false, out subSize);
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
        BuildSmokeLevels();

        MouseDown += delegate { SendAway(); };

        timer = new Timer();
        timer.Interval = 33;   // ~30 fps is plenty for this and halves the CPU
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
                for (int i = 0; i < 1; i++)
                    Emit(cx + Rnd(-48f, 48f), y - 4f + Rnd(-4f, 4f), Rnd(-0.4f, 0.4f), Rnd(-0.7f, -0.1f),
                         Rnd(6f, 11f), 0.25f, Rnd(500f, 800f), 0.45f);

            if (!landed && !leaving && t >= DESC * 0.72)    // landing cloud
            {
                landed = true;
                Burst(y + 14f, 24, 85f, 3.6f, 26f);
            }

            if (!leaving && t >= DESC && t - lastWisp > 110) // idle wisps while hovering
            {
                lastWisp = t;
                Emit(cx + Rnd(-30f, 30f), y + 18f, Rnd(-0.3f, 0.3f), Rnd(0.3f, 0.8f),
                     Rnd(4f, 7f), 0.15f, Rnd(500f, 800f), 0.3f);
            }

            if (ascending)                                  // lift-off: burst + plume
            {
                if (!tookOff) { tookOff = true; Burst(y + 16f, 22, 85f, 3.8f, 24f); }
                if (y > -30f)
                    for (int i = 0; i < 2; i++)
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
        float R = rTop + (rBot - rTop) * s;                     // straight cone
        depth = (float)Math.Sin(theta);
        return new PointF(cx + R * (float)Math.Cos(theta), top + s * len + depth * R * 0.34f);
    }

    // The title wrapped around the funnel like text on a turning cylinder.
    // front = draw only the letters on the near side (after the strands) or the far side (before).
    void DrawOrbitTitle(Graphics g, float cx, float top, float bottom, float rTop, float rBotIn, float beam, double t,
                        bool front, float tv, float amp, float flicker)
    {
        float len = bottom - top;
        if (len < 2f || glyphs == null) return;
        float rBot = rTop + (rBotIn - rTop) * beam;            // same widening as DrawVortex
        float R = (rTop + (rBot - rTop) * RING_S) * RING_OUT;  // a little outside the cone
        if (R < 10f) return;

        // Start centred at the front, then turn slowly
        // the first letter starts just past the left edge of the spiral (angle PI = left edge,
        // PI/2 = front centre) when the beam has fully opened, then everything drifts right slowly
        double theta0 = Math.PI - START_IN + glyphAdv[0] / (2.0 * R) - (t - (DESC + BEAM)) * ORBIT;
        double arc = 0;
        for (int i = 0; i < glyphs.Length; i++)
        {
            double mid = arc + glyphAdv[i] / 2.0;
            arc += glyphAdv[i];
            if (glyphs[i] == null) continue;

            double th = theta0 - mid / R;
            float d = (float)Math.Sin(th);
            PointF pt = new PointF(cx + R * (float)Math.Cos(th), top + RING_S * len + d * R * 0.34f);
            if ((d >= 0) != front) continue;
            if (Math.Abs(d) < 0.05f) continue;                  // edge-on: too thin to see

            float near = 0.5f + 0.5f * d;                       // 0 back .. 1 front
            float persp = 0.85f + 0.15f * d;
            // letters fade in one by one, left to right
            float reveal = (float)Clamp01((t - (DESC + BEAM * 0.35) - i * REVEAL_STAGGER) / REVEAL_FADE);
            float a = beam * reveal * (0.2f + 0.8f * near * near);
            if (a < 0.01f) continue;
            float gw = glyphSize[i].Width * persp, gh = glyphSize[i].Height * persp;
            // width * d: foreshortened at the sides, mirrored on the far side
            RectangleF dest = new RectangleF(pt.X - gw * d / 2f, pt.Y - gh / 2f, gw * d, gh);

            if (tv > 0.03f)
            {
                float split = 1.5f + tv * 6f;
                DrawFuzzy(g, glyphsRed[i], Off(dest, -split), amp, a * tv * 0.9f);
                DrawFuzzy(g, glyphsCyan[i], Off(dest, split), amp, a * tv * 0.9f);
            }
            DrawFuzzy(g, glyphs[i], dest, amp, a * flicker);
        }
    }

    static float TextAdvance(string s, Font font)
    {
        using (Bitmap tmp = new Bitmap(1, 1))
        using (Graphics mg = Graphics.FromImage(tmp))
            return mg.MeasureString(s, font, PointF.Empty, StringFormat.GenericTypographic).Width;
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
        {
            // far side is dim and thin: draw it the fast way; near side gets full anti-aliasing
            g.SmoothingMode = pass == 0 ? SmoothingMode.HighSpeed : SmoothingMode.AntiAlias;
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
                        if (pass == 1 && near > 0.55f) {   // soft glow only where it shows: the near side
                        glowPen.Color = A(c, 255 * a * 0.32f);
                        glowPen.Width = 3f + 6f * near;
                        g.DrawLine(glowPen, prev, pt); }
                        strandPen.Color = A(c, 255 * a);
                        strandPen.Width = 0.7f + 2.3f * near;
                        g.DrawLine(strandPen, prev, pt);
                    }
                    prev = pt; d0 = d;
                }
            }
        }
        g.SmoothingMode = SmoothingMode.AntiAlias;

        // Light pulses travelling down the strands
        for (int i = 0; i < STRANDS; i += 2)
        {
            double sp = (t * 0.00055 + i * 0.618) % 1.0;
            float s = (float)sp, d;
            PointF pt = Funnel(cx, top, len, rTop, rBot, s, i * 2 * Math.PI / STRANDS + spin + s * TWIST, out d);
            float near = 0.5f + 0.5f * d;
            float a = beam * (0.25f + 0.75f * near) * (float)Math.Sin(Math.PI * s);
            float rr = 1.6f + 2.2f * near;
            {
                g.FillEllipse(Brush(A(Color.White, 90 * a)), pt.X - rr * 2.2f, pt.Y - rr * 2.2f, rr * 4.4f, rr * 4.4f); }
            {
                g.FillEllipse(Brush(A(Color.White, 255 * a)), pt.X - rr * 0.6f, pt.Y - rr * 0.6f, rr * 1.2f, rr * 1.2f); }
        }

        // Dust specks swirling with the flow
        for (int j = 0; j < 70; j++)
        {
            double sp = (j * 0.1373 + t * 0.00028 * (0.7 + (j % 5) * 0.12)) % 1.0;
            float s = (float)sp, d;
            float spreadK = 0.55f + ((j * 37) % 50) / 100f;            // inside / outside the strands
            PointF pt = Funnel(cx, top, len, rTop * spreadK, rBot * spreadK, s, j * 2.399 + spin * 1.25 + s * TWIST, out d);
            float a = beam * (0.2f + 0.6f * (0.5f + 0.5f * d)) * (float)Math.Sin(Math.PI * s);
            if (a > 0.02f)
                g.FillEllipse(Brush(A(crown, 255 * a)), pt.X - 0.9f, pt.Y - 0.9f, 1.8f, 1.8f);
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

    const float TITLE_TRACKING = 1f;     // letter-spacing: 0.05em of 20px
    const float SUB_TRACKING = 0f;

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


    // Text rendered once at device resolution, centred in a box of logical size w x h.
    // threeD = extruded letters (accent-tinted sides, metallic face, lit top edge, soft shadow).
    const float TEXT_PAD = 18f;   // room around the text for the glow

    // Soft glow like CSS text-shadow: 0 0 <radius>px <color>.
    // Real blur: render the text small, then scale it back up with bicubic filtering.
    void Glow(Graphics g, GraphicsPath path, Color c, float radius, float bw, float bh)
    {
        float f = Math.Max(1.5f, radius * scale / 2.2f);                 // downscale factor (device px)
        int sw = Math.Max(1, (int)Math.Ceiling(bw * scale / f));
        int sh = Math.Max(1, (int)Math.Ceiling(bh * scale / f));
        Color boosted = Color.FromArgb(Math.Min(255, (int)(c.A * 1.0f)), c.R, c.G, c.B);
        using (Bitmap small = new Bitmap(sw, sh, PixelFormat.Format32bppArgb))
        {
            using (Graphics gs = Graphics.FromImage(small))
            {
                gs.Clear(Color.Transparent);
                gs.SmoothingMode = SmoothingMode.AntiAlias;
                gs.ScaleTransform(scale / f, scale / f);
                using (SolidBrush br = new SolidBrush(boosted)) gs.FillPath(br, path);
                using (Pen thick = new Pen(boosted, radius * 0.12f)) { thick.LineJoin = LineJoin.Round; gs.DrawPath(thick, path); }
            }
            InterpolationMode im = g.InterpolationMode;
            PixelOffsetMode pm = g.PixelOffsetMode;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            using (ImageAttributes ia = new ImageAttributes())
            {
                ia.SetWrapMode(WrapMode.TileFlipXY);   // no dark fringe at the bitmap edge
                g.DrawImage(small, new PointF[] { new PointF(0, 0), new PointF(bw, 0), new PointF(0, bh) },
                            new RectangleF(0, 0, sw, sh), GraphicsUnit.Pixel, ia);
            }
            g.InterpolationMode = im;
            g.PixelOffsetMode = pm;
        }
    }

    // Text rendered once at device resolution, centred in a w x h box (plus TEXT_PAD all round).
    // glow = the ufo-title look: white text with
    //        text-shadow: 0 0 10px rgba(255,255,255,.5), 0 0 20px rgba(255,220,150,.3)
    Bitmap MakeTextBmp(string text, Font font, float w, float h, Color color, bool glow, out SizeF logical)
    {
        float bw = w + TEXT_PAD * 2f, bh = h + TEXT_PAD * 2f;
        logical = new SizeF(bw, bh);
        Bitmap b = new Bitmap(Math.Max(1, (int)Math.Ceiling(bw * scale)), Math.Max(1, (int)Math.Ceiling(bh * scale)), PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(b))
        using (GraphicsPath path = BuildTextPath(text, font, font == titleFont ? TITLE_TRACKING : SUB_TRACKING))
        {
            g.Clear(Color.Transparent);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.ScaleTransform(scale, scale);

            // Centre (and shrink to fit if needed)
            RectangleF tb = path.GetBounds();
            float k = Math.Min(1f, (w - 4f) / Math.Max(1f, tb.Width));
            using (Matrix m = new Matrix())
            {
                m.Translate(-tb.X, -tb.Y, MatrixOrder.Append);
                m.Scale(k, k, MatrixOrder.Append);
                m.Translate(TEXT_PAD + (w - tb.Width * k) / 2f, TEXT_PAD + (h - tb.Height * k) / 2f, MatrixOrder.Append);
                path.Transform(m);
            }

            if (glow)
            {
                Glow(g, path, Color.FromArgb(30, 255, 220, 150), 16f, bw, bh);  // softer warm halo
                Glow(g, path, Color.FromArgb(55, 255, 255, 255), 8f, bw, bh);   // softer white glow
            }
            using (SolidBrush fill = new SolidBrush(color)) g.FillPath(fill, path);
        }
        return b;
    }

    // FuzzyText: every 1px row of the text is drawn with its own random horizontal offset
    void DrawFuzzy(Graphics g, Bitmap bmp, RectangleF dest, float amp, float alpha)
    {
        if (alpha < 0.02f) return;
        ImageAttributes ia = AlphaAttr(alpha);
        {
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

    const int SMOKE_LEVELS = 16;
    Bitmap[][] smokeLv;

    // Each smoke sprite pre-rendered at SMOKE_LEVELS opacities, 32x32 premultiplied (fast to blit)
    void BuildSmokeLevels()
    {
        smokeLv = new Bitmap[2][];
        for (int s = 0; s < 2; s++)
        {
            smokeLv[s] = new Bitmap[SMOKE_LEVELS];
            for (int lv = 1; lv < SMOKE_LEVELS; lv++)
            {
                Bitmap b = new Bitmap(32, 32, PixelFormat.Format32bppPArgb);
                using (Graphics g = Graphics.FromImage(b))
                using (ImageAttributes ia = new ImageAttributes())
                {
                    ColorMatrix cm = new ColorMatrix();
                    cm.Matrix33 = lv / (float)(SMOKE_LEVELS - 1);
                    ia.SetColorMatrix(cm);
                    g.Clear(Color.Transparent);
                    g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                    g.DrawImage(smoke[s], new Rectangle(0, 0, 32, 32), 0, 0, 64, 64, GraphicsUnit.Pixel, ia);
                }
                smokeLv[s][lv] = b;
            }
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
        CompositingQuality cq = g.CompositingQuality; InterpolationMode im = g.InterpolationMode;
        g.CompositingQuality = CompositingQuality.HighSpeed;
        g.InterpolationMode = InterpolationMode.Low;
        foreach (Puff p in puffs)
        {
            float lifeK = p.Life / p.MaxLife;
            float fadeIn = Math.Min(1f, (p.MaxLife - p.Life) / 90f);
            float a = p.Alpha * fadeIn * (float)Math.Pow(lifeK, 1.3);
            int lv = (int)Math.Round(a * (SMOKE_LEVELS - 1));
            if (lv <= 0) continue;
            // pre-faded sprite: a plain blit, no per-draw colour matrix (that path is very slow in GDI+)
            g.DrawImage(smokeLv[p.Shade][Math.Min(SMOKE_LEVELS - 1, lv)], p.X - p.R, p.Y - p.R, p.R * 2f, p.R * 2f);
        }
        g.CompositingQuality = cq; g.InterpolationMode = im;
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

    // ---- Persistent canvas: a DIB section that GDI+ draws into and Windows reads directly ----
    [StructLayout(LayoutKind.Sequential)]
    struct BIH { public int biSize, biWidth, biHeight; public short biPlanes, biBitCount; public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant; }
    [DllImport("gdi32.dll")] static extern IntPtr CreateDIBSection(IntPtr hdc, ref BIH bmi, uint usage, out IntPtr bits, IntPtr section, uint offset);
    IntPtr canvasDc, canvasDib, canvasOld;
    Bitmap canvas;
    Graphics canvasG;

    void EnsureCanvas()
    {
        if (canvas != null) return;
        BIH h = new BIH();
        h.biSize = Marshal.SizeOf(typeof(BIH));
        h.biWidth = Width; h.biHeight = -Height;   // top-down
        h.biPlanes = 1; h.biBitCount = 32;
        IntPtr bits;
        IntPtr screen = GetDC(IntPtr.Zero);
        canvasDc = CreateCompatibleDC(screen);
        canvasDib = CreateDIBSection(screen, ref h, 0, out bits, IntPtr.Zero, 0);
        ReleaseDC(IntPtr.Zero, screen);
        canvasOld = SelectObject(canvasDc, canvasDib);
        // premultiplied ARGB = exactly what UpdateLayeredWindow wants, no conversion per frame
        canvas = new Bitmap(Width, Height, Width * 4, PixelFormat.Format32bppPArgb, bits);
        canvasG = Graphics.FromImage(canvas);
        canvasG.CompositingMode = CompositingMode.SourceOver;
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        if (canvasG != null) canvasG.Dispose();
        if (canvas != null) canvas.Dispose();
        if (canvasDc != IntPtr.Zero) { SelectObject(canvasDc, canvasOld); DeleteObject(canvasDib); DeleteDC(canvasDc); }
        // free every GDI+ resource now: the tray app hosts many alerts over its lifetime
        timer.Dispose();
        DisposeAll(glyphs); DisposeAll(glyphsRed); DisposeAll(glyphsCyan); DisposeAll(smoke);
        if (smokeLv != null) foreach (Bitmap[] lv in smokeLv) DisposeAll(lv);
        foreach (ImageAttributes ia in alphaAttrs) if (ia != null) ia.Dispose();
        if (subBmp != null) subBmp.Dispose();
        brush.Dispose(); strandPen.Dispose(); glowPen.Dispose();
        titleFont.Dispose(); subFont.Dispose();
        base.OnFormClosed(e);
    }

    static void DisposeAll(Bitmap[] arr)
    {
        if (arr == null) return;
        foreach (Bitmap b in arr) if (b != null) b.Dispose();
    }

    // Cached alpha-only ImageAttributes (33 levels) instead of allocating one per draw call
    readonly ImageAttributes[] alphaAttrs = new ImageAttributes[33];
    ImageAttributes AlphaAttr(float a)
    {
        int k = Math.Max(0, Math.Min(32, (int)Math.Round(a * 32f)));
        if (k == 32) return null;   // fully opaque: skip the slow colour-matrix path
        if (alphaAttrs[k] == null)
        {
            ColorMatrix cm = new ColorMatrix();
            cm.Matrix33 = k / 32f;
            alphaAttrs[k] = new ImageAttributes();
            alphaAttrs[k].SetColorMatrix(cm);
        }
        return alphaAttrs[k];
    }

    readonly SolidBrush brush = new SolidBrush(Color.White);   // reused for all the small dots
    SolidBrush Brush(Color c) { brush.Color = c; return brush; }

    void Render(float y, float beam, double t)
    {
        EnsureCanvas();
        {
            Graphics g = canvasG;
            {
                g.ResetTransform();
                g.ResetClip();
                g.Clear(Color.Transparent);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAlias;
                g.ScaleTransform(scale, scale);
                float cx = W / 2f;

                // ---------- Tractor beam + the title orbiting the spiral ----------
                if (beam > 0.01f)
                {
                    float top = y + 14f;
                    float fullBottom = FUNNEL_END;
                    float bottom = top + (fullBottom - top) * beam;
                    float tw = 20f, bw = 20f + (FUNNEL_R - 26f) * beam;
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

                    float tv = Math.Min(1f, fuzz);                       // 0 = clean signal, 1 = full old TV
                    float flicker = tv > 0.04f ? 1f - tv * 0.4f * (float)rng.NextDouble() : 1f;
                    float amp = fuzz * FUZZ_RANGE;

                    DrawOrbitTitle(g, cx, top, bottom, 15f, bw - 6f, beam, t, false, tv, amp, flicker); // letters behind
                    DrawVortex(g, cx, top, bottom, 15f, bw - 6f, beam, t);
                    DrawOrbitTitle(g, cx, top, bottom, 15f, bw - 6f, beam, t, true, tv, amp, flicker);  // letters in front

                    // subtitle (orange alert) sits still just under the funnel
                    if (subtitle.Length > 0 && beam > 0.02f)
                    {
                        float subY = bottom + (bw - 6f) * 0.34f + 6f;
                        DrawFuzzy(g, subBmp, new RectangleF(cx - subSize.Width / 2f, subY - TEXT_PAD, subSize.Width, subSize.Height),
                                  amp * 0.3f, beam * flicker);
                    }
                }

                // ---------- Smoke (behind the saucer) ----------
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
            g.Flush(FlushIntention.Sync);
            Push();
        }
    }

    void Push()
    {
        IntPtr screen = GetDC(IntPtr.Zero);
        try
        {
            PT pos = new PT(Left, Top), src = new PT(0, 0);
            SZ size = new SZ(Width, Height);
            BLEND bl = new BLEND(); bl.Op = 0; bl.Flags = 0; bl.Alpha = 255; bl.Format = 1; // AC_SRC_ALPHA
            UpdateLayeredWindow(Handle, screen, ref pos, ref size, canvasDc, ref src, 0, ref bl, 2); // ULW_ALPHA
        }
        finally { ReleaseDC(IntPtr.Zero, screen); }
    }
}
