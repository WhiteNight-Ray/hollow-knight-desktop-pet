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
                CrossProcess(path,pet,Check);
            }
            catch(Exception e) {results.Add("FAIL "+e);}
            finally {foreach(var form in forms) form.Dispose();File.WriteAllLines(path,results);host.Close();}
        }));
        Application.Run(host);
        return results.Any(r=>r.StartsWith("FAIL"))?1:0;
    }
    static void Pump()
    {
        var clock=System.Diagnostics.Stopwatch.StartNew();
        while(clock.ElapsedMilliseconds<80) {Application.DoEvents();Thread.Sleep(1);}
    }
    static void Press(byte key,uint flags)
    {
        keybd_event(key,0,flags,0);
        try {Pump();} finally {keybd_event(key,0,flags|2,0);Pump();}
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
