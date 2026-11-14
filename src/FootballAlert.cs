// "Striker" style: the NotifyFinish alien runs up, shoots and scores - the agent's *finish*.
// Same place as the UFO alerts: a small transparent window at the top centre.
// The message pops up big above the pitch the moment the ball hits the net.
// When the agent needs you instead, the alien does keepy-ups waiting for you (orange).
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;

public class FootballAlert : Form
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
    const float GY = 150f;                    // the ground: where feet and the ball sit
    const float BALL_R = 7f;
    const float START_X = 58f, KICK_X = 128f; // alien run-up: from here to the ball
    const float BALL_X = 148f;
    const float GOAL_FRONT = 362f, GOAL_BACK = 410f, GOAL_TOP = 80f;
    const float DEPTH_X = -12f, DEPTH_Y = -20f; // far side of the goal: up and towards the vanishing point
    const float AIM_X = GOAL_BACK - 10f, AIM_Y = 114f, ARC = 30f;
    // pitch slab in perspective: far edge (top) narrower than the near edge, plus a front face
    const float P_FAR = 112f, P_NEAR = 166f, P_FACE = 10f;
    const float P_FAR_L = 58f, P_FAR_R = W - 58f, P_NEAR_L = 12f, P_NEAR_R = W - 12f;
    const float TITLE_Y = 40f;

    // ---- Timeline (ms) ----
    const double DROP = 380, RUN = 340, KICK = 110, FLY = 420, LEAVE = 450;
    const double KICK_AT = DROP + RUN;
    const double CONTACT = KICK_AT + KICK * 0.55;   // the moment the foot meets the ball
    const double GOAL_AT = CONTACT + FLY;
    const double MAX_HOLD = 3000;                   // the celebration never outstays this
    const double JUGGLE = 620;                      // keepy-up period (needs-you mode)

    static readonly Color Skin = Color.FromArgb(125, 215, 95);   // the brand alien
    static readonly Color SkinDark = Color.FromArgb(88, 172, 70);

    readonly string title;
    readonly Color accent;
    readonly bool needsYou;
    readonly double stayMs;
    readonly float H, scale;
    readonly Font titleFont, numberFont;
    readonly Timer timer;
    DateTime start;
    bool leaving; double leaveAt; float leaveDy, lastDy;

    // ---- Confetti (analytic: position = launch + v*t + g*t^2/2, nothing to simulate) ----
    const int CONFETTI = 44;
    const double CONFETTI_LIFE = 1800;
    readonly float[] cVx = new float[CONFETTI], cVy = new float[CONFETTI], cRot = new float[CONFETTI],
                     cSpin = new float[CONFETTI], cW = new float[CONFETTI], cH = new float[CONFETTI];
    readonly Color[] cCol = new Color[CONFETTI];

    public static void Run(string title, int r, int g, int b, bool needsYou, int seconds)
    {
        try { SetProcessDPIAware(); } catch { }
        Application.Run(Create(title, Color.FromArgb(r, g, b), needsYou, seconds));
    }

    public static Form Create(string title, Color accent, bool needsYou, int seconds)
    {
        return new FootballAlert(title, accent, needsYou, seconds);
    }

    FootballAlert(string title, Color accent, bool needsYou, int seconds)
    {
        this.title = title.ToUpperInvariant();
        this.accent = accent;
        this.needsYou = needsYou;
        stayMs = Math.Max(1, seconds) * 1000.0;

        using (Graphics sg = Graphics.FromHwnd(IntPtr.Zero)) scale = sg.DpiX / 96f;

        // big cartoon title where "GOAL!" would go; shrinks to fit long phrases
        float px = 36f;
        Font f = null;
        using (Bitmap tmp = new Bitmap(1, 1))
        using (Graphics mg = Graphics.FromImage(tmp))
            for (; px >= 22f; px -= 2f)
            {
                if (f != null) f.Dispose();
                f = PickFont(px, FontStyle.Regular, "Segoe UI Black") ?? new Font("Segoe UI", px, FontStyle.Bold, GraphicsUnit.Pixel);
                if (mg.MeasureString(this.title, f).Width <= W - 130f) break;   // clear of the goal
            }
        titleFont = f;
        numberFont = new Font("Segoe UI", 9f, FontStyle.Bold, GraphicsUnit.Pixel);
        H = P_NEAR + P_FACE + 10f;

        Random rnd = new Random();
        Color[] palette = { accent, Color.White, Color.FromArgb(255, 222, 70), Color.FromArgb(255, 85, 115), Color.FromArgb(70, 175, 255) };
        for (int i = 0; i < CONFETTI; i++)
        {
            cVx[i] = (float)(-0.32 + rnd.NextDouble() * 0.38);   // px/ms, mostly back towards the pitch
            cVy[i] = (float)(-0.48 + rnd.NextDouble() * 0.30);
            cRot[i] = (float)(rnd.NextDouble() * 360);
            cSpin[i] = (float)(-0.9 + rnd.NextDouble() * 1.8);   // deg/ms
            cW[i] = 3f + (float)rnd.NextDouble() * 3f;
            cH[i] = 2f + (float)rnd.NextDouble() * 2f;
            cCol[i] = palette[rnd.Next(palette.Length)];
        }

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
        FormClosed += delegate { timer.Dispose(); };
    }

    static Font PickFont(float px, FontStyle style, params string[] names)
    {
        foreach (string n in names)
        {
            try
            {
                Font f = new Font(n, px, style, GraphicsUnit.Pixel);
                if (string.Equals(f.Name, n, StringComparison.OrdinalIgnoreCase)) return f;
                f.Dispose();
            }
            catch { }
        }
        return null;
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
        leaveDy = lastDy;
    }

    // ---- Easing ----
    static float Lerp(float a, float b, double t) { return a + (b - a) * (float)t; }
    static double Clamp01(double t) { return t < 0 ? 0 : (t > 1 ? 1 : t); }
    static double EaseOutBack(double t) { const double c1 = 1.4, c3 = c1 + 1; t -= 1; return 1 + c3 * t * t * t + c1 * t * t; }
    static double EaseInBack(double t) { const double c1 = 1.6, c3 = c1 + 1; return c3 * t * t * t - c1 * t * t; }
    static double EaseOutCubic(double t) { t = 1 - t; return 1 - t * t * t; }

    // when the title pops in: right away when the agent needs you, on the goal otherwise
    double TitleAt { get { return needsYou ? DROP * 0.6 : GOAL_AT; } }

    void Frame()
    {
        double t = (DateTime.Now - start).TotalMilliseconds;
        double hold = needsYou ? stayMs : Math.Min(stayMs, MAX_HOLD);
        if (!leaving && t >= TitleAt + hold) SendAway();

        float dy;
        if (leaving)
        {
            double u = (t - leaveAt) / LEAVE;
            if (u >= 1) { timer.Stop(); Close(); return; }
            dy = Lerp(leaveDy, -H - 20f, EaseInBack(Clamp01(u)));
        }
        else dy = t < DROP ? Lerp(-H, 0f, EaseOutBack(Clamp01(t / DROP))) : 0f;
        lastDy = dy;
        Render(t, dy);
    }

    static Color A(Color c, double a) { return Color.FromArgb(Math.Max(0, Math.Min(255, (int)a)), c.R, c.G, c.B); }
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

    // ---------------------------------------------------------------- state over time

    // How far the net is pushed out (px) after the ball hits it: a damped wobble
    static float Bulge(double t)
    {
        double u = t - GOAL_AT;
        if (u < 0) return 0f;
        return (float)(13.0 * Math.Exp(-u / 320.0) * Math.Cos(u / 75.0));
    }

    void Ball(double t, out float x, out float y, out float rot)
    {
        if (needsYou)
        {
            // keepy-ups on the right foot
            double u = ((t % JUGGLE) / JUGGLE);
            x = KICK_X + 17f;
            y = GY - 15f - (float)(4 * u * (1 - u)) * 44f;
            rot = (float)(t * 0.25);
            return;
        }
        if (t < CONTACT) { x = BALL_X; y = GY - BALL_R; rot = 0f; return; }
        if (t < GOAL_AT)
        {
            double u = (t - CONTACT) / FLY;
            x = Lerp(BALL_X, AIM_X, u);
            y = Lerp(GY - BALL_R, AIM_Y, u) - (float)(4 * u * (1 - u)) * ARC;
            rot = (float)(u * 720);
            return;
        }
        // in the net: pushed back with the bulge, then drops and bounces to rest
        double tau = t - GOAL_AT;
        x = AIM_X + Math.Max(0f, Bulge(t)) * 0.8f;
        if (tau < 380) y = Lerp(AIM_Y, GY - BALL_R, (tau / 380) * (tau / 380));
        else
        {
            double b = tau - 380;
            y = GY - BALL_R - (float)(11 * Math.Abs(Math.Sin(Math.PI * b / 260)) * Math.Exp(-b / 280));
        }
        rot = (float)(720 + 200 * (1 - Math.Exp(-tau / 400)));
    }

    // alien pose: x, how high it's lifted, both leg angles and both arm angles (degrees from straight down,
    // positive = towards +x / the goal)
    void Alien(double t, out float x, out float lift, out float backLeg, out float kickLeg, out float armL, out float armR, out bool joy)
    {
        joy = false; lift = 0f;
        if (needsYou)
        {
            double u = (t % JUGGLE) / JUGGLE;
            double k = Math.Min(u, 1 - u);                  // distance from the touch
            x = KICK_X;
            kickLeg = k < 0.16 ? Lerp(62f, 12f, k / 0.16) : 12f;
            backLeg = -6f;
            armL = -38f; armR = 30f;                         // arms out for balance
            lift = (float)(k < 0.16 ? 0 : 0.6 * Math.Sin(u * Math.PI));
            return;
        }
        if (t < DROP)
        {
            x = START_X; backLeg = -6f; kickLeg = 6f; armL = -12f; armR = 12f;
            return;
        }
        if (t < KICK_AT)
        {
            double u = (t - DROP) / RUN;
            double ph = (t - DROP) / 85.0;
            x = Lerp(START_X, KICK_X, u);
            float sw = (float)(34 * Math.Sin(ph));
            kickLeg = sw; backLeg = -sw;
            armL = sw * 1.1f; armR = -sw * 1.1f;
            lift = (float)(2.5 * Math.Abs(Math.Sin(ph)));
            return;
        }
        x = KICK_X;
        if (t < KICK_AT + KICK)
        {
            double u = (t - KICK_AT) / KICK;
            kickLeg = Lerp(-58f, 80f, EaseOutCubic(u));
            backLeg = -8f; armL = -45f; armR = 20f;
            return;
        }
        if (t < GOAL_AT)
        {
            double u = Clamp01((t - KICK_AT - KICK) / 320);
            kickLeg = Lerp(80f, 8f, EaseOutCubic(u));
            backLeg = -8f; armL = Lerp(-45f, -15f, u); armR = Lerp(20f, 15f, u);
            return;
        }
        // GOAL! jump with both arms up
        joy = true;
        double tau = t - GOAL_AT;
        double hop = Math.Abs(Math.Sin(Math.PI * tau / 380));
        lift = (float)(16 * hop);
        kickLeg = (float)(10 + 18 * hop); backLeg = -kickLeg;
        float wave = (float)(12 * Math.Sin(tau / 90.0));
        armL = -150f + wave; armR = 150f - wave;
    }

    // ---------------------------------------------------------------- drawing

    void Render(double t, float dy)
    {
        using (Bitmap bmp = new Bitmap(Width, Height, PixelFormat.Format32bppArgb))
        {
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAlias;
                g.ScaleTransform(scale, scale);
                g.TranslateTransform(0, dy);
                DrawScene(g, t);
            }
            Push(bmp);
        }
    }

    // the whole scene at time t, in logical px
    void DrawScene(Graphics g, double t)
    {
        float bx, by, brot;
        Ball(t, out bx, out by, out brot);
        float ax, lift, legB, legK, armL, armR; bool joy;
        Alien(t, out ax, out lift, out legB, out legK, out armL, out armR, out joy);
        float bulge = needsYou ? 0f : Bulge(t);

        DrawPitch(g);
        DrawShadow(g, ax, 34f, lift);
        DrawShadow(g, bx, 15f, GY - BALL_R - by);
        DrawGoalBack(g, bulge);
        if (!needsYou) DrawBall(g, bx, by, brot);
        DrawGoalFront(g, bulge);
        DrawAlien(g, ax, GY - lift, legB, legK, armL, armR, joy, t);
        if (needsYou) DrawBall(g, bx, by, brot);     // juggled in front of the alien
        if (!needsYou && t >= GOAL_AT) DrawConfetti(g, t - GOAL_AT);
        if (t >= TitleAt) DrawTitle(g, t - TitleAt);
    }

    // ---- pitch in perspective ----
    static float EdgeL(float y) { return P_NEAR_L + (P_FAR_L - P_NEAR_L) * (P_NEAR - y) / (P_NEAR - P_FAR); }
    static float EdgeR(float y) { return P_NEAR_R + (P_FAR_R - P_NEAR_R) * (P_NEAR - y) / (P_NEAR - P_FAR); }

    // a point on the turf: u = 0..1 left to right, v = 0..1 far to near
    static PointF OnPitch(float u, float v)
    {
        float y = P_FAR + (P_NEAR - P_FAR) * v;
        return new PointF(EdgeL(y) + (EdgeR(y) - EdgeL(y)) * u, y);
    }

    const float END_U = 0.823f;   // the goal line: runs through the goal mouth

    void DrawPitch(Graphics g)
    {
        PointF fl = new PointF(P_FAR_L, P_FAR), fr = new PointF(P_FAR_R, P_FAR),
               nr = new PointF(P_NEAR_R, P_NEAR), nl = new PointF(P_NEAR_L, P_NEAR);

        // soft shadow under the slab
        using (GraphicsPath sh = new GraphicsPath())
        {
            sh.AddEllipse(P_NEAR_L + 6f, P_NEAR + P_FACE - 7f, P_NEAR_R - P_NEAR_L - 12f, 14f);
            using (PathGradientBrush pb = new PathGradientBrush(sh))
            {
                pb.CenterColor = Color.FromArgb(110, 0, 0, 0);
                pb.SurroundColors = new Color[] { Color.FromArgb(0, 0, 0, 0) };
                g.FillPath(pb, sh);
            }
        }

        // front face: the edge of the turf and a slice of earth
        PointF[] face = { nl, nr, new PointF(nr.X - 3f, nr.Y + P_FACE), new PointF(nl.X + 3f, nl.Y + P_FACE) };
        using (LinearGradientBrush fb = new LinearGradientBrush(new PointF(0, P_NEAR), new PointF(0, P_NEAR + P_FACE + 1),
            Color.FromArgb(255, 30, 98, 40), Color.FromArgb(255, 88, 58, 34)))
            g.FillPolygon(fb, face);
        using (Pen lip = new Pen(Color.FromArgb(255, 24, 80, 32), 2.2f))
            g.DrawLine(lip, nl.X + 1f, nl.Y + 1.5f, nr.X - 1f, nr.Y + 1.5f);

        using (GraphicsPath top = new GraphicsPath())
        {
            top.AddPolygon(new PointF[] { fl, fr, nr, nl });
            using (LinearGradientBrush tb = new LinearGradientBrush(new PointF(0, P_FAR), new PointF(0, P_NEAR + 1),
                Color.FromArgb(255, 46, 128, 56), Color.FromArgb(255, 92, 196, 92)))
                g.FillPath(tb, top);

            GraphicsState st = g.Save();
            g.SetClip(top);
            // mowing stripes, converging with the perspective
            using (SolidBrush stripe = new SolidBrush(Color.FromArgb(30, 255, 255, 255)))
                for (int i = 0; i < 14; i += 2)
                {
                    float u0 = i / 14f, u1 = (i + 1) / 14f;
                    g.FillPolygon(stripe, new PointF[] { OnPitch(u0, 0), OnPitch(u1, 0), OnPitch(u1, 1), OnPitch(u0, 1) });
                }
            // the far end fades into a little haze, the near end gets the light
            using (LinearGradientBrush haze = new LinearGradientBrush(new PointF(0, P_FAR - 1), new PointF(0, P_FAR + 24),
                Color.FromArgb(70, 230, 245, 255), Color.FromArgb(0, 230, 245, 255)))
                g.FillRectangle(haze, 0, P_FAR - 1, W, 25);
            g.Restore(st);

            using (Pen rim = new Pen(Color.FromArgb(90, 255, 255, 255), 1.2f))
                g.DrawPath(rim, top);
        }

        // markings
        using (Pen line = new Pen(Color.FromArgb(205, 255, 255, 255), 1.4f))
        {
            const float L = 0.035f, V0 = 0.1f, V1 = 0.9f;
            line.LineJoin = LineJoin.Round;
            g.DrawPolygon(line, new PointF[] { OnPitch(L, V0), OnPitch(END_U, V0), OnPitch(END_U, V1), OnPitch(L, V1) });
            float mid = (L + END_U) / 2f;
            g.DrawLine(line, OnPitch(mid, V0), OnPitch(mid, V1));
            PointF c = OnPitch(mid, 0.5f);
            g.DrawEllipse(line, c.X - 30f, c.Y - 8.5f, 60f, 17f);
            g.DrawLines(line, new PointF[] { OnPitch(END_U, 0.24f), OnPitch(END_U - 0.12f, 0.24f), OnPitch(END_U - 0.12f, 0.8f), OnPitch(END_U, 0.8f) });
            g.DrawLines(line, new PointF[] { OnPitch(L, 0.24f), OnPitch(L + 0.12f, 0.24f), OnPitch(L + 0.12f, 0.8f), OnPitch(L, 0.8f) });
        }
        using (SolidBrush spot = new SolidBrush(Color.FromArgb(215, 255, 255, 255)))
        {
            PointF ps = OnPitch(END_U - 0.08f, 0.55f), pc = OnPitch((0.035f + END_U) / 2f, 0.5f);
            g.FillEllipse(spot, ps.X - 2f, ps.Y - 1.2f, 4f, 2.4f);
            g.FillEllipse(spot, pc.X - 2f, pc.Y - 1.2f, 4f, 2.4f);
        }
    }

    static void DrawShadow(Graphics g, float x, float w, float height)
    {
        float k = Math.Max(0.35f, 1f - Math.Max(0f, height) / 80f);
        using (SolidBrush sb = new SolidBrush(Color.FromArgb((int)(70 * k), 0, 0, 0)))
            g.FillEllipse(sb, x - w * k / 2f, GY - 2f, w * k, 6f);
    }

    // ---- goal as a 3D box: u = 0..1 mouth to back, d = 0..1 near side to far side, y in near-side coordinates ----
    static PointF Goal(float u, float y, float d, float bulge)
    {
        float dyb = (y - AIM_Y) / 26f;
        float push = bulge * u * (float)Math.Exp(-dyb * dyb) * (1f - 0.4f * Math.Abs(d - 0.5f) * 2f);
        return new PointF(GOAL_FRONT + (GOAL_BACK - GOAL_FRONT) * u + DEPTH_X * d + push, y + DEPTH_Y * d);
    }

    static void Panel(Graphics g, Pen pen, Brush fill, int cols, int rows, Func<float, float, PointF> at)
    {
        const int STEPS = 10;
        if (fill != null)
        {
            PointF[] edge = new PointF[STEPS * 4];
            for (int i = 0; i < STEPS; i++)
            {
                float k = i / (float)STEPS;
                edge[i] = at(k, 0f); edge[STEPS + i] = at(1f, k);
                edge[2 * STEPS + i] = at(1f - k, 1f); edge[3 * STEPS + i] = at(0f, 1f - k);
            }
            g.FillPolygon(fill, edge);
        }
        for (int c = 0; c <= cols; c++)
        {
            PointF[] pts = new PointF[STEPS + 1];
            for (int i = 0; i <= STEPS; i++) pts[i] = at(c / (float)cols, i / (float)STEPS);
            g.DrawLines(pen, pts);
        }
        for (int r = 0; r <= rows; r++)
        {
            PointF[] pts = new PointF[STEPS + 1];
            for (int i = 0; i <= STEPS; i++) pts[i] = at(i / (float)STEPS, r / (float)rows);
            g.DrawLines(pen, pts);
        }
    }

    static float GY_(float k) { return GOAL_TOP + (GY - GOAL_TOP) * k; }

    // everything the ball passes in front of: far side net, back net, roof, far post
    static void DrawGoalBack(Graphics g, float bulge)
    {
        using (SolidBrush fill = new SolidBrush(Color.FromArgb(34, 255, 255, 255)))
        using (Pen net = new Pen(Color.FromArgb(105, 255, 255, 255), 0.9f))
        {
            Panel(g, net, fill, 4, 6, delegate(float a, float b) { return Goal(a, GY_(b), 1f, bulge); });   // far side
            Panel(g, net, fill, 3, 6, delegate(float a, float b) { return Goal(1f, GY_(b), a, bulge); });   // back
            Panel(g, net, fill, 4, 3, delegate(float a, float b) { return Goal(a, GOAL_TOP, b, bulge); });  // roof
        }
        using (Pen bar = new Pen(Color.FromArgb(255, 175, 182, 196), 2.2f))
        {
            bar.StartCap = bar.EndCap = LineCap.Round;
            g.DrawLine(bar, Goal(0, GOAL_TOP, 1, 0), Goal(1, GOAL_TOP, 1, 0));
            g.DrawLine(bar, Goal(1, GOAL_TOP, 0, 0), Goal(1, GOAL_TOP, 1, 0));
        }
        using (Pen post = new Pen(Color.FromArgb(255, 228, 232, 240), 3.6f))
        {
            post.StartCap = post.EndCap = LineCap.Round;
            g.DrawLine(post, Goal(0, GOAL_TOP, 1, 0), Goal(0, GY, 1, 0));          // far post
        }
    }

    // in front of the ball: near side net, near post and the crossbar
    static void DrawGoalFront(Graphics g, float bulge)
    {
        using (Pen net = new Pen(Color.FromArgb(150, 255, 255, 255), 1f))
            Panel(g, net, null, 4, 6, delegate(float a, float b) { return Goal(a, GY_(b), 0f, bulge); });
        using (Pen bar = new Pen(Color.FromArgb(255, 190, 196, 210), 2.6f))
        {
            bar.StartCap = bar.EndCap = LineCap.Round;
            g.DrawLine(bar, Goal(0, GOAL_TOP, 0, 0), Goal(1, GOAL_TOP, 0, 0));
        }
        using (Pen post = new Pen(Color.White, 4.5f))
        {
            post.StartCap = post.EndCap = LineCap.Round;
            g.DrawLine(post, Goal(0, GOAL_TOP, 0, 0), Goal(0, GOAL_TOP, 1, 0));    // crossbar
            g.DrawLine(post, Goal(0, GOAL_TOP, 0, 0), Goal(0, GY, 0, 0));          // near post
        }
        using (Pen shade = new Pen(Color.FromArgb(90, 120, 130, 150), 1.2f))
            g.DrawLine(shade, GOAL_FRONT + 1.4f, GOAL_TOP + 2f, GOAL_FRONT + 1.4f, GY - 1f);
    }

    static void DrawBall(Graphics g, float x, float y, float rot)
    {
        RectangleF r = new RectangleF(x - BALL_R, y - BALL_R, BALL_R * 2, BALL_R * 2);
        using (LinearGradientBrush fill = new LinearGradientBrush(r, Color.White, Color.FromArgb(205, 210, 220), LinearGradientMode.ForwardDiagonal))
            g.FillEllipse(fill, r);

        GraphicsState st = g.Save();
        using (GraphicsPath clip = new GraphicsPath())
        {
            clip.AddEllipse(r);
            g.SetClip(clip);
            g.TranslateTransform(x, y);
            g.RotateTransform(rot);
            using (SolidBrush patch = new SolidBrush(Color.FromArgb(40, 42, 52)))
            {
                g.FillPolygon(patch, Pentagon(0, 0, BALL_R * 0.42f, 0));
                for (int i = 0; i < 5; i++)
                {
                    double a = (i * 72 + 36) * Math.PI / 180;
                    g.FillPolygon(patch, Pentagon((float)Math.Cos(a) * BALL_R * 1.05f, (float)Math.Sin(a) * BALL_R * 1.05f, BALL_R * 0.36f, i * 72 + 36));
                }
            }
        }
        g.Restore(st);
        using (Pen edge = new Pen(Color.FromArgb(60, 64, 78), 1f))
            g.DrawEllipse(edge, r);
    }

    static PointF[] Pentagon(float cx, float cy, float rad, float deg)
    {
        PointF[] p = new PointF[5];
        for (int i = 0; i < 5; i++)
        {
            double a = (deg + i * 72 - 90) * Math.PI / 180;
            p[i] = new PointF(cx + (float)Math.Cos(a) * rad, cy + (float)Math.Sin(a) * rad);
        }
        return p;
    }

    void DrawAlien(Graphics g, float x, float feet, float legB, float legK, float armL, float armR, bool joy, double t)
    {
        float hipY = feet - 15f;
        PointF hipB = new PointF(x - 4f, hipY), hipK = new PointF(x + 4f, hipY);

        using (Pen leg = new Pen(SkinDark, 5f))
        {
            leg.StartCap = leg.EndCap = LineCap.Round;
            DrawLimb(g, leg, hipB, legB, 14f, true);
            DrawLimb(g, leg, hipK, legK, 14f, true);
        }

        // shorts + jersey in the alert colour, with a number on the back
        using (SolidBrush shorts = new SolidBrush(Color.FromArgb(38, 42, 58)))
        using (GraphicsPath sp = RoundRect(new RectangleF(x - 10f, hipY - 5f, 20f, 8f), 3f))
            g.FillPath(shorts, sp);
        RectangleF jr = new RectangleF(x - 10.5f, feet - 39f, 21f, 22f);
        using (GraphicsPath jersey = RoundRect(jr, 7f))
        {
            using (LinearGradientBrush jb = new LinearGradientBrush(new PointF(0, jr.Top), new PointF(0, jr.Bottom + 1),
                Mix(accent, Color.White, 0.25f), Mix(accent, Color.Black, 0.15f)))
                g.FillPath(jb, jersey);
            using (Pen je = new Pen(Color.FromArgb(120, 255, 255, 255), 1f))
                g.DrawPath(je, jersey);
        }
        StringFormat sf = new StringFormat();
        sf.Alignment = StringAlignment.Center; sf.LineAlignment = StringAlignment.Center;
        using (SolidBrush num = new SolidBrush(Color.FromArgb(235, 255, 255, 255)))
            g.DrawString("10", numberFont, num, new RectangleF(jr.X, jr.Y + 3f, jr.Width, jr.Height - 6f), sf);

        using (Pen arm = new Pen(Skin, 4f))
        {
            arm.StartCap = arm.EndCap = LineCap.Round;
            DrawLimb(g, arm, new PointF(x - 9f, feet - 35f), armL, 12f, false);
            DrawLimb(g, arm, new PointF(x + 9f, feet - 35f), armR, 12f, false);
        }

        // head: the logo's alien, a bit bigger, looking towards the goal
        float hx = x + 1f, hy = feet - 52f;
        RectangleF head = new RectangleF(hx - 16f, hy - 14.5f, 32f, 29f);
        using (LinearGradientBrush hb = new LinearGradientBrush(head, Mix(Skin, Color.White, 0.15f), SkinDark, LinearGradientMode.Vertical))
            g.FillEllipse(hb, head);
        using (Pen ho = new Pen(Color.FromArgb(90, 40, 90, 40), 1f))
            g.DrawEllipse(ho, head);

        bool blink = !joy && (t % 3100) < 140;
        float eh = blink ? 1.6f : (joy ? 9f : 12.5f);
        float ey = hy - 5.5f + (12.5f - eh) / 2f;
        float look = needsYou ? 0f : 1.5f;
        using (SolidBrush eye = new SolidBrush(Color.FromArgb(22, 22, 32)))
        {
            g.FillEllipse(eye, hx - 10f + look, ey, 8.5f, eh);
            g.FillEllipse(eye, hx + 1.5f + look, ey, 8.5f, eh);
        }
        if (!blink)
            using (SolidBrush glint = new SolidBrush(Color.FromArgb(210, 255, 255, 255)))
            {
                g.FillEllipse(glint, hx - 8f + look, ey + 2f, 2.8f, 3.2f);
                g.FillEllipse(glint, hx + 3.5f + look, ey + 2f, 2.8f, 3.2f);
            }
        if (joy)   // big grin while celebrating
            using (Pen smile = new Pen(Color.FromArgb(22, 22, 32), 1.6f))
            {
                smile.StartCap = smile.EndCap = LineCap.Round;
                g.DrawArc(smile, hx - 5f, hy + 2f, 10f, 7f, 15, 150);
            }
    }

    // a limb from `from`, `deg` degrees from straight down (positive swings towards +x)
    static void DrawLimb(Graphics g, Pen pen, PointF from, float deg, float len, bool shoe)
    {
        double a = deg * Math.PI / 180;
        PointF to = new PointF(from.X + (float)Math.Sin(a) * len, from.Y + (float)Math.Cos(a) * len);
        g.DrawLine(pen, from, to);
        if (shoe)
            using (SolidBrush b = new SolidBrush(Color.FromArgb(30, 32, 42)))
                g.FillEllipse(b, to.X - 3f, to.Y - 2.5f, 8f, 5f);
    }

    void DrawConfetti(Graphics g, double tau)
    {
        if (tau > CONFETTI_LIFE) return;
        float fade = (float)Clamp01((CONFETTI_LIFE - tau) / 500);
        const float G = 0.0009f;   // px/ms^2
        float ox = GOAL_FRONT + 8f, oy = AIM_Y - 14f;
        for (int i = 0; i < CONFETTI; i++)
        {
            float tt = (float)tau;
            float px = ox + cVx[i] * tt;
            float py = oy + cVy[i] * tt + 0.5f * G * tt * tt;
            if (py > GY + 4f) continue;
            GraphicsState st = g.Save();
            g.TranslateTransform(px, py);
            g.RotateTransform(cRot[i] + cSpin[i] * tt);
            float flip = (float)Math.Abs(Math.Cos((tt + i * 37) / 120.0));   // paper flutter
            using (SolidBrush b = new SolidBrush(A(cCol[i], 255 * fade)))
                g.FillRectangle(b, -cW[i] / 2f, -cH[i] * flip / 2f, cW[i], Math.Max(0.6f, cH[i] * flip));
            g.Restore(st);
        }
    }

    // the message, big and bouncy where a "GOAL!" would go
    void DrawTitle(Graphics g, double tau)
    {
        double p = Clamp01(tau / 380);
        float s = (float)(0.3 + 0.7 * EaseOutBack(p));
        float alpha = (float)Clamp01(tau / 150);
        GraphicsState st = g.Save();
        g.TranslateTransform(W / 2f, TITLE_Y);
        g.RotateTransform((float)(-3 + 2.5 * Math.Sin(tau / 260.0)));
        g.ScaleTransform(s, s);
        using (GraphicsPath path = new GraphicsPath())
        {
            StringFormat sf = new StringFormat();
            sf.Alignment = StringAlignment.Center; sf.LineAlignment = StringAlignment.Center;
            sf.FormatFlags = StringFormatFlags.NoWrap;
            path.AddString(title, titleFont.FontFamily, (int)titleFont.Style, titleFont.Size, new RectangleF(-W / 2f, -30f, W, 60f), sf);

            using (Matrix m = new Matrix()) { m.Translate(2f, 3f); path.Transform(m); }
            using (SolidBrush sh = new SolidBrush(Color.FromArgb((int)(80 * alpha), 0, 0, 0))) g.FillPath(sh, path);
            using (Matrix m = new Matrix()) { m.Translate(-2f, -3f); path.Transform(m); }

            using (Pen outline = new Pen(A(Mix(accent, Color.Black, 0.35f), 255 * alpha), 6f))
            {
                outline.LineJoin = LineJoin.Round;
                g.DrawPath(outline, path);
            }
            using (LinearGradientBrush fill = new LinearGradientBrush(new PointF(0, -20f), new PointF(0, 20f),
                A(Color.White, 255 * alpha), A(Color.FromArgb(255, 236, 140), 255 * alpha)))
                g.FillPath(fill, path);
        }
        g.Restore(st);
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
