namespace HollowKnightPet;

internal static class AbilityTests
{
    public static void Run(Action<string,Action> test)
    {
        var area=new RectangleF(0,0,1600,900);
        var floor=new Platform(-1,new RectangleF(0,900,1600,100),true);
        void Assert(bool ok,string why) {if(!ok)throw new Exception(why);}
        void Step(AbilityState a,Physics p,int ticks,IReadOnlyList<Platform>? platforms=null,IReadOnlyList<Monster>? monsters=null,int direction=0,ScreenEdges? edges=null)
        {for(int i=0;i<ticks;i++)a.Step(1f/120,p,direction,false,platforms??[floor],area,false,edges,null,monsters??[],i/120d);}
        test("shade dash travels 200 pixels in .25 seconds and preserves aerial height",()=> {
            var p=new Physics();p.Reset(300,300);p.VY=-500;var a=new AbilityState();
            Assert(a.StartDash(p,1),"dash rejected");Step(a,p,30);
            Assert(Math.Abs(p.X-500)<.02 && p.Y==300 && a.Phase==AbilityPhase.DashRecovery,"distance, height or duration incorrect");
            Step(a,p,15);Assert(!a.Busy && p.Y>300,"gravity did not resume");
        });
        test("shade dash honors facing direction and cooldown",()=> {
            var p=new Physics();p.Reset(700,806);p.Grounded=true;var a=new AbilityState();
            Assert(a.StartDash(p,1,-1),"left dash rejected");Step(a,p,45);
            Assert(a.Facing==-1 && Math.Abs(p.X-500)<.02 && !a.StartDash(p,1),"direction or cooldown lost");
            Step(a,p,150);Assert(a.StartDash(p,1),"cooldown never ended");
        });
        test("shade dash collides with solid window sides without tunneling",()=> {
            var p=new Physics {SolidWindows=true};p.Reset(300,300);var a=new AbilityState();a.StartDash(p,1);
            Step(a,p,30,[new Platform(19,new RectangleF(430,200,250,500)),floor]);
            Assert(p.X==430-Physics.Width && p.Y==300,"dash crossed wall");
        });
        test("shade dash clamps at desktop edge without automatic attachment",()=> {
            var p=new Physics();p.Reset(1450,300);var a=new AbilityState();a.StartDash(p,1);
            Step(a,p,30,edges:new ScreenEdges(0,1600));
            Assert(p.X==1600-Physics.Width && p.AttachedSide==0,"edge dash attached or left desktop");
        });
        test("shade dash leaves an attached wall in the away direction",()=> {
            var p=new Physics();p.Reset(20,300);p.Step(1f/120,-1,false,[floor],area,false,new ScreenEdges(0,1600));
            Assert(p.AttachedSide==-1,"test wall did not attach");var a=new AbilityState();a.StartDash(p,-1);Step(a,p,30);
            Assert(a.Facing==1 && p.AttachedSide==0 && p.X>200,"dash failed to release wall");
        });
        test("shade dash hits each monster once with a swept hitbox",()=> {
            var p=new Physics();p.Reset(300,806);p.Grounded=true;var a=new AbilityState();
            var m=new Monster {Id=1,X=410,Y=857};var far=new Monster {Id=2,X=850,Y=857};a.StartDash(p,1);Step(a,p,30,monsters:[m,far]);
            Assert(m.HP==1 && far.HP==2,"dash repeated damage or hit a distant monster");
        });
        test("descending dark requires air and suspends motion for windup",()=> {
            var p=new Physics();p.Reset(300,806);p.Grounded=true;var a=new AbilityState();
            Assert(!a.StartDive(p,1),"ground cast accepted");p.Reset(300,300);p.VY=-500;
            Assert(a.StartDive(p,1),"air cast rejected");Step(a,p,30,direction:1);
            Assert(p.X==300 && p.Y==300 && a.Phase==AbilityPhase.DiveFall,"windup did not freeze motion");
            Step(a,p,1);Assert(Math.Abs(p.VY-1600)<.01 && p.Y>300,"dive speed incorrect");
        });
        test("descending dark lands on nearest window roof at high fall speed",()=> {
            var p=new Physics();p.Reset(300,380);var a=new AbilityState();a.StartDive(p,1);
            var roof=new Platform(7,new RectangleF(250,500,400,220));Step(a,p,35,[floor,roof]);
            Assert(a.Phase==AbilityPhase.DiveImpact && p.Support==7 && p.Y==406,"dive tunneled through roof");
        });
        test("descending dark reaches taskbar and finishes recovery",()=> {
            var p=new Physics();p.Reset(300,300);var a=new AbilityState();a.StartDive(p,-1);Step(a,p,70);
            Assert(a.Phase==AbilityPhase.DiveImpact && p.Grounded && p.Y==806 && a.ImpactY==900,"missed taskbar");
            Step(a,p,80);Assert(!a.Busy && a.StartDive(p,-1)==false,"recovery or grounded cast incorrect");
        });
        test("descending dark deals fall and radial impact damage once per phase",()=> {
            var p=new Physics();p.Reset(300,300);var a=new AbilityState();
            var below=new Monster {Id=1,X=300,Y=857,HP=8};var side=new Monster {Id=2,X=430,Y=857,HP=8};var far=new Monster {Id=3,X=800,Y=857,HP=8};
            a.StartDive(p,1);Step(a,p,110,monsters:[below,side,far]);
            Assert(below.HP==5 && side.HP==6 && far.HP==8,"damage was repeated or splash range incorrect");
        });
        test("ability interruption restores ordinary motion and keeps cooldown",()=> {
            var p=new Physics();p.Reset(300,300);var a=new AbilityState();a.StartDash(p,1);Step(a,p,5);
            float x=p.X;a.Cancel();p.CancelInput();Step(a,p,5);
            Assert(!a.Busy && p.X==x && p.Y>300 && !a.StartDash(p,1),"cancelled dash continued or bypassed cooldown");
        });
        test("special action priority prevents conflicting casts",()=> {
            var p=new Physics();p.Reset(300,300);var a=new AbilityState();a.StartDash(p,1);
            Assert(!a.StartDive(p,1) && !a.StartDash(p,-1),"conflicting dash started");Step(a,p,30);
            Assert(a.StartDive(p,1) && !a.StartDash(p,-1),"dash recovery did not allow dive or dive allowed dash");
        });
        test("Enter produces one dash request and focus loss clears it",()=> {
            var input=new PetControls();input.KeyDown(Keys.Enter);Assert(!input.TakeDash(),"inactive Enter accepted");
            input.Activate();input.KeyDown(Keys.Enter);Assert(input.TakeDash(),"Enter lost");
            input.KeyDown(Keys.Enter);Assert(!input.TakeDash(),"repeat Enter recast");
            input.KeyUp(Keys.Enter);input.KeyDown(Keys.Enter);input.Stop();Assert(!input.TakeDash(),"dash survived blur");
        });
        test("all special animation phases and directions fit a transparent canvas",()=> {
            using var art=new AbilityArt();using var normal=new Bitmap(292,142);
            Assert(art.FrameCount==82,"missing original ability frames");
            foreach(int facing in new[] {-1,1})
            {
                var p=new Physics();p.Reset(500,300);var a=new AbilityState();a.StartDash(p,facing);
                void Verify()
                {
                    using var frame=art.Compose(normal,p,a,true,false);
                    Assert(frame.Width==720 && frame.Height==480,"canvas size changed");
                    for(int x=0;x<frame.Width;x+=4)Assert(frame.GetPixel(x,0).A==0 && frame.GetPixel(x,479).A==0,"vertical effect clipped");
                    for(int y=0;y<frame.Height;y+=4)Assert(frame.GetPixel(0,y).A==0 && frame.GetPixel(719,y).A==0,"horizontal effect clipped");
                }
                for(int i=0;i<44;i++){Verify();Step(a,p,1);art.UpdateTrail(1f/120,p,a);}
                a.Cancel();art.ClearTrail();p.Reset(500,300);a.StartDive(p,facing);
                for(int i=0;i<140;i++){Verify();Step(a,p,1);art.UpdateTrail(1f/120,p,a);}
            }
        });
        test("ability PNG decoding keeps native pixel scale on high DPI displays",()=> {
            using var art=new AbilityArt();using var normal=new Bitmap(292,142);
            var p=new Physics();p.Reset(500,300);var a=new AbilityState();a.StartDive(p,1);
            using var frame=art.Compose(normal,p,a,false,false);
            int left=frame.Width,top=frame.Height,right=0,bottom=0;
            for(int y=0;y<frame.Height;y++)for(int x=0;x<frame.Width;x++)
                if(frame.GetPixel(x,y).A>40){left=Math.Min(left,x);top=Math.Min(top,y);right=Math.Max(right,x);bottom=Math.Max(bottom,y);}
            Assert(right-left is >=68 and <=78 && bottom-top is >=102 and <=112,"PNG acquired an unintended monitor DPI scale");
        });
    }
}
