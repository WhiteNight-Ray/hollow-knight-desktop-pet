using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace HollowKnightPet;

internal sealed class AbilityArt : IDisposable
{
    public const int Width=720,Height=480,HeroX=294,HeroY=230;
    const float SpriteScale=.88f;
    // These times are in the original clip's clock; the ability keeps its
    // existing 1.1x playback and recovery duration.
    static readonly float[] DiveReturnTimes=[.375f,.415f,.45f,.475f,.515f,.58f];
    static readonly PointF[] DiveReturnEyes=[new(173.3f,164),new(173,165.8f),new(173.4f,163.8f),new(162,149),new(163,127),new(165,123)];
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
        foreach(string clip in new[] {"shadow_dash","quake","dark_dive","shadow_trail","double_jump","shriek_pose"})
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
    public Bitmap Compose(Bitmap normal,Physics p,AbilityState state,bool active,bool debug,int facing=1)
    {
        var canvas=new Bitmap(Width,Height,PixelFormat.Format32bppPArgb);
        using var g=Graphics.FromImage(canvas);
        g.InterpolationMode=InterpolationMode.HighQualityBicubic;g.PixelOffsetMode=PixelOffsetMode.HighQuality;g.SmoothingMode=SmoothingMode.AntiAlias;
        float center=HeroX+66,feet=HeroY+123;
        float impactAge=state.Age*AbilityState.DiveImpactPlaybackRate;
        foreach(var t in trail)
            DrawSprite(g,"shadow_dash",t.Frame,center+t.X-p.X,feet+t.Y-p.Y,t.Facing,.32f*(1-t.Age/.14f));
        if(state.Phase==AbilityPhase.DiveFall)
            DrawEffect(g,"dark_streak",(int)(state.Age*24)%3,center,feet-95,.42f,.85f);
        if(state.Phase==AbilityPhase.DiveImpact)
        {
            float x=center+state.ImpactX-(p.X+Physics.Width/2),y=feet+state.ImpactY-(p.Y+Physics.Height);
            float fade=Math.Clamp((AbilityState.DiveRecovery-state.Age)*AbilityState.DiveImpactPlaybackRate/.12f,0,1);
            DrawEffect(g,"dark_wave",(int)(impactAge*24),x,y-66*1.05f,1.05f,fade);
            DrawEffect(g,"dark_blast",(int)(Math.Max(0,impactAge-.06f)*20),x,y-262*.78f,.78f,fade);
            if(impactAge<.3f) DrawEffect(g,"dark_impact",(int)(impactAge*24),x,y-208*.40f,.40f,fade);
        }
        if(state.Phase is AbilityPhase.ShriekBurst or AbilityPhase.ShriekRecovery)
        {
            float age=state.Phase==AbilityPhase.ShriekBurst?state.Age:AbilityState.ShriekBurst+state.Age;
            float fade=state.Phase==AbilityPhase.ShriekRecovery?Math.Clamp(1-state.Age/AbilityState.ShriekRecovery,0,1):1;
            DrawEffect(g,"shriek_base",Math.Min((int)(age*15),9),center,feet-87*.8f,.8f,fade);
            DrawEffect(g,"shriek_blast",Math.Min((int)(age*20),12),center,feet-140,.9f,fade);
        }
        if(!state.Busy && p.DoubleJumping)
        {
            DrawEffect(g,"double_wings",(int)(p.DoubleJumpAge*16),center,feet-65,.55f,1);
            DrawSprite(g,"double_jump",(int)(p.DoubleJumpAge*16),center,feet,facing);
            DrawMarkers();
        }
        else if(!state.Busy) g.DrawImageUnscaled(normal,HeroX-CombatArt.Margin,HeroY);
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
                    DrawDiveReturn(g,normal,impactAge,center,feet,state.Facing);break;
                case AbilityPhase.ShriekWindup:
                    DrawSprite(g,"shriek_pose",Math.Min((int)(state.Age*20),1),center,feet,state.Facing);break;
                case AbilityPhase.ShriekBurst:
                    DrawSprite(g,"shriek_pose",2+(int)(state.Age*20)%6,center,feet,state.Facing);break;
                case AbilityPhase.ShriekRecovery:
                    DrawSprite(g,"shriek_pose",8,center,feet,state.Facing);break;
            }
            DrawMarkers();
        }
        return canvas;
        void DrawMarkers()
        {
            if(active) {using var dot=new SolidBrush(Color.FromArgb(215,108,238,208));g.FillEllipse(dot,HeroX+105,HeroY+20,7,7);}
            if(debug) {using var pen=new Pen(Color.FromArgb(210,87,233,194));g.DrawRectangle(pen,HeroX+KnightArt.BodyLeft,HeroY+KnightArt.BodyTop,Physics.Width,Physics.Height);}
        }
    }
    void DrawSprite(Graphics g,string clip,int index,float x,float feet,int facing,float alpha=1)
    {
        var frame=sprites[clip][Math.Clamp(index,0,sprites[clip].Length-1)];
        DrawSpriteFrame(g,frame,clip,x,feet,facing,alpha);
    }
    void DrawDiveReturn(Graphics g,Bitmap normal,float age,float x,float feet,int facing)
    {
        if(age<DiveReturnTimes[0])
        {
            DrawSprite(g,"dark_dive",5+(int)(age*24),x,feet,facing);return;
        }
        int segment=0;
        while(segment<DiveReturnTimes.Length-2 && age>=DiveReturnTimes[segment+1])segment++;
        float progress=Math.Clamp((age-DiveReturnTimes[segment])/(DiveReturnTimes[segment+1]-DiveReturnTimes[segment]),0,1);
        progress=progress*progress*(3-2*progress);
        int index=14+segment;
        // Register the eye positions while the body rises, rather than fading
        // between two faces at different heights. Keep the ground contact fixed.
        var eye=DiveReturnEyes[segment];var nextEye=DiveReturnEyes[segment+1];
        float dx=(nextEye.X-eye.X)*SpriteScale*-facing,dy=(nextEye.Y-eye.Y)*SpriteScale;
        const int top=60;
        using var from=new Bitmap(normal.Width,normal.Height+top,PixelFormat.Format32bppPArgb);
        using var to=new Bitmap(from.Width,from.Height,PixelFormat.Format32bppPArgb);
        using(var layer=Graphics.FromImage(from))
        {
            Configure(layer);
            DrawSprite(layer,"dark_dive",index,x-HeroX+CombatArt.Margin+dx*progress,feet-HeroY+top+dy*progress,facing);
        }
        using(var layer=Graphics.FromImage(to))
        {
            Configure(layer);
            if(index<18)
                DrawSprite(layer,"dark_dive",index+1,x-HeroX+CombatArt.Margin-dx*(1-progress),feet-HeroY+top-dy*(1-progress),facing);
            else
                Draw(layer,normal,new RectangleF(-dx*(1-progress),top-dy*(1-progress),normal.Width,normal.Height),1);
        }
        using var frame=Blend(from,to,progress);
        var clip=g.Save();g.SetClip(new RectangleF(0,0,Width,feet+1));
        g.DrawImageUnscaled(frame,HeroX-CombatArt.Margin,HeroY-top);g.Restore(clip);
        void Configure(Graphics layer)
        {layer.InterpolationMode=g.InterpolationMode;layer.PixelOffsetMode=g.PixelOffsetMode;layer.SmoothingMode=g.SmoothingMode;}
    }
    static Bitmap Blend(Bitmap from,Bitmap to,float progress)
    {
        var result=new Bitmap(from.Width,from.Height,PixelFormat.Format32bppPArgb);
        var bounds=new Rectangle(0,0,from.Width,from.Height);
        BitmapData? first=null,second=null,output=null;
        try
        {
            first=from.LockBits(bounds,ImageLockMode.ReadOnly,PixelFormat.Format32bppPArgb);
            second=to.LockBits(bounds,ImageLockMode.ReadOnly,PixelFormat.Format32bppPArgb);
            output=result.LockBits(bounds,ImageLockMode.WriteOnly,PixelFormat.Format32bppPArgb);
            int bytes=first.Stride*from.Height;
            var a=new byte[bytes];var b=new byte[bytes];
            Marshal.Copy(first.Scan0,a,0,bytes);Marshal.Copy(second.Scan0,b,0,bytes);
            int weight=Math.Clamp((int)Math.Round(progress*256),0,256);
            // Interpolate premultiplied color and alpha together. Drawing two
            // translucent sprites over each other would make the body fade out.
            for(int i=0;i<bytes;i++)a[i]=(byte)((a[i]*(256-weight)+b[i]*weight+128)>>8);
            Marshal.Copy(a,0,output.Scan0,bytes);
        }
        finally
        {
            if(first!=null)from.UnlockBits(first);
            if(second!=null)to.UnlockBits(second);
            if(output!=null)result.UnlockBits(output);
        }
        return result;
    }
    static void DrawSpriteFrame(Graphics g,Bitmap frame,string clip,float x,float feet,int facing,float alpha)
    {
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
