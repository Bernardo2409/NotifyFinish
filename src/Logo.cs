// The NotifyFinish brand mark: the UFO with its little alien.
// One drawing used everywhere: the exe icon, the tray icon and the README banner.
using System;
using System.Drawing;
using System.Drawing.Drawing2D;

public static class Logo
{
    static Color Mix(Color a, Color b, float k)
    {
        return Color.FromArgb(a.A, (int)(a.R + (b.R - a.R) * k), (int)(a.G + (b.G - a.G) * k), (int)(a.B + (b.B - a.B) * k));
    }

    // Draws the mark into a square of side s at (x, y).
    // on = alien green and yellow lights; off = greyed out with red lights (tray "alerts off").
    // beam = draw the tractor beam (big sizes); without it the UFO is centred and fills the square.
    public static void DrawMark(Graphics g, float x, float y, float s, bool on, bool beam)
    {
        GraphicsState st = g.Save();
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TranslateTransform(x, y);
        g.ScaleTransform(s / 100f, s / 100f);   // designed on a 100 x 100 grid
        if (!beam)
        {
            // the UFO alone spans y 14..66: centre it and zoom so it fills the icon
            g.TranslateTransform(50f, 50f);
            g.ScaleTransform(1.1f, 1.1f);
            g.TranslateTransform(-50f, -40f);
        }

        Color accent = on ? Color.FromArgb(30, 190, 80) : Color.FromArgb(140, 140, 140);

        // tractor beam
        if (beam)
            using (GraphicsPath bp = new GraphicsPath())
            {
                bp.AddPolygon(new PointF[] { new PointF(38, 60), new PointF(62, 60), new PointF(86, 99), new PointF(14, 99) });
                using (LinearGradientBrush lb = new LinearGradientBrush(new PointF(0, 59), new PointF(0, 100),
                    Color.FromArgb(on ? 190 : 110, Mix(accent, Color.White, 0.45f)), Color.FromArgb(0, accent)))
                    g.FillPath(lb, bp);
            }

        // glass dome (back)
        RectangleF dome = new RectangleF(29, 14, 42, 44);
        using (LinearGradientBrush db = new LinearGradientBrush(dome,
            Color.FromArgb(225, 200, 240, 255), Color.FromArgb(205, 80, 160, 210), LinearGradientMode.Vertical))
            g.FillEllipse(db, dome);

        // the alien
        Color skin = on ? Color.FromArgb(125, 215, 95) : Color.FromArgb(165, 165, 165);
        using (SolidBrush b = new SolidBrush(skin)) g.FillEllipse(b, 38, 21, 24, 22);
        using (SolidBrush eye = new SolidBrush(Color.FromArgb(22, 22, 32)))
        {
            g.FillEllipse(eye, 42.5f, 27.5f, 6.5f, 9.5f);
            g.FillEllipse(eye, 51f, 27.5f, 6.5f, 9.5f);
        }
        using (SolidBrush glint = new SolidBrush(Color.FromArgb(200, 255, 255, 255)))
        {
            g.FillEllipse(glint, 44f, 29f, 2.2f, 2.6f);
            g.FillEllipse(glint, 52.5f, 29f, 2.2f, 2.6f);
        }

        // glass highlight + rim
        using (SolidBrush hl = new SolidBrush(Color.FromArgb(150, 255, 255, 255))) g.FillEllipse(hl, 33, 18, 11, 7);
        using (Pen rim = new Pen(Color.FromArgb(190, 255, 255, 255), 1.6f)) g.DrawArc(rim, dome, 180, 180);

        // saucer
        RectangleF body = new RectangleF(5, 40, 90, 26);
        using (LinearGradientBrush bb = new LinearGradientBrush(body,
            Color.FromArgb(245, 247, 252), Color.FromArgb(105, 112, 130), LinearGradientMode.Vertical))
            g.FillEllipse(bb, body);
        using (SolidBrush under = new SolidBrush(Color.FromArgb(78, 84, 100))) g.FillEllipse(under, 19, 54, 62, 10);
        using (Pen outline = new Pen(Color.FromArgb(55, 60, 74), 1.8f)) g.DrawEllipse(outline, body);

        // running lights
        Color lamp = on ? Color.FromArgb(255, 232, 95) : Color.FromArgb(235, 60, 55);
        float[] lx = { 18f, 33f, 50f, 67f, 82f };
        for (int i = 0; i < lx.Length; i++)
        {
            float ly = 50f + (1f - Math.Abs(lx[i] - 50f) / 32f) * 3f;
            using (SolidBrush halo = new SolidBrush(Color.FromArgb(110, lamp))) g.FillEllipse(halo, lx[i] - 5.5f, ly - 5.5f, 11, 11);
            using (SolidBrush b = new SolidBrush(lamp)) g.FillEllipse(b, lx[i] - 3f, ly - 3f, 6, 6);
        }

        g.Restore(st);
    }

    // Icon-ready bitmap: the beam only shows at 64 px and up, smaller icons are just the UFO
    public static Bitmap Mark(int size, bool on)
    {
        Bitmap b = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(b))
        {
            g.Clear(Color.Transparent);
            DrawMark(g, 0, 0, size, on, size >= 64);
        }
        return b;
    }
}
