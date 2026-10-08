using System.Diagnostics;
using System.Text.Json;

namespace HollowKnightPet;

internal static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        if (args.Length >= 2 && args[0] == "--self-test") return Tests.Run(args[1]);
        if (args.Length >= 2 && args[0] == "--focus-test") return FocusTests.Run(args[1]);
        if (args.Length >= 2 && args[0] == "--focus-target") return FocusTests.RunTarget(args[1]);
        if (args.Length >= 2 && args[0] == "--preview-combat")
        {
            using var art=new SpriteArt();using var enemyArt=new MonsterArt();
            using var sheet=new Bitmap(760,230);using var g=Graphics.FromImage(sheet);g.Clear(Color.FromArgb(28,34,47));
            using var font=new Font("Segoe UI",13);g.DrawString("CLICK TO CONTROL    A / D  MOVE    SPACE  JUMP    J  ATTACK",font,Brushes.White,22,18);
            var attack=new AttackState();attack.Start(0,1);
            var p=new Physics();p.Grounded=true;
            using var knight=art.Render(.10,p,1,false,false,.10);
            using var hero=CombatArt.Hero(knight,attack,.10,true);g.DrawImageUnscaled(hero,25,62);
            for(int i=0;i<3;i++)
            {
                var m=new Monster {Born=0,Facing=i==1?-1:1,HP=i==2?1:2,HitUntil=i==2?1:0};
                using var bug=enemyArt.Render(m,.1+i*.12);g.DrawImageUnscaled(bug,300+i*130,110);
            }
            using var line=new Pen(Color.FromArgb(95,127,153),2);g.DrawLine(line,20,186,740,186);
            sheet.Save(args[1]);return 0;
        }
        if (args.Length >= 2 && args[0] == "--preview-sheet")
        {
            using var art = new SpriteArt();
            string[] clips=["idle_still","run","jump","fall","wall_slide","wall_jump"];
            using var sheet=new Bitmap(KnightArt.W*clips.Length*2,KnightArt.H*2+60);
            using var g=Graphics.FromImage(sheet); g.Clear(Color.FromArgb(28,34,47));
            using var font=new Font("Segoe UI",14);
            for(int i=0;i<clips.Length;i++)
            {
                using var frame=art.RenderClip(clips[i],.12,1,clips[i]=="wall_slide"?1:0);
                g.DrawImage(frame,i*KnightArt.W*2,0,KnightArt.W*2,KnightArt.H*2);
                g.DrawString(clips[i],font,Brushes.White,i*KnightArt.W*2+45,KnightArt.H*2+10);
            }
            sheet.Save(args[1]); return 0;
        }
        if (args.Length >= 2 && args[0] == "--preview")
        {
            using var art = new SpriteArt();
            using var image = art.RenderClip(args.Length > 2 ? args[2] : "idle_still",0,1);
            image.Save(args[1]); return 0;
        }
        bool smoke = args.Length >= 2 && args[0] == "--smoke-test";
        using var mutex = new Mutex(true,@"Local\HollowKnightPet.Desktop.v1",out bool created);
        if (!created && !smoke) { MessageBox.Show("桌宠已经在运行。右键小骑士或系统托盘图标可以设置或退出。","空洞骑士桌宠"); return 0; }
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
        try { using var pet = new PetForm(smoke ? args[1] : null); Application.Run(pet); return 0; }
        catch (Exception ex)
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"HollowKnightPet");
            Directory.CreateDirectory(dir); File.WriteAllText(Path.Combine(dir,"error.log"),ex.ToString());
            if (smoke) File.WriteAllText(args[1],ex.ToString());
            else MessageBox.Show("启动失败，错误信息已写入：\n"+Path.Combine(dir,"error.log"),"空洞骑士桌宠");
            return 1;
        }
    }
}

internal sealed class PetForm : Form
{
    readonly Physics physics = new();
    readonly SpriteArt art = new();
    readonly PetControls controls = new();
    readonly AttackState attack = new();
    readonly MonsterWorld monsters = new();
    readonly MonsterArt monsterArt = new();
    readonly Dictionary<int,MonsterForm> monsterForms = [];
    readonly PetSettings settings;
    readonly System.Windows.Forms.Timer timer = new() { Interval = 15 };
    readonly Stopwatch clock = Stopwatch.StartNew();
    readonly NotifyIcon tray;
    readonly ContextMenuStrip menu = new();
    readonly ToolStripMenuItem pauseItem, solidItem, debugItem, attachItem, monsterItem, frequencyItem;
    readonly ToolStripMenuItem activationItem=new("鼠标侧键激活");
    readonly string? smokePath;
    readonly bool smokeAutoClose;
    List<Platform> windows = [], colliders = [];
    List<WindowEdge> windowEdges = [];
    bool paused, debug, dragging, mousePressed, menuOpen, resourcesDisposed, pendingJump;
    int facing = 1, frames;
    Point dragOffset;
    Point mouseOrigin;
    double lastTick, lastScan, accumulator;
    readonly List<int> hotkeys = [];
    ActivationShortcut? activationShortcut;
    internal bool ControlsActive=>controls.Armed;

    public PetForm(string? smokePath,bool smokeAutoClose=true)
    {
        this.smokePath = smokePath;
        this.smokeAutoClose=smokeAutoClose;
        settings=smokePath==null?PetSettings.Load():new PetSettings();
        monsters.Configure(settings.MonstersEnabled,settings.SpawnSeconds,0);
        Text = "空洞骑士桌宠";
        FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true;
        AutoScaleMode = AutoScaleMode.None; ClientSize = new Size(KnightArt.W+CombatArt.Margin*2,KnightArt.H);
        KeyPreview=true;
        StartPosition = FormStartPosition.Manual;
        pauseItem = new ToolStripMenuItem("暂停键盘控制（F8）",null,(_,_) => TogglePause());
        solidItem = new ToolStripMenuItem("实体窗口碰撞（侧边 / 底边）",null,(_,_) => { physics.SolidWindows = !physics.SolidWindows; solidItem!.Checked=physics.SolidWindows; RefreshWorld(); });
        debugItem = new ToolStripMenuItem("显示角色碰撞框",null,(_,_) => { debug=!debug; debugItem!.Checked=debug; });
        attachItem = new ToolStripMenuItem("屏幕 / 窗口侧边自动吸附",null,(_,_) => {
            physics.AutoAttach=!physics.AutoAttach; attachItem!.Checked=physics.AutoAttach;
            if(!physics.AutoAttach) physics.ReleaseEdge();
        }) { Checked=true };
        monsterItem=new ToolStripMenuItem("任务栏随机刷怪",null,(_,_)=> {
            settings.MonstersEnabled=!settings.MonstersEnabled; ApplyMonsterSettings();
        }) {Checked=settings.MonstersEnabled};
        frequencyItem=new ToolStripMenuItem();
        foreach(int seconds in new[] {5,10,15,30,60})
        {
            int interval=seconds;
            frequencyItem.DropDownItems.Add(new ToolStripMenuItem($"每 {seconds} 秒一只",null,(_,_)=> { settings.SpawnSeconds=interval; ApplyMonsterSettings(); }) {Tag=seconds,Checked=seconds==settings.SpawnSeconds});
        }
        frequencyItem.DropDownItems.Add("自定义间隔…",null,(_,_)=>ChooseSpawnInterval());
        frequencyItem.Text=$"刷怪间隔：{settings.SpawnSeconds} 秒";
        menu.Items.Add(new ToolStripMenuItem("小骑士 · 桌面漫游") { Enabled=false });
        menu.Items.Add(new ToolStripMenuItem("点击 / 侧键激活 · A/D 移动 · Space 跳 · J 攻击") { Enabled=false });
        foreach(var choice in new[] {(1,"后退侧键（XButton1，默认）"),(2,"前进侧键（XButton2）"),(0,"关闭侧键激活")})
        {
            int button=choice.Item1;
            activationItem.DropDownItems.Add(new ToolStripMenuItem(choice.Item2,null,(_,_)=> {
                SetActivationButton(button);
                if(smokePath==null && !settings.Save()) tray!.ShowBalloonTip(2500,"设置未保存","本次设置已生效，但写入设置文件失败。",ToolTipIcon.Warning);
            }) {Tag=button,Checked=settings.ActivationMouseButton==button});
        }
        menu.Items.Add(activationItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(pauseItem); menu.Items.Add(attachItem); menu.Items.Add(solidItem); menu.Items.Add(debugItem);
        menu.Items.Add(new ToolStripMenuItem("吸附后：反方向离开 / Space 蹬开") { Enabled=false });
        menu.Items.Add("回到任务栏（F9）",null,(_,_) => ResetPosition());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(monsterItem); menu.Items.Add(frequencyItem);
        menu.Items.Add("立即生成一只",null,(_,_)=> { monsters.Spawn(clock.Elapsed.TotalSeconds,Lanes(),physics.Body); SyncMonsters(); });
        menu.Items.Add("清空当前怪物",null,(_,_)=> { monsters.Monsters.Clear(); SyncMonsters(); });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("退出桌宠",null,(_,_) => Close());
        menu.Opening += (_,_) => { menuOpen=true; StopControl(); };
        menu.Closed += (_,_) => menuOpen=false;
        using var iconStream=typeof(PetForm).Assembly.GetManifestResourceStream("HollowKnightPet.HollowKnight.ico")!;
        using(var icon=new Icon(iconStream)) Icon=(Icon)icon.Clone();
        tray = new NotifyIcon { Icon = Icon, Text = "点击 / 鼠标侧键激活 · A/D 移动 · Space 跳跃 · J 攻击",ContextMenuStrip=menu,Visible=smokePath == null };
        tray.DoubleClick += (_,_) => TogglePause();
        timer.Tick += (_,_) => TickFrame();
        MouseDown += OnMouseDown; MouseMove += OnMouseMove; MouseUp += OnMouseUp;
        MouseCaptureChanged += (_,_) => { if (!Capture) { dragging=false; mousePressed=false; } };
        Deactivate += (_,_)=>StopControl();
        KeyDown += (_,e)=> {
            if(e.KeyCode==Keys.Escape) StopControl();
            else if(controls.Armed && !paused && !menuOpen) controls.KeyDown(e.KeyCode);
            if(e.KeyCode is Keys.A or Keys.D or Keys.Space or Keys.J or Keys.Escape) {e.Handled=true;e.SuppressKeyPress=true;}
        };
        KeyUp += (_,e)=>controls.KeyUp(e.KeyCode);
    }

    protected override bool ShowWithoutActivation => true;
    protected override CreateParams CreateParams
    {
        get { var cp = base.CreateParams; cp.ExStyle |= 0x80000 | 0x80; return cp; }
    }
    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        foreach(var pair in new[] { (1,Keys.F8),(2,Keys.F9) })
            if(Native.RegisterHotKey(Handle,pair.Item1,0x4000,(uint)pair.Item2)) hotkeys.Add(pair.Item1);
        if(!hotkeys.Contains(1)) pauseItem.Text="暂停键盘控制（F8 被占用，请用菜单）";
        activationShortcut=new ActivationShortcut(Handle,settings.ActivationMouseButton);
        ResetPosition(); RefreshWorld(); lastTick=clock.Elapsed.TotalSeconds;
        timer.Start();
        if(smokePath == null) tray.ShowBalloonTip(4500,"点击小骑士或按鼠标侧键开始控制",activationShortcut.Installed
            ? "A/D 移动，Space 跳跃，J 攻击。点击其他应用或按 Esc 自动停控。右键设置刷怪间隔。"
            : "鼠标侧键监听启动失败，请点击角色激活。A/D 移动，Space 跳跃，J 攻击。",ToolTipIcon.Info);
    }
    protected override void WndProc(ref Message m)
    {
        if(m.Msg==0x21) { m.Result=1; return; } // MA_ACTIVATE for an explicit user click.
        if(m.Msg==ActivationShortcut.Message) { ActivateControl();return; }
        if(m.Msg==0x312) { if(m.WParam==1) TogglePause(); if(m.WParam==2) ResetPosition(); return; }
        if(m.Msg==0x7e || m.Msg==0x1a) lastScan=-1; // display / working-area changed
        base.WndProc(ref m);
    }
    void StopControl() { controls.Stop(); physics.CancelInput(); pendingJump=false; attack.Cancel(); }
    internal void SetActivationButton(int button)
    {
        settings.ActivationMouseButton=button;settings.Normalize();
        if(activationShortcut!=null) activationShortcut.Button=settings.ActivationMouseButton;
        foreach(ToolStripMenuItem item in activationItem.DropDownItems) item.Checked=item.Tag is int value && value==settings.ActivationMouseButton;
    }
    void ActivateControl()
    {
        if(menuOpen) menu.Close();
        Native.SetForegroundWindow(Handle);Activate();Focus();
        if(Native.GetForegroundWindow()!=Handle) return;
        paused=false;pauseItem.Checked=false;
        if(!controls.Armed) controls.Activate();
    }
    void TogglePause() { paused=!paused; pauseItem.Checked=paused; StopControl(); }
    static List<RectangleF> Lanes()=>Screen.AllScreens.Select(s=>(RectangleF)s.WorkingArea).ToList();
    void ApplyMonsterSettings()
    {
        settings.Normalize(); monsterItem.Checked=settings.MonstersEnabled;
        frequencyItem.Text=$"刷怪间隔：{settings.SpawnSeconds} 秒";
        foreach(ToolStripMenuItem item in frequencyItem.DropDownItems) item.Checked=item.Tag is int value && value==settings.SpawnSeconds;
        monsters.Configure(settings.MonstersEnabled,settings.SpawnSeconds,clock.Elapsed.TotalSeconds);
        SyncMonsters();
        if(smokePath==null && !settings.Save()) tray.ShowBalloonTip(2500,"设置未保存","本次设置已生效，但写入设置文件失败。",ToolTipIcon.Warning);
    }
    void ChooseSpawnInterval()
    {
        using var dialog=new Form {Text="刷怪间隔",FormBorderStyle=FormBorderStyle.FixedDialog,StartPosition=FormStartPosition.CenterScreen,ClientSize=new Size(310,130),MinimizeBox=false,MaximizeBox=false,TopMost=true};
        var label=new Label {Text="每隔多少秒生成一只（2–300 秒）",Location=new Point(16,16),AutoSize=true};
        var number=new NumericUpDown {Minimum=2,Maximum=300,Value=settings.SpawnSeconds,Location=new Point(18,45),Width=270};
        var save=new Button {Text="确定",DialogResult=DialogResult.OK,Location=new Point(126,88)};
        var cancel=new Button {Text="取消",DialogResult=DialogResult.Cancel,Location=new Point(212,88)};
        dialog.Controls.AddRange([label,number,save,cancel]);dialog.AcceptButton=save;dialog.CancelButton=cancel;
        if(dialog.ShowDialog(this)==DialogResult.OK) { settings.SpawnSeconds=(int)number.Value;ApplyMonsterSettings(); }
    }
    void SyncMonsters()
    {
        foreach(int id in monsterForms.Keys.Where(id=>!monsters.Monsters.Any(m=>m.Id==id)).ToArray()) {monsterForms[id].Dispose();monsterForms.Remove(id);}
        foreach(var monster in monsters.Monsters)
        {
            if(!monsterForms.TryGetValue(monster.Id,out var form))
            { form=new MonsterForm();monsterForms.Add(monster.Id,form);form.Present(monster,clock.Elapsed.TotalSeconds,monsterArt);form.ShowPassive(); }
            form.Present(monster,clock.Elapsed.TotalSeconds,monsterArt);
        }
    }
    void ResetPosition()
    {
        var screen = Screen.FromPoint(Cursor.Position).WorkingArea;
        physics.Reset(screen.Left+screen.Width*.5f,screen.Bottom-Physics.Height);
        physics.Grounded=true; Present();
    }
    void RefreshWorld()
    {
        var next = Desktop.Scan();
        if(physics.Grounded && physics.Support > 0 && !dragging)
        {
            var old = windows.FirstOrDefault(p => p.Id==physics.Support);
            var current = next.FirstOrDefault(p => p.Id==physics.Support);
            if(old != null && current != null)
            {
                physics.X += current.Bounds.Left-old.Bounds.Left;
                physics.Y += current.Bounds.Top-old.Bounds.Top;
            }
        }
        windows=next;
        windowEdges=Desktop.VisibleSides(windows);
        colliders=physics.SolidWindows ? [..windows] : Desktop.VisibleRoofs(windows);
        colliders.AddRange(Desktop.Floors());
    }
    void TickFrame()
    {
        double now=clock.Elapsed.TotalSeconds;
        double elapsed=Math.Clamp(now-lastTick,0,.05); lastTick=now;
        if(now-lastScan >= .08) { RefreshWorld(); lastScan=now; }
        if(controls.Armed && Native.GetForegroundWindow()!=Handle) StopControl();
        bool controlsEnabled=controls.Armed && !paused && !menuOpen && smokePath == null;
        bool held=controls.JumpHeld;
        bool jump=controls.TakeJump();
        bool swing=controls.TakeAttack();
        if(controlsEnabled && jump) pendingJump=true;
        if(!controlsEnabled) pendingJump=false;
        int direction = controlsEnabled ? controls.Direction : 0;
        if(direction!=0) facing=direction;
        if(controlsEnabled && swing && !dragging) attack.Start(now,facing);
        if(!dragging)
        {
            accumulator += elapsed;
            while(accumulator>=1d/120)
            {
                // Choose a real monitor so a non-rectangular desktop cannot swallow the pet.
                var center=new Point((int)(physics.X+Physics.Width/2),(int)(physics.Y+Physics.Height/2));
                var screen=Screen.FromPoint(center);
                var bounds=screen.WorkingArea;
                var virtualBounds=SystemInformation.VirtualScreen;
                // Horizontal crossing works for adjacent displays; vertical limits use this monitor.
                var area=new RectangleF(virtualBounds.Left,bounds.Top,virtualBounds.Width,bounds.Bottom-bounds.Top);
                var edges=Desktop.EdgesAt(screen,center.Y);
                physics.Step(1f/120,direction,pendingJump,colliders,area,controlsEnabled && held,edges,windowEdges);
                pendingJump=false;
                accumulator-=1d/120;
            }
            if(!Screen.AllScreens.Any(s=>s.Bounds.Contains(new Point((int)(physics.X+21),(int)(physics.Y+47))))) ResetPosition();
        }
        else { accumulator=0; pendingJump=false; }
        if(physics.AttachedSide!=0) facing=physics.AttachedSide;
        else if(physics.WallJumping && physics.VX!=0) facing=Math.Sign(physics.VX);
        monsters.Update(now,(float)elapsed,Lanes(),physics.Body,attack);
        if(smokePath!=null && monsters.Monsters.Count==0 && now<.2) monsters.Spawn(now,Lanes(),physics.Body);
        SyncMonsters();
        Present(); frames++;
        if(smokePath != null && smokeAutoClose && now>=3)
        {
            File.WriteAllText(smokePath,JsonSerializer.Serialize(new { frames,windows=windows.Count,platforms=colliders.Count,physics.X,physics.Y,physics.Grounded,physics.AttachedSide,spriteFrames=art.FrameCount,animation=art.State,controlsActive=controls.Armed,foregroundIsPet=Native.GetForegroundWindow()==Handle,monsterWindows=monsterForms.Count,monitors=Screen.AllScreens.Select(s=>new {s.Bounds,s.WorkingArea}),layeredWindow=true },new JsonSerializerOptions { WriteIndented=true,IncludeFields=true }));
            Close();
        }
    }
    void Present()
    {
        double now=clock.Elapsed.TotalSeconds;
        bool attacking=attack.Visible(now);
        using var sprite=art.Render(now,physics,attacking?attack.Facing:facing,paused||!controls.Armed,debug,attacking?attack.Age(now):-1);
        using var image=CombatArt.Hero(sprite,attack,now,controls.Armed&&!paused);
        Native.Present(Handle,image,(int)Math.Round(physics.X)-KnightArt.BodyLeft-CombatArt.Margin,(int)Math.Round(physics.Y)-KnightArt.BodyTop);
    }
    void OnMouseDown(object? sender,MouseEventArgs e)
    {
        if(e.Button==MouseButtons.Right) { menu.Show(Cursor.Position); return; }
        if(e.Button!=MouseButtons.Left) return;
        ActivateControl();
        mousePressed=true;dragging=false;mouseOrigin=Cursor.Position;Capture=true;
        dragOffset=new Point(Cursor.Position.X-(int)physics.X,Cursor.Position.Y-(int)physics.Y);
    }
    void OnMouseMove(object? sender,MouseEventArgs e)
    {
        if(!mousePressed) return;
        if(!dragging && Math.Abs(Cursor.Position.X-mouseOrigin.X)<SystemInformation.DragSize.Width/2 && Math.Abs(Cursor.Position.Y-mouseOrigin.Y)<SystemInformation.DragSize.Height/2) return;
        dragging=true; attack.Cancel();
        physics.Reset(Cursor.Position.X-dragOffset.X,Cursor.Position.Y-dragOffset.Y); Present();
    }
    void OnMouseUp(object? sender,MouseEventArgs e)
    {
        if(e.Button!=MouseButtons.Left) return;
        bool wasDragging=dragging;dragging=false;mousePressed=false;Capture=false;
        if(wasDragging) { physics.VY=0;RefreshWorld(); }
    }
    protected override void Dispose(bool disposing)
    {
        if(disposing && !resourcesDisposed)
        {
            resourcesDisposed=true;
            timer.Stop(); timer.Dispose();
            activationShortcut?.Dispose();
            if(IsHandleCreated) foreach(int id in hotkeys) Native.UnregisterHotKey(Handle,id);
            hotkeys.Clear();
            tray.Visible=false; tray.Dispose(); menu.Dispose(); Icon?.Dispose();
            art.Dispose();
            foreach(var form in monsterForms.Values) form.Dispose();monsterForms.Clear();monsterArt.Dispose();
        }
        base.Dispose(disposing);
    }
}
