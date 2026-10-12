using System.Drawing.Imaging;

namespace HollowKnightPet;

internal static class AbilityPreview
{
    public static int Write(string directory)
    {
        Directory.CreateDirectory(directory);
        using var sprites=new SpriteArt();using var dashArt=new AbilityArt();using var diveArt=new AbilityArt();
        var dash=new AbilityState();var dive=new AbilityState();
        var runner=new Physics();runner.Reset(210,226);runner.Grounded=true;
        var diver=new Physics();diver.Reset(450,45);
        var area=new RectangleF(0,0,960,320);Platform[] floor=[new(-1,new RectangleF(0,320,960,100),true)];
        using var font=new Font("Segoe UI",15,FontStyle.Bold);using var small=new Font("Segoe UI",11);
        var watch=System.Diagnostics.Stopwatch.StartNew();
        for(int i=0;i<108;i++)
        {
            double now=i/60d;
            if(i==12)dash.StartDash(runner,1);
            if(i==6)dive.StartDive(diver,1);
            for(int j=0;j<2;j++)
            {
                dash.Step(1f/120,runner,0,false,floor,area,false,null,null,[],now);
                dive.Step(1f/120,diver,0,false,floor,area,false,null,null,[],now);
                dashArt.UpdateTrail(1f/120,runner,dash);diveArt.UpdateTrail(1f/120,diver,dive);
            }
            using var image=new Bitmap(960,740,PixelFormat.Format32bppPArgb);using var g=Graphics.FromImage(image);
            g.Clear(Color.FromArgb(28,34,47));
            g.DrawString("HOLLOW KNIGHT DESKTOP PET  /  v3.3",font,Brushes.White,22,12);
            Draw(runner,dash,dashArt,45,"ENTER  /  SHADE DASH");
            Draw(diver,dive,diveArt,400,"AIR + J  /  DESCENDING DARK");
            image.Save(Path.Combine(directory,$"frame-{i:000}.png"),ImageFormat.Png);
            if(i is 18 or 30 or 42)image.Save(Path.Combine(directory,$"preview-{i}.png"),ImageFormat.Png);
            void Draw(Physics p,AbilityState state,AbilityArt art,int top,string label)
            {
                g.DrawString(label,small,Brushes.LightGray,24,top+10);
                using var line=new Pen(Color.FromArgb(95,127,153),2);g.DrawLine(line,20,top+320,940,top+320);
                using var sprite=sprites.Render(now,p,1,false,false);
                using var normal=CombatArt.Hero(sprite,new AttackState(),now,true);
                using var frame=art.Compose(normal,p,state,true,false);
                g.DrawImageUnscaled(frame,(int)Math.Round(p.X+21-(AbilityArt.HeroX+66)),top+(int)Math.Round(p.Y+94-(AbilityArt.HeroY+123)));
            }
        }
        File.WriteAllText(Path.Combine(directory,"render.json"),System.Text.Json.JsonSerializer.Serialize(new {frames=108,elapsedMs=watch.Elapsed.TotalMilliseconds,width=960,height=740,physicsHz=120,previewFps=60}));
        return 0;
    }
}
