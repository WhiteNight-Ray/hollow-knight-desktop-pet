using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Reflection;

namespace HollowKnightPet;

internal sealed class SpriteArt : IDisposable
{
    readonly Dictionary<string,Bitmap[]> clips = [];
    string state = "idle_still";
    double started;
    bool wasGrounded = true;
    public int FrameCount => clips.Values.Sum(x => x.Length);
    public string State => state;

    public SpriteArt()
    {
        var assembly = Assembly.GetExecutingAssembly();
        foreach (string clip in new[] { "idle_still", "idle_to_run", "run", "run_to_idle", "jump", "fall", "land", "wall_slide", "wall_jump", "nail_slash" })
        {
            var frames = new List<Bitmap>();
            foreach (string name in assembly.GetManifestResourceNames().Where(n => n.Contains(".Assets." + clip + ".")).Order(StringComparer.Ordinal))
            {
                using var stream = assembly.GetManifestResourceStream(name)!;
                using var image = Image.FromStream(stream);
                frames.Add(new Bitmap(image));
            }
            if (frames.Count == 0) throw new InvalidDataException("Missing embedded animation: " + clip);
            clips.Add(clip,frames.ToArray());
        }
    }

    void Change(string next, double now) { if (next != state) { state = next; started = now; } }
    public Bitmap Render(double now, Physics p, int facing, bool paused, bool debug,double attackAge=-1)
    {
        double age = now-started;
        if (p.AttachedSide != 0) Change("wall_slide",now);
        else if (!p.Grounded)
        {
            if (p.WallJumping) Change("wall_jump",now);
            else Change(p.VY < 0 ? "jump" : "fall",now);
        }
        else if (Math.Abs(p.VX) > 1)
        {
            if (state is not "run" and not "idle_to_run") Change("idle_to_run",now);
            else if (state == "idle_to_run" && age >= clips[state].Length/12d) Change("run",now);
        }
        else if (!wasGrounded) Change("land",now);
        else if (state is "run" or "idle_to_run") Change("run_to_idle",now);
        else if (state == "land" && age < clips[state].Length/12d) { }
        else if (state == "run_to_idle" && age < clips[state].Length/24d) { }
        else Change("idle_still",now);
        wasGrounded=p.Grounded;
        return attackAge>=0 ? RenderClip("nail_slash",attackAge,facing,0,paused,debug) : RenderClip(state,now-started,facing,p.AttachedSide,paused,debug);
    }

    internal Bitmap RenderClip(string clip,double age,int facing,int attachedSide=0,bool paused=false,bool debug=false)
    {
        var frames = clips[clip];
        int fps = clip == "run_to_idle" ? 24 : clip is "jump" or "fall" or "wall_jump" or "nail_slash" ? 16 : 12;
        int frame = Math.Max(0,(int)(age*fps));
        bool loop = clip is "idle_still" or "run" or "fall" or "wall_slide";
        frame = loop ? frame % frames.Length : Math.Min(frame,frames.Length-1);
        if(clip=="nail_slash") frame=Math.Min(frame,3);
        var image = frames[frame];
        var bitmap = new Bitmap(KnightArt.W,KnightArt.H,PixelFormat.Format32bppPArgb);
        using var g=Graphics.FromImage(bitmap);
        g.Clear(Color.Transparent);
        g.InterpolationMode=InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode=PixelOffsetMode.HighQuality;
        g.SmoothingMode=SmoothingMode.AntiAlias;
        var transform=g.Save();
        const float scale=.88f;
        bool wall=clip=="wall_slide";
        // Flip the attached pose relative to v2, on both screen and window edges.
        int flip=wall ? -(attachedSide == 0 ? facing : attachedSide) : -facing;
        g.TranslateTransform(66,0); g.ScaleTransform(flip,1);
        float anchorX=174.5f;
        float anchorY=wall ? 185 : 183;
        g.DrawImage(image,new RectangleF(-anchorX*scale,123-anchorY*scale,image.Width*scale,image.Height*scale));
        g.Restore(transform);
        if (paused)
        {
            using var badge=new SolidBrush(Color.FromArgb(230,29,40,58)); g.FillEllipse(badge,96,17,23,23);
            using var ink=new Pen(Color.FromArgb(227,230,210),3); g.DrawLine(ink,104,24,104,33); g.DrawLine(ink,111,24,111,33);
        }
        if (debug)
        {
            using var pen=new Pen(Color.FromArgb(210,87,233,194),1) { DashStyle=DashStyle.Dash };
            g.DrawRectangle(pen,KnightArt.BodyLeft,KnightArt.BodyTop,Physics.Width,Physics.Height);
        }
        return bitmap;
    }

    public void Dispose() { foreach(var frame in clips.Values.SelectMany(x=>x)) frame.Dispose(); clips.Clear(); }
}
