using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Text.Json;

namespace HollowKnightPet;

internal sealed class AbilityArt : IDisposable
{
    public const int Width=720,Height=480,HeroX=294,HeroY=230;
    const float SpriteScale=.88f;
    readonly Dictionary<string,Bitmap[]> sprites=[];
    readonly Dictionary<string,EffectFrame[]> effects=[];
    readonly List<(float X,float Y,float Age,int Frame,int Facing)> trail=[];
    float trailClock;
    public int FrameCount=>sprites.Values.Sum(c=>c.Length)+effects.Values.Sum(c=>c.Length);
    sealed record EffectFrame(Bitmap Image,int Left,int Top);
    sealed class FrameInfo
    {
        public string file {get;set;}="";
        public int left {get;set;}
        public int top {get;set;}
    }
    public AbilityArt()
    {
        var assembly=typeof(AbilityArt).Assembly;
        foreach(string clip in new[] {"shadow_dash","quake","dark_dive","shadow_trail"})
            sprites[clip]=assembly.GetManifestResourceNames().Where(n=>n.Contains(".Assets."+clip+".") && n.EndsWith(".png"))
                .Order(StringComparer.Ordinal).Select(Read).ToArray();
        using var manifest=assembly.GetManifestResourceStream("HollowKnightPet.Assets.ability-effects.json")!;
        var metadata=JsonSerializer.Deserialize<Dictionary<string,FrameInfo[]>>(manifest)!;
        foreach(var (clip,frames) in metadata)
            effects[clip]=frames.Select(f=>new EffectFrame(Read("HollowKnightPet.Assets."+clip+"."+f.file),f.left,f.top)).ToArray();
        if(sprites.Any(c=>c.Value.Length==0) || effects.Any(c=>c.Value.Length==0)) throw new InvalidDataException("Missing ability animation.");
        Bitmap Read(string name)
        {
            using var stream=assembly.GetManifestResourceStream(name)!;
            using var image=Image.FromStream(stream);
            var cache=new Bitmap(image.Width,image.Height,PixelFormat.Format32bppPArgb);
            // Explicit pixel coordinates prevent GDI+ from applying the monitor's
            // DPI scale while copying PNGs into a newly allocated bitmap.
            cache.SetResolution(96,96);
            using var g=Graphics.FromImage(cache);
            g.DrawImage(image,new Rectangle(0,0,image.Width,image.Height),0,0,image.Width,image.Height,GraphicsUnit.Pixel);
            return cache;
        }
    }
    public void UpdateTrail(float dt,Physics p,AbilityState state)
    {
        if(!state.Busy) {trail.Clear();trailClock=0;return;}
        for(int i=trail.Count-1;i>=0;i--)
        {
            var t=trail[i];t.Age+=dt;
            if(t.Age>.14f) trail.RemoveAt(i);else trail[i]=t;
        }
        trailClock+=dt;
        if(state.Phase==AbilityPhase.ShadowDash && trailClock>=1f/60)
        {
            trailClock=0;trail.Add((p.X,p.Y,0,Math.Clamp((int)(state.Age/DashVisualDuration*9),0,8),state.Facing));
        }
    }
    const float DashVisualDuration=AbilityState.DashDuration;
    public Bitmap Compose(Bitmap normal,Physics p,AbilityState state,bool active,bool debug)
    {
        var canvas=new Bitmap(Width,Height,PixelFormat.Format32bppPArgb);
        using var g=Graphics.FromImage(canvas);
        g.InterpolationMode=InterpolationMode.HighQualityBicubic;g.PixelOffsetMode=PixelOffsetMode.HighQuality;g.SmoothingMode=SmoothingMode.AntiAlias;
        float center=HeroX+66,feet=HeroY+123;
        foreach(var t in trail)
            DrawSprite(g,"shadow_dash",t.Frame,center+t.X-p.X,feet+t.Y-p.Y,t.Facing,.32f*(1-t.Age/.14f));
        if(state.Phase==AbilityPhase.DiveFall)
            DrawEffect(g,"dark_streak",(int)(state.Age*24)%3,center,feet-95,.42f,.85f);
        if(state.Phase==AbilityPhase.DiveImpact)
        {
            float x=center+state.ImpactX-(p.X+Physics.Width/2),y=feet+state.ImpactY-(p.Y+Physics.Height);
            float fade=Math.Clamp((AbilityState.DiveRecovery-state.Age)/.12f,0,1);
            DrawEffect(g,"dark_wave",(int)(state.Age*24),x,y-66*1.05f,1.05f,fade);
            DrawEffect(g,"dark_blast",(int)(Math.Max(0,state.Age-.06f)*20),x,y-262*.78f,.78f,fade);
            if(state.Age<.3f) DrawEffect(g,"dark_impact",(int)(state.Age*24),x,y-208*.40f,.40f,fade);
        }
        if(!state.Busy) g.DrawImageUnscaled(normal,HeroX-CombatArt.Margin,HeroY);
        else
        {
            switch(state.Phase)
            {
                case AbilityPhase.ShadowDash:
                    DrawSprite(g,"shadow_trail",Math.Min((int)(state.Age*26),6),center-state.Facing*20,feet-5,state.Facing,.82f);
                    DrawSprite(g,"shadow_dash",Math.Clamp((int)(state.Age/AbilityState.DashDuration*9),0,8),center,feet,state.Facing);break;
                case AbilityPhase.DashRecovery:
                    DrawSprite(g,"shadow_dash",Math.Min(9+(int)(state.Age*25),11),center,feet,state.Facing);break;
                case AbilityPhase.DiveWindup:
                    DrawSprite(g,"quake",Math.Min((int)(state.Age*20),4),center,feet,state.Facing);break;
                case AbilityPhase.DiveFall:
                    DrawSprite(g,"dark_dive",1+(int)(state.Age*20)%3,center,feet,state.Facing);break;
                case AbilityPhase.DiveImpact:
                    DrawSprite(g,"dark_dive",Math.Min(5+(int)(state.Age*24),18),center,feet,state.Facing);break;
            }
            if(active) {using var dot=new SolidBrush(Color.FromArgb(215,108,238,208));g.FillEllipse(dot,HeroX+105,HeroY+20,7,7);}
            if(debug) {using var pen=new Pen(Color.FromArgb(210,87,233,194));g.DrawRectangle(pen,HeroX+KnightArt.BodyLeft,HeroY+KnightArt.BodyTop,Physics.Width,Physics.Height);}
        }
        return canvas;
    }
    void DrawSprite(Graphics g,string clip,int index,float x,float feet,int facing,float alpha=1)
    {
        var frame=sprites[clip][Math.Clamp(index,0,sprites[clip].Length-1)];
        var transform=g.Save();g.TranslateTransform(x,feet);g.ScaleTransform(-facing,1);
        float anchorX=clip=="shadow_trail"?171:174.5f,anchorY=clip=="shadow_trail"?143:183;
        Draw(g,frame,new RectangleF(-anchorX*SpriteScale,-anchorY*SpriteScale,frame.Width*SpriteScale,frame.Height*SpriteScale),alpha);
        g.Restore(transform);
    }
    void DrawEffect(Graphics g,string clip,int index,float x,float y,float scale,float alpha)
    {
        var frames=effects[clip];if(index<0 || index>=frames.Length)return;
        var frame=frames[index];Draw(g,frame.Image,new RectangleF(x+frame.Left*scale,y+frame.Top*scale,frame.Image.Width*scale,frame.Image.Height*scale),alpha);
    }
    static void Draw(Graphics g,Bitmap frame,RectangleF destination,float alpha)
    {
        if(alpha>=.999f) {g.DrawImage(frame,destination,new RectangleF(0,0,frame.Width,frame.Height),GraphicsUnit.Pixel);return;}
        using var attr=new ImageAttributes();attr.SetColorMatrix(new ColorMatrix {Matrix33=Math.Clamp(alpha,0,1)});
        PointF[] points=[new(destination.Left,destination.Top),new(destination.Right,destination.Top),new(destination.Left,destination.Bottom)];
        g.DrawImage(frame,points,new RectangleF(0,0,frame.Width,frame.Height),GraphicsUnit.Pixel,attr);
    }
    public void ClearTrail() {trail.Clear();trailClock=0;}
    public void Dispose()
    {
        foreach(var frame in sprites.Values.SelectMany(x=>x))frame.Dispose();
        foreach(var frame in effects.Values.SelectMany(x=>x))frame.Image.Dispose();
        sprites.Clear();effects.Clear();trail.Clear();
    }
}
