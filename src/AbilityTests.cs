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
        test("shade dash travels 300 pixels in .25 seconds and preserves aerial height",()=> {
            var p=new Physics();p.Reset(300,300);p.VY=-500;var a=new AbilityState();
            Assert(a.StartDash(p,1),"dash rejected");Step(a,p,30);
            Assert(Math.Abs(p.X-600)<.02 && p.Y==300 && a.Phase==AbilityPhase.DashRecovery,"distance, height or duration incorrect");
            Step(a,p,15);Assert(!a.Busy && p.Y>300,"gravity did not resume");
        });
        test("shade dash honors facing direction and cooldown",()=> {
            var p=new Physics();p.Reset(700,806);p.Grounded=true;var a=new AbilityState();
            Assert(a.StartDash(p,1,-1),"left dash rejected");Step(a,p,45);
            Assert(a.Facing==-1 && Math.Abs(p.X-400)<.02 && !a.StartDash(p,1),"direction or cooldown lost");
            Step(a,p,14);Assert(!a.StartDash(p,1),"cooldown ended before .5 seconds");
            Step(a,p,1);Assert(a.StartDash(p,1),"dash unavailable at .5 seconds");
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
        test("descending dark reaches taskbar and recovers at 1.1 times playback speed",()=> {
            var p=new Physics();p.Reset(300,300);var a=new AbilityState();a.StartDive(p,-1);Step(a,p,70);
            Assert(a.Phase==AbilityPhase.DiveImpact && p.Grounded && p.Y==806 && a.ImpactY==900,"missed taskbar");
            Step(a,p,59);Assert(a.Phase==AbilityPhase.DiveImpact,"recovery cut off final animation frames");
            Step(a,p,4);Assert(!a.Busy && a.StartDive(p,-1)==false,"accelerated recovery or grounded cast incorrect");
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
            Assert(art.FrameCount==128,"missing original ability frames");
            foreach(int facing in new[] {-1,1})
            {
                var p=new Physics();p.Reset(500,300);var a=new AbilityState();a.StartDash(p,facing);
                void Verify()
                {
                    using var frame=art.Compose(normal,p,a,true,false,facing);
                    Assert(frame.Width==720 && frame.Height==480,"canvas size changed");
                    for(int x=0;x<frame.Width;x+=4)Assert(frame.GetPixel(x,0).A==0 && frame.GetPixel(x,479).A==0,"vertical effect clipped");
                    for(int y=0;y<frame.Height;y+=4)Assert(frame.GetPixel(0,y).A==0 && frame.GetPixel(719,y).A==0,"horizontal effect clipped");
                }
                for(int i=0;i<44;i++){Verify();Step(a,p,1);art.UpdateTrail(1f/120,p,a);}
                a.Cancel();art.ClearTrail();p.Reset(500,300);a.StartDive(p,facing);
                for(int i=0;i<140;i++){Verify();Step(a,p,1);art.UpdateTrail(1f/120,p,a);}
                a.Cancel();p.Reset(500,500);a.StartShriek(p,facing);
                for(int i=0;i<110;i++){Verify();Step(a,p,1);}
                p.Reset(500,500);p.Step(1f/120,0,true,[floor],area);
                for(int i=0;i<65;i++){Verify();Step(a,p,1);}
            }
        });
        test("double jump reverses falling velocity once and blocks a third airborne jump",()=> {
            var p=new Physics();p.Reset(500,500);p.VY=500;
            p.Step(1f/120,0,true,[floor],area,true);
            Assert(p.VY<0 && p.DoubleJumping && !p.AirJumpAvailable,"airborne Space did not jump");
            for(int i=0;i<60;i++)p.Step(1f/120,0,false,[floor],area,true);
            float velocity=p.VY;p.Step(1f/120,0,true,[floor],area,true);
            Assert(!p.AirJumpAvailable && p.VY>=velocity,"third jump refreshed upward velocity");
        });
        test("ordinary ground jump preserves the second jump and increases reachable height",()=> {
            var p=new Physics();p.Reset(500,806);p.Grounded=true;
            p.Step(1f/120,0,true,[floor],area,true);Assert(p.AirJumpAvailable && !p.DoubleJumping,"ground jump consumed wings");
            for(int i=0;i<40;i++)p.Step(1f/120,0,false,[floor],area,true);
            p.Step(1f/120,0,true,[floor],area,true);
            for(int i=0;i<32;i++)p.Step(1f/120,0,false,[floor],area,true);
            Assert(806-p.Y>350 && !p.AirJumpAvailable,"double jump gained no extra height");
        });
        test("landing on a window roof refreshes the second jump",()=> {
            var p=new Physics();p.Reset(500,500);p.Step(1f/120,0,true,[floor],area);
            var roof=new Platform(8,new RectangleF(400,650,400,220));
            for(int i=0;i<180;i++)p.Step(1f/120,0,false,[roof,floor],area,false);
            Assert(p.Grounded && p.Support==8 && p.AirJumpAvailable && !p.DoubleJumping,"roof landing did not refresh wings");
        });
        test("wall attachment refreshes wings and wall kick leaves them available",()=> {
            var p=new Physics();p.Reset(200,400);p.Step(1f/120,0,true,[floor],area);
            for(int i=0;i<80 && p.AttachedSide==0;i++)p.Step(1f/120,-1,false,[floor],area,true,new ScreenEdges(0,1600));
            Assert(p.AttachedSide==-1 && p.AirJumpAvailable,"wall did not refresh wings");
            p.Step(1f/120,0,true,[floor],area,true,new ScreenEdges(0,1600));
            Assert(p.WallJumping && p.AirJumpAvailable && !p.DoubleJumping,"wall kick spent second jump");
        });
        test("focus loss and air spells cannot replenish an already spent second jump",()=> {
            var p=new Physics();p.Reset(500,500);p.Step(1f/120,0,true,[floor],area);
            p.CancelInput();Assert(!p.AirJumpAvailable && !p.DoubleJumping,"blur refreshed wings");
            var a=new AbilityState();a.StartDash(p,1);Step(a,p,45);
            Assert(!p.AirJumpAvailable,"dash refreshed wings");
            a.StartShriek(p,1);Step(a,p,106);Assert(!p.AirJumpAvailable,"air shriek refreshed wings");
        });
        test("abyss shriek freezes air motion and recovers through all phases",()=> {
            var p=new Physics();p.Reset(500,400);p.VY=-500;var a=new AbilityState();Assert(a.StartShriek(p,-1),"cast rejected");
            Step(a,p,12,direction:1);Assert(a.Phase==AbilityPhase.ShriekBurst && p.X==500 && p.Y==400,"windup moved hero");
            Step(a,p,78,direction:1);Assert(a.Phase==AbilityPhase.ShriekRecovery && p.Y==400,"burst failed to suspend gravity");
            Step(a,p,15);Assert(!a.Busy,"recovery did not end");Step(a,p,2);Assert(p.Y>400,"gravity failed to resume");
        });
        test("abyss shriek deals four upward pulses and excludes distant enemies",()=> {
            var p=new Physics();p.Reset(500,806);p.Grounded=true;var a=new AbilityState();
            var near=new Monster {Id=1,X=560,Y=857,HP=8};var above=new Monster {Id=2,X=500,Y=580,HP=8};
            var far=new Monster {Id=3,X=850,Y=857,HP=8};var below=new Monster {Id=4,X=500,Y=950,HP=8};
            a.StartShriek(p,1);Step(a,p,100,monsters:[near,above,far,below]);
            Assert(near.HP==4 && above.HP==4 && far.HP==8 && below.HP==8,"pulse count or upward hitbox incorrect");
        });
        test("abyss shriek respects cooldown cancels on blur and excludes conflicting casts",()=> {
            var p=new Physics();p.Reset(500,400);var a=new AbilityState();a.StartShriek(p,1);
            Assert(!a.StartDash(p,1) && !a.StartDive(p,1) && !a.StartShriek(p,1),"cast conflict accepted");
            Step(a,p,5);a.Cancel();p.CancelInput();Assert(!a.StartShriek(p,1),"cancel bypassed cooldown");
            Step(a,p,127);Assert(a.StartShriek(p,-1),"cooldown exceeded 1.1 seconds");
        });
        test("left Shift requests shriek once while right Shift and inactive keys do not",()=> {
            var input=new PetControls();input.KeyDown(Keys.LShiftKey);Assert(!input.TakeShriek(),"inactive Shift accepted");
            input.Activate();input.KeyDown(Keys.RShiftKey);Assert(!input.TakeShriek(),"right Shift cast");
            input.KeyDown(Keys.LShiftKey);Assert(input.TakeShriek(),"left Shift lost");input.KeyDown(Keys.LShiftKey);Assert(!input.TakeShriek(),"held Shift recast");
            input.KeyUp(Keys.LShiftKey);input.KeyDown(Keys.LShiftKey);input.CheckFocus(false);Assert(!input.TakeShriek(),"pending shriek survived blur");
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
