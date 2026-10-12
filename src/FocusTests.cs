namespace HollowKnightPet;

internal static class FocusTests
{
    internal static int Run(string path)
    {
        var results=new List<string>();
        using var host=new TestWindow {Text="桌宠焦点回归测试",Size=new Size(320,120),ShowInTaskbar=false};
        using var art=new MonsterArt();
        var input=new PetControls();
        int deactivated=0;
        host.Deactivate+=(_,_)=> {deactivated++;input.Stop();};
        host.Shown+=(_,_)=>host.BeginInvoke((Action)(()=> {
            var forms=new List<MonsterForm>();
            void Check(bool ok,string name) {results.Add((ok?"PASS ":"FAIL ")+name);}
            try
            {
                host.Activate(); Native.SetForegroundWindow(host.Handle);host.Focus();Application.DoEvents();
                Check(Native.GetForegroundWindow()==host.Handle,"test host acquired foreground");
                input.Activate();input.KeyDown(Keys.D);input.KeyDown(Keys.Space);input.KeyDown(Keys.J);
                int before=deactivated;
                for(int i=0;i<6;i++)
                {
                    var form=new MonsterForm();forms.Add(form);
                    var monster=new Monster {X=50+i*110,Y=Screen.PrimaryScreen!.WorkingArea.Bottom-Monster.Height};
                    form.Present(monster,.1,art);form.ShowPassive();Application.DoEvents();
                    Check(Native.IsWindowVisible(form.Handle),$"monster {i+1} visible");
                    Check(Native.GetForegroundWindow()==host.Handle && input.Direction==1 && input.JumpHeld && deactivated==before,$"spawn {i+1} preserves foreground and held input");
                }
                Check(input.TakeAttack(),"spawning preserves pending attack");
                foreach(var form in forms) form.Dispose();forms.Clear();Application.DoEvents();
                Check(input.Armed && Native.GetForegroundWindow()==host.Handle,"despawn preserves control");
                using var other=new TestWindow {Text="Focus target",Size=new Size(150,80),ShowInTaskbar=false};
                other.Show();other.Activate();Native.SetForegroundWindow(other.Handle);Application.DoEvents();
                Check(!input.Armed && input.Direction==0 && !input.JumpHeld,"switching away still clears input");
                using var pet=new PetForm(Path.Combine(Path.GetTempPath(),"HollowKnightPet-focus-smoke.json"),false);
                pet.Show();other.Activate();Native.SetForegroundWindow(other.Handle);Pump();
                Press(0xA5,1);
                Check(Native.GetForegroundWindow()==other.Handle && !pet.ControlsActive,"Right Alt no longer activates pet");
                SideClick(1);
                Check(Native.GetForegroundWindow()==pet.Handle && pet.ControlsActive,"XButton1 activates actual pet and arms controls");
                other.Activate();Native.SetForegroundWindow(other.Handle);Pump();
                Check(!pet.ControlsActive,"actual pet disarms when another window activates");
                SideClick(2);
                Check(!pet.ControlsActive,"unselected XButton2 does not activate");
                pet.SetActivationButton(2);SideClick(2);
                Check(pet.ControlsActive,"menu selection switches activation to XButton2");
                other.Activate();Native.SetForegroundWindow(other.Handle);Pump();
                pet.SetActivationButton(0);SideClick(1);SideClick(2);
                Check(!pet.ControlsActive,"disabled side shortcut does not activate");
                pet.SetActivationButton(1);
                AbilityKeys(pet,other,Check);
                CrossProcess(path,pet,Check);
            }
            catch(Exception e) {results.Add("FAIL "+e);}
            finally {foreach(var form in forms) form.Dispose();File.WriteAllLines(path,results);host.Close();}
        }));
        Application.Run(host);
        return results.Any(r=>r.StartsWith("FAIL"))?1:0;
    }
    static void AbilityKeys(PetForm pet,Form other,Action<bool,string> check)
    {
        var area=Screen.PrimaryScreen!.WorkingArea;
        pet.Motion.Reset(area.Left+600,area.Bottom-Physics.Height);pet.Motion.Grounded=true;SideClick(1);
        Press(0x4A,0);
        check(pet.NailVisible && pet.CurrentAbility==AbilityPhase.None,"ground J keeps ordinary nail attack");
        Pump(300);float oldX=pet.Motion.X;Press(0x0D,0);
        check(pet.Motion.X>oldX+60 && pet.Motion.X<=oldX+300.1f && pet.DashCooldownRemaining>0,"Enter starts shade dash through actual keyboard messages");
        other.Activate();Native.SetForegroundWindow(other.Handle);Pump();
        check(!pet.ControlsActive && pet.CurrentAbility==AbilityPhase.None,"focus loss cancels shade dash");
        pet.Motion.Reset(area.Left+600,area.Bottom-Physics.Height);pet.Motion.Grounded=true;SideClick(1);
        Press(0x20,0);check(!pet.Motion.Grounded,"Space puts actual pet in air");Press(0x4A,0);
        check(pet.CurrentAbility is AbilityPhase.DiveWindup or AbilityPhase.DiveFall,"air J starts descending dark through actual keyboard messages");
        Pump(180);
        check(pet.CurrentAbility==AbilityPhase.DiveImpact && pet.Motion.Grounded,"descending dark reaches actual desktop floor");
        other.Activate();Native.SetForegroundWindow(other.Handle);Pump();
        check(pet.CurrentAbility==AbilityPhase.None && !pet.ControlsActive,"focus loss cancels dive recovery");
        pet.Motion.Reset(area.Left+600,area.Bottom-Physics.Height);pet.Motion.Grounded=true;SideClick(1);
        Press(0x20,0);Press(0x20,0);
        check(!pet.Motion.AirJumpAvailable && pet.Motion.DoubleJumping,"second physical Space press triggers actual double jump");
        other.Activate();Native.SetForegroundWindow(other.Handle);Pump();
        check(!pet.ControlsActive && !pet.Motion.DoubleJumping,"focus loss stops wing animation without restoring jump charge");
        pet.Motion.Reset(area.Left+600,area.Bottom-Physics.Height);pet.Motion.Grounded=true;SideClick(1);
        Press(0xA1,0,0x36);
        check(pet.CurrentAbility==AbilityPhase.None && pet.ShriekCooldownRemaining==0,"right Shift does not cast abyss shriek");
        float castY=pet.Motion.Y;Press(0xA0,0,0x2a);
        check(pet.CurrentAbility is AbilityPhase.ShriekWindup or AbilityPhase.ShriekBurst && pet.ShriekCooldownRemaining>0,"left Shift casts abyss shriek through native scan codes");
        check(pet.Motion.Y==castY,"ground shriek preserves floor position");
        using(var monsterArt=new MonsterArt())using(var monsterForm=new MonsterForm())
        {
            monsterForm.Present(new Monster {X=700,Y=area.Bottom-Monster.Height},.1,monsterArt);monsterForm.ShowPassive();Pump();
            check(pet.ControlsActive && Native.GetForegroundWindow()==pet.Handle && pet.CurrentAbility!=AbilityPhase.None,"monster spawn preserves active shriek");
        }
        other.Activate();Native.SetForegroundWindow(other.Handle);Pump();
        check(pet.CurrentAbility==AbilityPhase.None && !pet.ControlsActive,"focus loss cancels abyss shriek");
    }
    static void Pump(int milliseconds=80)
    {
        var clock=System.Diagnostics.Stopwatch.StartNew();
        while(clock.ElapsedMilliseconds<milliseconds) {Application.DoEvents();Thread.Sleep(1);}
    }
    static void Press(byte key,uint flags,byte scan=0)
    {
        keybd_event(key,scan,flags,0);
        try {Pump();} finally {keybd_event(key,scan,flags|2,0);Pump();}
    }
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern void keybd_event(byte key,byte scan,uint flags,nuint extra);
    static void SideClick(uint button)
    {
        mouse_event(0x80,0,0,button,0);
        try {Pump();} finally {mouse_event(0x100,0,0,button,0);Pump();}
    }
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern void mouse_event(uint flags,uint x,uint y,uint data,nuint extra);
    static void CrossProcess(string path,PetForm pet,Action<bool,string> check)
    {
        string ready=path+".target";
        if(File.Exists(ready)) File.Delete(ready);
        var start=new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!) {UseShellExecute=false,CreateNoWindow=true,WindowStyle=System.Diagnostics.ProcessWindowStyle.Hidden};
        start.ArgumentList.Add("--focus-target");start.ArgumentList.Add(ready);
        using var process=System.Diagnostics.Process.Start(start)!;
        var previous=Cursor.Position;
        try
        {
            var timeout=System.Diagnostics.Stopwatch.StartNew();
            while(!File.Exists(ready) && timeout.ElapsedMilliseconds<5000) Pump();
            nint hwnd=(nint)long.Parse(File.ReadAllText(ready));
            Native.SetWindowPos(hwnd,-1,40,40,240,160,0x40|0x10);
            for(int i=0;i<5;i++)
            {
                Native.SetForegroundWindow(hwnd);Pump();
                Cursor.Position=new Point(100,100);
                mouse_event(2,0,0,0,0);mouse_event(4,0,0,0,0);Pump();
                check(Native.GetForegroundWindow()==hwnd && !pet.ControlsActive,$"external process owns focus before activation {i+1} (expected={hwnd}, actual={Native.GetForegroundWindow()}, armed={pet.ControlsActive})");
                SideClick(1);
                check(Native.GetForegroundWindow()==pet.Handle && pet.ControlsActive,$"side button activates from external process {i+1}");
                check(File.ReadAllText(ready+".events")=="0",$"activation {i+1} does not deliver back-button messages to external window");
            }
            // Put a real process directly behind the spell's opaque trailing art.
            // Clicking it must still release control, even inside the larger canvas.
            var area=Screen.PrimaryScreen!.WorkingArea;
            Native.SetWindowPos(hwnd,-1,460,area.Bottom-150,130,150,0x40|0x10);
            pet.Motion.Reset(600,area.Bottom-Physics.Height);pet.Motion.Grounded=true;SideClick(1);
            keybd_event(0x0D,0,0,0);
            try
            {
                Pump(30);Cursor.Position=new Point(520,area.Bottom-55);
                mouse_event(2,0,0,0,0);mouse_event(4,0,0,0,0);Pump();
                check(Native.GetForegroundWindow()==hwnd && !pet.ControlsActive,"spell area passes clicks through to another process");
            }
            finally {keybd_event(0x0D,0,2,0);}
            Native.SetWindowPos(hwnd,-1,40,40,240,160,0x40|0x10);
            Cursor.Position=new Point(100,100);
            pet.SetActivationButton(0);Native.SetForegroundWindow(hwnd);Pump();
            mouse_event(2,0,0,0,0);mouse_event(4,0,0,0,0);Pump();SideClick(1);
            check(Native.GetForegroundWindow()==hwnd && !pet.ControlsActive,"disabled shortcut preserves external focus");
            check(File.ReadAllText(ready+".events")=="2","disabled shortcut passes side-button down and up to external window");
        }
        finally
        {
            Cursor.Position=previous;
            if(!process.HasExited) {process.Kill();process.WaitForExit();}
            if(File.Exists(ready)) File.Delete(ready);
            if(File.Exists(ready+".events")) File.Delete(ready+".events");
        }
    }
    internal static int RunTarget(string ready)
    {
        using var form=new TestWindow {Text="桌宠跨进程焦点测试",StartPosition=FormStartPosition.Manual,Location=new Point(40,40),Size=new Size(240,160)};
        int sideEvents=0;
        form.SideReceived=()=>File.WriteAllText(ready+".events",(++sideEvents).ToString());
        form.Shown+=(_,_)=> {File.WriteAllText(ready+".events","0");File.WriteAllText(ready,form.Handle.ToInt64().ToString());};
        Application.Run(form);return 0;
    }
    sealed class TestWindow : Form
    {
        public Action? SideReceived;
        protected override void WndProc(ref Message m)
        {
            if(m.Msg is 0x20B or 0x20C) SideReceived?.Invoke();
            if(m.Msg==0x112 && ((long)m.WParam&0xfff0)==0xf100) return;
            base.WndProc(ref m);
        }
    }
}
