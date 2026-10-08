using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace HollowKnightPet;

// Original vector fan art, drawn at runtime; no extracted game assets.
internal static class KnightArt
{
    public const int W = 132, H = 142, BodyLeft = 45, BodyTop = 29;
    public static Bitmap Render(double time, float velocity, float vertical, bool grounded, int facing, bool paused, bool debug)
    {
        var bitmap = new Bitmap(W,H,PixelFormat.Format32bppPArgb);
        using var g = Graphics.FromImage(bitmap);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Color.Transparent);
        float run = Math.Min(1,Math.Abs(velocity)/200);
        float wave = (float)Math.Sin(time*15)*run;
        float bob = grounded ? (float)Math.Sin(time*(run > .1 ? 30 : 3))*(run > .1 ? 1.2f : .7f) : 0;
        using var shadow = new SolidBrush(Color.FromArgb(grounded ? 50 : 18,10,17,30));
        g.FillEllipse(shadow,42,121,48,7);
        var original = g.Save();
        g.TranslateTransform(66,0); g.ScaleTransform(facing,1); g.TranslateTransform(-66,bob);
        using var outline = new Pen(Color.FromArgb(15,20,30),2.4f) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round };
        using var black = new SolidBrush(Color.FromArgb(14,19,29));
        using var limb = new Pen(Color.FromArgb(15,20,31),7) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        float leftFoot = grounded ? wave*8 : -6;
        float rightFoot = grounded ? -wave*8 : 5;
        g.DrawLine(limb,59,106,57+leftFoot,120);
        g.DrawLine(limb,72,107,75+rightFoot,120);
        // Nail behind the cloak.
        using(var nail = new GraphicsPath())
        {
            nail.AddPolygon([new PointF(85,89),new PointF(104,112),new PointF(108,124),new PointF(98,118),new PointF(79,95)]);
            using var metal = new SolidBrush(Color.FromArgb(151,167,184));
            g.FillPath(metal,nail); g.DrawPath(outline,nail);
            using var seam = new Pen(Color.FromArgb(213,225,231),1);
            g.DrawLine(seam,85,94,104,120);
        }
        float flow = grounded ? -run*7+wave*2 : Math.Clamp(vertical/100,-7,7);
        using(var cloak = new GraphicsPath())
        {
            cloak.StartFigure(); cloak.AddBezier(49,77,39,87,39+flow,103,28+flow,112);
            cloak.AddBezier(28+flow,112,41,108,45,118,52,115);
            cloak.AddBezier(52,115,58,111,63,123,68,116);
            cloak.AddBezier(68,116,75,110,81,118,87,110);
            cloak.AddBezier(87,110,83,96,83,83,78,77); cloak.CloseFigure();
            using var fabric = new LinearGradientBrush(new Point(40,80),new Point(80,117),Color.FromArgb(81,94,119),Color.FromArgb(30,39,61));
            g.FillPath(fabric,cloak); g.DrawPath(outline,cloak);
            using var fold = new Pen(Color.FromArgb(107,122,146),1.6f);
            g.DrawBezier(fold,52,84,50,94,48,103,43+flow,110);
            g.DrawBezier(fold,61,85,58,96,58,108,62,113);
            g.DrawBezier(fold,70,84,74,95,70,105,78,110);
        }
        using(var face = new GraphicsPath())
        {
            face.StartFigure();
            face.AddBezier(44,47,29,34,28,15,33,7);
            face.AddBezier(33,7,34,23,41,29,48,31);
            face.AddBezier(48,31,57,28,71,28,80,31);
            face.AddBezier(80,31,89,25,94,15,95,5);
            face.AddBezier(95,5,103,22,98,38,89,47);
            face.AddBezier(89,47,93,58,88,74,80,79);
            face.AddBezier(80,79,70,87,52,83,45,76);
            face.AddBezier(45,76,39,68,40,55,44,47); face.CloseFigure();
            using var shell = new LinearGradientBrush(new Point(42,28),new Point(85,84),Color.FromArgb(253,252,236),Color.FromArgb(205,221,223));
            g.FillPath(shell,face); g.DrawPath(outline,face);
            using var highlight = new Pen(Color.FromArgb(255,255,250),2);
            g.DrawBezier(highlight,46,44,51,37,72,35,81,40);
        }
        bool blink = grounded && time % 6.4 > 6.23;
        if (blink)
        {
            using var eyes = new Pen(Color.FromArgb(13,18,25),3) { StartCap=LineCap.Round,EndCap=LineCap.Round };
            g.DrawLine(eyes,49,59,58,61); g.DrawLine(eyes,72,61,81,59);
        }
        else { g.FillEllipse(black,48,50,12,20); g.FillEllipse(black,72,49,12,20); }
        g.Restore(original);
        if (paused)
        {
            using var badge = new SolidBrush(Color.FromArgb(230,29,40,58)); g.FillEllipse(badge,96,17,23,23);
            using var ink = new Pen(Color.FromArgb(227,230,210),3); g.DrawLine(ink,104,24,104,33); g.DrawLine(ink,111,24,111,33);
        }
        if (debug)
        {
            using var pen = new Pen(Color.FromArgb(210,87,233,194),1) { DashStyle=DashStyle.Dash };
            g.DrawRectangle(pen,BodyLeft,BodyTop,Physics.Width,Physics.Height);
        }
        return bitmap;
    }
}
