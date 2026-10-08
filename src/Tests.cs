namespace HollowKnightPet;

internal static class Tests
{
    internal static int Run(string path)
    {
        var results=new List<string>();
        var area=new RectangleF(0,0,1600,1000);
        var floor=new Platform(-1,new RectangleF(0,900,1600,100),true);
        void Test(string name,Action action) { try { action(); results.Add("PASS "+name); } catch(Exception e) { results.Add("FAIL "+name+": "+e.Message); } }
        void Assert(bool value,string why) { if(!value) throw new Exception(why); }
        void Sim(Physics p,int count,IReadOnlyList<Platform> platforms,int direction=0) { for(int i=0;i<count;i++) p.Step(1f/120,direction,false,platforms,area); }
        Test("falls onto taskbar without penetration",()=> {
            var p=new Physics(); p.Reset(300,50); Sim(p,240,[floor]);
            Assert(p.Grounded && Math.Abs(p.Y+Physics.Height-900)<.01,"wrong landing");
        });
        Test("jump rises then lands",()=> {
            var p=new Physics(); p.Reset(300,806); p.Grounded=true;
            p.Step(1f/120,0,true,[floor],area); Sim(p,25,[floor]);
            Assert(p.Y<700,"jump too low"); Sim(p,150,[floor]); Assert(p.Grounded && p.Y==806,"failed to land");
        });
        Test("one way roof can be reached from below",()=> {
            var roof=new Platform(1,new RectangleF(200,820,300,60));
            var p=new Physics(); p.Reset(300,806); p.Grounded=true;
            p.Step(1f/120,0,true,[roof,floor],area); Sim(p,170,[roof,floor]);
            Assert(p.Support==1 && p.Y==726,"missed roof");
        });
        Test("walking off a roof causes falling",()=> {
            var roof=new Platform(1,new RectangleF(200,500,120,300));
            var p=new Physics(); p.Reset(290,406); p.Grounded=true;
            Sim(p,130,[roof,floor],1); Assert(p.X>500 && p.Y>406,"stuck on edge");
        });
        Test("fast falling cannot tunnel through roof",()=> {
            var p=new Physics(); p.Reset(300,300); p.VY=1300;
            Sim(p,2,[new Platform(1,new RectangleF(200,400,300,200)),floor]);
            Assert(p.Support==1 && p.Y==306,"tunneled");
        });
        Test("nearest roof wins regardless of enumeration order",()=> {
            var p=new Physics(); p.Reset(300,299); p.VY=1300;
            p.Step(1f/60,0,false,[new Platform(2,new RectangleF(200,405,300,100)),new Platform(1,new RectangleF(200,400,300,100))],area);
            Assert(p.Support==1 && p.Y==306,"wrong roof");
        });
        Test("solid side collision",()=> {
            var p=new Physics {SolidWindows=true}; p.Reset(110,550);
            Sim(p,25,[new Platform(1,new RectangleF(200,400,300,450)),floor],1);
            Assert(p.X+Physics.Width<=200.01,"walked through wall");
        });
        Test("solid underside collision",()=> {
            var p=new Physics {SolidWindows=true}; p.Reset(300,705); p.VY=-740;
            p.Step(1f/60,0,false,[new Platform(1,new RectangleF(200,400,300,300)),floor],area);
            Assert(p.Y>=700 && p.VY==0,"passed through underside");
        });
        Test("newly overlapping window does not trap pet",()=> {
            var p=new Physics {SolidWindows=true}; p.Reset(300,500);
            Sim(p,120,[new Platform(1,new RectangleF(200,400,300,400)),floor],1);
            Assert(p.X>500,"trapped inside moved window");
        });
        Test("removed window releases support",()=> {
            var p=new Physics(); p.Reset(300,406); p.Grounded=true; p.Support=1;
            Sim(p,120,[floor]); Assert(p.Y==806 && p.Support==-1,"stale platform");
        });
        Test("foreground window clips obscured roofs",()=> {
            var roofs=Desktop.VisibleRoofs([new Platform(1,new RectangleF(250,200,100,400)),new Platform(2,new RectangleF(100,300,400,300))]);
            var rear=roofs.Where(p=>p.Id==2).ToList();
            Assert(rear.Count==2 && rear[0].Bounds.Right==250 && rear[1].Bounds.Left==350,"occluded roof exposed");
        });
        Test("negative monitor coordinates",()=> {
            var p=new Physics(); p.Reset(-800,100);
            var left=new RectangleF(-1920,0,1920,1080);
            for(int i=0;i<240;i++) p.Step(1f/120,-1,false,[new Platform(-1,new RectangleF(-1920,1040,1920,100),true)],left);
            Assert(p.X< -800 && p.Y==946,"negative coordinates broken");
        });
        Test("immediate run stop and air reverse",()=> {
            var p=new Physics(); p.Reset(300,200);
            p.Step(1f/120,1,false,[],area);
            Assert(p.VX==Physics.RunSpeed,"slow acceleration");
            p.Step(1f/120,-1,false,[],area);
            Assert(p.VX==-Physics.RunSpeed,"slow air reverse");
            float x=p.X; p.Step(1f/120,0,false,[],area);
            Assert(p.VX==0 && p.X==x,"sliding after release");
        });
        float JumpHeight(float heldSeconds)
        {
            var p=new Physics(); p.Reset(300,806); p.Grounded=true; float top=p.Y;
            for(int i=0;i<180;i++) { p.Step(1f/120,0,i==0,[floor],area,i/120f<heldSeconds); top=Math.Min(top,p.Y); }
            return 806-top;
        }
        Test("short tap and long hold have distinct jump heights",()=> {
            float shortHop=JumpHeight(.025f),full=JumpHeight(1f),medium=JumpHeight(.14f);
            Assert(shortHop>35 && shortHop<85,$"short hop {shortHop}");
            Assert(full>200 && full<290,$"full jump {full}");
            Assert(shortHop<medium && medium<full,"non-monotonic hold response");
            results.Add($"INFO jump heights: tap={shortHop:F1}px, medium={medium:F1}px, full={full:F1}px; run={Physics.RunSpeed:F1}px/s");
        });
        Test("release during ballistic ascent cancels rise",()=> {
            var p=new Physics(); p.Reset(300,806); p.Grounded=true;
            p.Step(1f/120,0,true,[floor],area,true);
            for(int i=0;i<28;i++) p.Step(1f/120,0,false,[floor],area,true);
            Assert(p.VY<0,"expected ascent");
            p.Step(1f/120,0,false,[floor],area,false);
            Assert(p.VY>=0,"late release did not cut jump");
        });
        Test("jump input buffered just before landing",()=> {
            var p=new Physics(); p.Reset(300,800); p.VY=200;
            p.Step(1f/120,0,true,[floor],area,true);
            Sim(p,6,[floor]); Assert(p.VY<0,"buffer lost");
        });
        Test("coyote jump shortly after leaving a roof",()=> {
            var p=new Physics(); p.Reset(300,400); p.Grounded=true;
            p.Step(1f/120,1,false,[],area); p.Step(1f/120,1,true,[],area);
            Assert(p.VY<0,"no ledge grace");
        });
        Test("holding jump never auto jumps again",()=> {
            var p=new Physics(); p.Reset(300,806); p.Grounded=true;
            p.Step(1f/120,0,true,[floor],area,true); Sim(p,240,[floor]);
            Assert(p.Grounded && p.Y==806,"repeated jump");
        });
        var edges=new ScreenEdges(0,1600);
        Test("left edge attracts and holds without gravity drift",()=> {
            var p=new Physics(); p.Reset(28,350);
            p.Step(1f/120,-1,false,[],area,false,edges);
            Assert(p.AttachedSide==-1 && p.X==Physics.EdgeInset,"left attachment");
            for(int i=0;i<120;i++) p.Step(1f/120,0,false,[],area,false,edges);
            Assert(p.Y==350 && p.VY==0,"edge drift");
        });
        Test("right edge attachment and reverse direction release",()=> {
            var p=new Physics(); p.Reset(1530,350);
            p.Step(1f/120,1,false,[],area,false,edges);
            Assert(p.AttachedSide==1,"right attachment");
            float x=p.X; p.Step(1f/120,-1,false,[],area,false,edges);
            Assert(p.AttachedSide==0 && p.X<x,"cannot walk away");
        });
        Test("space kicks away from wall and cooldown prevents immediate reattachment",()=> {
            var p=new Physics(); p.Reset(28,350);
            p.Step(1f/120,-1,false,[],area,false,edges);
            p.Step(1f/120,-1,true,[],area,true,edges);
            Assert(p.AttachedSide==0 && p.VX>0 && p.VY<0,"bad wall kick");
            for(int i=0;i<8;i++) p.Step(1f/120,-1,false,[],area,true,edges);
            Assert(p.AttachedSide==0 && p.X>Physics.EdgeInset,"immediately reattached");
        });
        Test("disabled attachment and internal monitor seams",()=> {
            var p=new Physics { AutoAttach=false }; p.Reset(20,350);
            p.Step(1f/120,-1,false,[],area,false,edges);
            Assert(p.AttachedSide==0,"disabled snap still active");
            p.AutoAttach=true; p.Step(1f/120,-1,false,[],area,false,new ScreenEdges(null,1600));
            Assert(p.AttachedSide==0,"attached on shared monitor seam");
        });
        Test("screen change releases obsolete attachment",()=> {
            var p=new Physics(); p.Reset(28,350);
            p.Step(1f/120,-1,false,[],area,false,edges);
            p.Step(1f/120,0,false,[],area,false,new ScreenEdges(null,1600));
            Assert(p.AttachedSide==0 && p.VY>0,"stuck on vanished edge");
        });
        Test("original frame resources load and all animations render",()=> {
            using var art=new SpriteArt(); Assert(art.FrameCount==76,"missing original frames");
            foreach(string clip in new[] {"idle_still","idle_to_run","run","run_to_idle","jump","fall","land","wall_slide","wall_jump"})
            {
                using var left=art.RenderClip(clip,.2,-1); using var right=art.RenderClip(clip,.2,1);
                Assert(left.Width==KnightArt.W && right.Height==KnightArt.H,"bad canvas");
            }
        });
        var window=new Platform(77,new RectangleF(400,200,300,500));
        var sides=Desktop.VisibleSides([window]);
        Test("both window sides attach without solid collision mode",()=> {
            var left=new Physics(); left.Reset(340,300);
            left.Step(1f/120,1,false,[window],area,false,null,sides);
            Assert(left.AttachedWindow==77 && left.AttachedSide==1 && left.X==352,"left flank not attached");
            var right=new Physics(); right.Reset(715,300);
            right.Step(1f/120,-1,false,[window],area,false,null,sides);
            Assert(right.AttachedWindow==77 && right.AttachedSide==-1 && right.X==706,"right flank not attached");
        });
        Test("window attachment follows movement and resize",()=> {
            var p=new Physics(); p.Reset(715,300);
            p.Step(1f/120,-1,false,[window],area,false,null,sides);
            var moved=new Platform(77,new RectangleF(430,220,350,500));
            p.Step(1f/120,0,false,[moved],area,false,null,Desktop.VisibleSides([moved]));
            Assert(p.AttachedWindow==77 && p.X==786 && p.Y==320,"did not follow moved right edge");
        });
        Test("missing or occluded window edge releases attachment",()=> {
            foreach(bool hidden in new[] {false,true})
            {
                var p=new Physics(); p.Reset(340,300);
                p.Step(1f/120,1,false,[window],area,false,null,sides);
                var cover=new Platform(88,new RectangleF(380,240,80,250));
                var remaining=hidden ? Desktop.VisibleSides([cover,window]) : new List<WindowEdge>();
                p.Step(1f/120,0,false,hidden?[cover,window]:[],area,false,null,remaining);
                Assert(p.AttachedSide==0 && p.AttachedWindow==0 && p.VY>0,"stale side attachment");
            }
        });
        Test("window reverse and jump release move away from the correct surface",()=> {
            var p=new Physics(); p.Reset(340,300);
            p.Step(1f/120,1,false,[window],area,false,null,sides);
            p.Step(1f/120,-1,false,[window],area,false,null,sides);
            Assert(p.AttachedSide==0 && p.VX<0 && p.X<352,"reverse did not detach left flank");
            p.Reset(715,300);
            p.Step(1f/120,-1,false,[window],area,false,null,sides);
            p.Step(1f/120,-1,true,[window],area,true,null,sides);
            Assert(p.AttachedSide==0 && p.VX>0 && p.VY<0,"jump did not kick away from right flank");
        });
        Test("window roofs and interiors do not cause side attachment",()=> {
            var top=new Physics(); top.Reset(350,106); top.Grounded=true;
            top.Step(1f/120,1,false,[window],area,false,null,sides);
            Assert(top.AttachedSide==0,"roof walk snapped sideways");
            var inside=new Physics(); inside.Reset(410,300);
            inside.Step(1f/120,-1,false,[window],area,false,null,sides);
            Assert(inside.AttachedSide==0,"interior teleported outside");
        });
        Test("foreground windows clip only covered side segments",()=> {
            var cover=new Platform(88,new RectangleF(380,300,80,100));
            var visible=Desktop.VisibleSides([cover,window]);
            var left=visible.Where(w=>w.Id==77 && w.Side==1).ToList();
            var right=visible.Where(w=>w.Id==77 && w.Side==-1).ToList();
            Assert(left.Count==2 && left[0].Bottom==300 && left[1].Top==400 && right.Count==1,"wrong side clipping");
        });
        Test("solid collision and side attachment coexist",()=> {
            var p=new Physics {SolidWindows=true}; p.Reset(352,300);
            p.Step(1f/60,1,false,[window],area,false,null,sides);
            Assert(p.AttachedWindow==77 && p.X+Physics.Width<400,"solid wall prevented attachment");
        });
        Test("window attachment toggle and release cooldown",()=> {
            var p=new Physics {AutoAttach=false}; p.Reset(340,300);
            p.Step(1f/120,1,false,[window],area,false,null,sides);
            Assert(p.AttachedWindow==0,"disabled window attachment still active");
            p.AutoAttach=true; p.Step(1f/120,1,false,[window],area,false,null,sides);
            Assert(p.AttachedWindow==77,"failed to re-enable attachment");
            p.ReleaseEdge(); p.Step(1f/120,0,false,[window],area,false,null,sides);
            Assert(p.AttachedWindow==0,"release cooldown ignored");
        });
        Test("window shrinking past contact point releases attachment",()=> {
            var p=new Physics(); p.Reset(340,500);
            p.Step(1f/120,1,false,[window],area,false,null,sides);
            var small=new Platform(77,new RectangleF(400,200,300,150));
            p.Step(1f/120,0,false,[small],area,false,null,Desktop.VisibleSides([small]));
            Assert(p.AttachedWindow==0 && p.VY>0,"attached beyond resized edge");
        });
        Test("ground walking never attaches to screen sides",()=> {
            var p=new Physics();p.Reset(50,806);p.Grounded=true;
            for(int i=0;i<60;i++) p.Step(1f/120,-1,false,[floor],area,false,edges);
            Assert(p.AutoAttach && p.AttachedSide==0 && p.Grounded,"ground walk attached");
        });
        Test("ground walking never attaches to a window but jumping does",()=> {
            var w=new Platform(99,new RectangleF(400,200,300,700));var ws=Desktop.VisibleSides([w]);
            var p=new Physics();p.Reset(325,806);p.Grounded=true;
            for(int i=0;i<7;i++) p.Step(1f/120,1,false,[w,floor],area,false,null,ws);
            Assert(p.AttachedWindow==0,"ground window snap");
            p.Step(1f/120,1,true,[w,floor],area,true,null,ws);
            Assert(p.AttachedWindow==99,"jump did not enable attachment");
        });
        Test("dragged pose can attach even when released at floor height",()=> {
            var p=new Physics();p.Reset(25,806);
            p.Step(1f/120,0,false,[floor],area,false,edges);
            Assert(p.AttachedSide==-1,"drag release ignored");
        });
        Test("controls require click activation and clear on focus loss",()=> {
            var input=new PetControls();input.KeyDown(Keys.D);input.KeyDown(Keys.J);
            Assert(input.Direction==0 && !input.TakeAttack(),"inactive key accepted");
            input.Activate();input.KeyDown(Keys.D);input.KeyDown(Keys.Space);input.KeyDown(Keys.J);
            Assert(input.Direction==1 && input.JumpHeld,"active movement rejected");
            input.CheckFocus(false);
            Assert(!input.Armed && input.Direction==0 && !input.JumpHeld && !input.TakeJump() && !input.TakeAttack(),"input leaked after blur");
            input.CheckFocus(true);input.KeyDown(Keys.D);Assert(input.Direction==0,"refocus armed without a click");
            input.Activate();Assert(input.Direction==0,"stale keys restored on click");
        });
        Test("local keys generate one action per physical press",()=> {
            var input=new PetControls();input.Activate();input.KeyDown(Keys.J);
            Assert(input.TakeAttack(),"missing attack");input.KeyDown(Keys.J);Assert(!input.TakeAttack(),"key repeat attacked twice");
            input.KeyUp(Keys.J);input.KeyDown(Keys.J);Assert(input.TakeAttack(),"second press lost");
        });
        Test("blur cancels buffered jumps and wall kick momentum",()=> {
            var p=new Physics();p.Reset(25,350);p.Step(1f/120,-1,false,[],area,false,edges);
            p.Step(1f/120,0,true,[],area,true,edges);p.CancelInput();
            p.Step(1f/120,0,false,[],area,false,edges);
            Assert(p.VX==0 && !p.WallJumping && p.VY>=0,"movement survived blur");
        });
        var lanes=new RectangleF[] {new(0,0,1600,900)};
        var hero=new RectangleF(750,806,42,94);
        Test("spawn interval and random placement stay on taskbar",()=> {
            var world=new MonsterWorld(17);var sword=new AttackState();world.Configure(true,5,0);
            world.Update(4.99,.01f,lanes,hero,sword);Assert(world.Monsters.Count==0,"spawned too soon");
            world.Update(5,.01f,lanes,hero,sword);Assert(world.Monsters.Count==1,"did not spawn");
            var m=world.Monsters[0];Assert(m.Y+Monster.Height==900 && m.X>=0 && m.X+Monster.Width<=1600 && !m.Body.IntersectsWith(hero),"bad spawn position");
            world.Update(9.9,.01f,lanes,hero,sword);Assert(world.Monsters.Count==1,"spawn frequency incorrect");
            world.Update(10,.01f,lanes,hero,sword);Assert(world.Monsters.Count==2,"second interval missed");
        });
        Test("spawn cap toggle and frequency reconfiguration",()=> {
            var world=new MonsterWorld(12);var sword=new AttackState();world.Configure(true,2,0);
            for(int i=0;i<30;i++) world.Spawn(i,lanes,hero);
            Assert(world.Monsters.Count==MonsterWorld.MaxMonsters,"cap not respected");
            world.Configure(false,5,40);Assert(world.Monsters.Count==0 && !world.Spawn(50,lanes,hero),"disabled spawner left monsters");
            world.Configure(true,30,50);world.Update(79,.01f,lanes,hero,sword);Assert(world.Monsters.Count==0,"old schedule survived");
            world.Update(80,.01f,lanes,hero,sword);Assert(world.Monsters.Count==1,"new schedule not applied");
        });
        Test("two distinct sword swings kill and a swing hits each enemy only once",()=> {
            var sword=new AttackState();var m=new Monster {Id=1,X=810,Y=857,Lane=lanes[0]};sword.Start(0,1);
            Assert(sword.TryHit(m,hero,.08) && m.HP==1,"first hit failed");
            Assert(!sword.TryHit(m,hero,.09) && m.HP==1,"same swing hit twice");
            Assert(!sword.Start(.1,1),"cooldown missing");Assert(sword.Start(.4,1),"second swing rejected");
            Assert(sword.TryHit(m,hero,.48) && m.Dead,"second hit did not kill");
        });
        Test("attack respects facing range height and inactive damage window",()=> {
            var sword=new AttackState();var behind=new Monster {Id=1,X=680,Y=857,Lane=lanes[0]};
            sword.Start(0,1);Assert(!sword.TryHit(behind,hero,.08),"hit behind player");
            var far=new Monster {Id=2,X=1000,Y=857,Lane=lanes[0]};Assert(!sword.TryHit(far,hero,.08),"hit outside range");
            sword.Start(.4,-1);Assert(sword.TryHit(behind,hero,.48),"left-facing hit failed");
            sword.Cancel();Assert(!sword.TryHit(far,hero,.49) && !sword.Visible(.49),"cancelled attack still active");
        });
        Test("dead enemies despawn and disabled windows can be removed",()=> {
            var world=new MonsterWorld(4);var sword=new AttackState();world.Configure(true,300,0);world.Spawn(0,lanes,hero);
            world.Monsters[0].HP=0;world.Monsters[0].DeadAt=1;world.Update(1.5,.01f,lanes,hero,sword);
            Assert(world.Monsters.Count==0,"dead monster was not removed");
        });
        Test("settings validation and original monster rendering",()=> {
            var settings=new PetSettings {SpawnSeconds=-3};settings.Normalize();Assert(settings.SpawnSeconds==2,"invalid interval accepted");
            settings.SpawnSeconds=999;settings.Normalize();Assert(settings.SpawnSeconds==300,"interval maximum ignored");
            using var art=new MonsterArt();using var bitmap=art.Render(new Monster {Born=0},.2);
            Assert(bitmap.GetPixel(0,0).A==0 && bitmap.Width==100,"monster canvas is opaque");
        });
        Test("running distance increases by fifteen percent from v3",()=> {
            var p=new Physics();p.Reset(300,806);p.Grounded=true;
            Sim(p,120,[floor],1);
            Assert(Math.Abs(p.X-300-381.8f*1.15f)<.05,"unexpected one-second run distance");
        });
        Test("selected side button consumes complete click and triggers once",()=> {
            var gesture=new SideButtonGesture();
            Assert(gesture.Process(1,true,1)==(true,true),"missing activation");
            Assert(gesture.Process(1,true,1)==(true,false),"duplicate activation");
            Assert(gesture.Process(1,false,1)==(true,false),"release leaked");
            Assert(gesture.Process(1,false,1)==(false,false),"orphan release swallowed");
        });
        Test("unselected and disabled side buttons pass through",()=> {
            var gesture=new SideButtonGesture();
            Assert(gesture.Process(2,true,1)==(false,false) && gesture.Process(2,false,1)==(false,false),"unselected button blocked");
            Assert(gesture.Process(1,true,0)==(false,false),"disabled shortcut triggered");
            Assert(gesture.Process(2,true,2)==(true,true),"second side button failed");
            Assert(gesture.Process(2,false,0)==(true,false),"setting change leaked captured release");
            Assert(gesture.Process(0,true,1)==(false,false),"non-side button captured");
        });
        Test("side button settings preserve old configurations and validate",()=> {
            var settings=System.Text.Json.JsonSerializer.Deserialize<PetSettings>("{\"MonstersEnabled\":false,\"SpawnSeconds\":30}")!;
            Assert(settings.ActivationMouseButton==1 && !settings.MonstersEnabled && settings.SpawnSeconds==30,"migration changed settings");
            settings.ActivationMouseButton=2;settings.Normalize();Assert(settings.ActivationMouseButton==2,"valid setting lost");
            settings.ActivationMouseButton=5;settings.Normalize();Assert(settings.ActivationMouseButton==1,"invalid button accepted");
        });
        File.WriteAllLines(path,results); return results.Any(r=>r.StartsWith("FAIL"))?1:0;
    }
}
