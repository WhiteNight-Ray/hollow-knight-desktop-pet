using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Reflection;

namespace HollowKnightPet;

internal static class CombatArt
{
    public const int Margin=80;
    public static Bitmap Hero(Bitmap sprite,AttackState attack,double now,bool active)
    {
        var bitmap=new Bitmap(sprite.Width+Margin*2,sprite.Height,PixelFormat.Format32bppPArgb);
        using var g=Graphics.FromImage(bitmap); g.SmoothingMode=SmoothingMode.AntiAlias;
        g.DrawImageUnscaled(sprite,Margin,0);
        if(active)
        {
            using var dot=new SolidBrush(Color.FromArgb(215,108,238,208)); g.FillEllipse(dot,Margin+105,20,7,7);
        }
        if(attack.Visible(now))
        {
            float progress=(float)(attack.Age(now)/AttackState.Duration);
            int alpha=(int)(240*(1-progress));
            float center=Margin+KnightArt.BodyLeft+Physics.Width/2;
            g.TranslateTransform(center,KnightArt.BodyTop+61); g.ScaleTransform(attack.Facing,1);
            using var glow=new Pen(Color.FromArgb(alpha/3,160,219,255),13) {StartCap=LineCap.Round,EndCap=LineCap.Round};
            using var blade=new Pen(Color.FromArgb(alpha,245,253,255),5) {StartCap=LineCap.Round,EndCap=LineCap.Round};
            float sweep=20+progress*150;
            g.DrawArc(glow,-30,-39,135,78,-70,sweep); g.DrawArc(blade,-30,-39,135,78,-70,sweep);
        }
        return bitmap;
    }
}

internal sealed class MonsterArt : IDisposable
{
    readonly Bitmap sheet;
    static readonly Rectangle[] walk=[new(6,24,110,81),new(124,24,112,81),new(243,24,111,81),new(362,24,113,81)];
    public MonsterArt()
    {
        using var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("HollowKnightPet.Assets.crawlid.sheet.png")!;
        using var source=Image.FromStream(stream); sheet=new Bitmap(source);
    }
    public Bitmap Render(Monster monster,double now)
    {
        var image=new Bitmap(100,80,PixelFormat.Format32bppPArgb);
        using var g=Graphics.FromImage(image);
        g.InterpolationMode=InterpolationMode.HighQualityBicubic; g.SmoothingMode=SmoothingMode.AntiAlias;
        var transform=g.Save(); g.TranslateTransform(50,0); g.ScaleTransform(-monster.Facing,1);
        int frame=(int)((now-monster.Born)*10)%walk.Length;
        using var attr=new ImageAttributes();
        float fade=monster.Dead?Math.Clamp(1-(float)((now-monster.DeadAt)/.3),0,1):1;
        var color=new ColorMatrix {Matrix33=fade};
        if(now<monster.HitUntil) { color.Matrix40=.65f; color.Matrix41=.35f; color.Matrix42=.1f; }
        attr.SetColorMatrix(color);
        var src=walk[Math.Max(0,frame)];
        g.DrawImage(sheet,new Rectangle(-38,20,76,55),src.X,src.Y,src.Width,src.Height,GraphicsUnit.Pixel,attr);
        g.Restore(transform);
        if(monster.HP==1)
        {
            using var bg=new SolidBrush(Color.FromArgb(200,27,32,42));g.FillRectangle(bg,30,12,40,4);
            using var hp=new SolidBrush(Color.FromArgb(240,247,172,73));g.FillRectangle(hp,30,12,20,4);
        }
        if(monster.Dead)
        {
            float radius=(float)(now-monster.DeadAt)*90;
            using var spark=new SolidBrush(Color.FromArgb((int)(220*fade),254,191,107));
            for(int i=0;i<6;i++)
            { double angle=i*Math.PI/3; g.FillEllipse(spark,47+(float)Math.Cos(angle)*radius,42+(float)Math.Sin(angle)*radius,5,5); }
        }
        return image;
    }
    public void Dispose()=>sheet.Dispose();
}

internal sealed class MonsterForm : Form
{
    public MonsterForm() { FormBorderStyle=FormBorderStyle.None; ShowInTaskbar=false;AutoScaleMode=AutoScaleMode.None;ClientSize=new Size(100,80);StartPosition=FormStartPosition.Manual; }
    protected override bool ShowWithoutActivation=>true;
    protected override CreateParams CreateParams { get { var cp=base.CreateParams;cp.ExStyle|=0x80000|0x80|0x20|0x08000000;return cp; } }
    public void ShowPassive()
    {
        // Form.Show can change the active WinForms window even with ShowWithoutActivation.
        // Show the native layer without activating it or modifying the keyboard focus.
        if(!Native.SetWindowPos(Handle,-1,0,0,0,0,0x01|0x02|0x10|0x40))
            throw new System.ComponentModel.Win32Exception(System.Runtime.InteropServices.Marshal.GetLastWin32Error());
    }
    protected override void WndProc(ref Message m)
    {
        if(m.Msg==0x21) { m.Result=3;return; }
        if(m.Msg==0x84) { m.Result=-1;return; }
        base.WndProc(ref m);
    }
    public void Present(Monster monster,double now,MonsterArt art)
    {
        using var image=art.Render(monster,now);
        Native.Present(Handle,image,(int)monster.X-19,(int)(monster.Y+Monster.Height)-74);
    }
}
