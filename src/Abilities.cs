namespace HollowKnightPet;

internal enum AbilityPhase { None, ShadowDash, DashRecovery, DiveWindup, DiveFall, DiveImpact, ShriekWindup, ShriekBurst, ShriekRecovery }
internal readonly record struct SpecialMotion(float Horizontal,bool FreezeVertical,float? FallSpeed=null);

internal sealed class AbilityState
{
    public const float DashSpeed=20*Physics.PixelsPerUnit*1.5f, DashDuration=.25f, DashRecovery=.12f, DashCooldown=.5f;
    public const float DiveImpactPlaybackRate=1.1f;
    public const float DiveWindup=.25f, DiveSpeed=1600, DiveRecovery=.58f/DiveImpactPlaybackRate, DiveCooldown=.85f;
    public const float ShriekWindup=.1f,ShriekBurst=.65f,ShriekRecovery=.12f,ShriekCooldown=1.1f;
    readonly HashSet<int> hit=[];
    public AbilityPhase Phase {get;private set;}
    public float Age {get;private set;}
    public float DashRemaining {get;private set;}
    public float DiveRemaining {get;private set;}
    public float ShriekRemaining {get;private set;}
    int shriekPulse=-1;
    public int Facing {get;private set;}=1;
    public float ImpactX {get;private set;}
    public float ImpactY {get;private set;}
    public bool Busy=>Phase!=AbilityPhase.None;
    public bool StartDash(Physics p,int facing,int direction=0)
    {
        if(Busy || DashRemaining>0) return false;
        Facing=direction!=0?Math.Sign(direction):p.AttachedSide!=0?-p.AttachedSide:Math.Sign(facing==0?1:facing);
        p.PrepareAbility();Phase=AbilityPhase.ShadowDash;Age=0;DashRemaining=DashCooldown;hit.Clear();return true;
    }
    public bool StartDive(Physics p,int facing)
    {
        if(p.Grounded || (Busy && Phase!=AbilityPhase.DashRecovery) || DiveRemaining>0) return false;
        Facing=Math.Sign(facing==0?1:facing);p.PrepareAbility();Phase=AbilityPhase.DiveWindup;Age=0;
        DiveRemaining=DiveCooldown;hit.Clear();return true;
    }
    public void Cancel() {Phase=AbilityPhase.None;Age=0;hit.Clear();}
    public bool StartShriek(Physics p,int facing)
    {
        if((Busy && Phase!=AbilityPhase.DashRecovery) || ShriekRemaining>0)return false;
        Facing=Math.Sign(facing==0?1:facing);p.PrepareAbility();
        Phase=AbilityPhase.ShriekWindup;Age=0;ShriekRemaining=ShriekCooldown;shriekPulse=-1;hit.Clear();return true;
    }
    public void Step(float dt,Physics p,int direction,bool jump,IReadOnlyList<Platform> platforms,RectangleF area,
        bool held,ScreenEdges? edges,IReadOnlyList<WindowEdge>? walls,IReadOnlyList<Monster> monsters,double now)
    {
        dt=Math.Clamp(dt,0,1f/60);if(dt==0)return;
        DashRemaining=Math.Max(0,DashRemaining-dt);DiveRemaining=Math.Max(0,DiveRemaining-dt);
        ShriekRemaining=Math.Max(0,ShriekRemaining-dt);
        // Repeated float subtraction must not postpone an exact .5-second cooldown.
        if(DashRemaining<.000001f)DashRemaining=0;
        if(DiveRemaining<.000001f)DiveRemaining=0;
        if(ShriekRemaining<.000001f)ShriekRemaining=0;
        var previousPhase=Phase;
        SpecialMotion? motion=Phase switch {
            AbilityPhase.ShadowDash=>new(DashSpeed*Facing,true),
            AbilityPhase.DiveWindup=>new(0,true),
            AbilityPhase.DiveFall=>new(0,false,DiveSpeed),
            AbilityPhase.DiveImpact=>new(0,false),
            AbilityPhase.ShriekWindup or AbilityPhase.ShriekBurst or AbilityPhase.ShriekRecovery=>new(0,true),
            _=>null
        };
        var old=p.Body;
        p.Step(dt,direction,jump,platforms,area,held,edges,walls,motion);
        if(Phase==AbilityPhase.ShadowDash) Damage(monsters,RectangleF.Union(old,p.Body),1,now);
        if(Phase==AbilityPhase.DiveFall)
        {
            Damage(monsters,RectangleF.Union(old,p.Body),1,now);
            if(p.Grounded)
            {
                Phase=AbilityPhase.DiveImpact;Age=0;ImpactX=p.X+Physics.Width/2;ImpactY=p.Y+Physics.Height;hit.Clear();
            }
        }
        if(Phase==AbilityPhase.DiveImpact && Age<.24f)
            Damage(monsters,new RectangleF(ImpactX-170,ImpactY-120,340,138),2,now);
        if(Phase==AbilityPhase.ShriekBurst && Age>=.04f && Age<.52f)
        {
            int pulse=Math.Min(3,(int)((Age-.04f)/.12f));
            if(pulse!=shriekPulse){hit.Clear();shriekPulse=pulse;}
            Damage(monsters,new RectangleF(p.X+Physics.Width/2-150,p.Y+Physics.Height-290,300,310),1,now);
        }
        // Integrate the full last tick before changing phase: 30 ticks at 120 Hz
        // give the intended 300-pixel dash, and exactly .25 seconds of windup.
        if(Busy && Phase==previousPhase) Age+=dt;
        const float epsilon=.000001f;
        if(Phase==AbilityPhase.ShadowDash && Age+epsilon>=DashDuration) {Phase=AbilityPhase.DashRecovery;Age=Math.Max(0,Age-DashDuration);hit.Clear();}
        else if(Phase==AbilityPhase.DashRecovery && Age+epsilon>=DashRecovery) Cancel();
        else if(Phase==AbilityPhase.DiveWindup && Age+epsilon>=DiveWindup) {Phase=AbilityPhase.DiveFall;Age=Math.Max(0,Age-DiveWindup);}
        else if(Phase==AbilityPhase.DiveImpact && Age+epsilon>=DiveRecovery) Cancel();
        else if(Phase==AbilityPhase.ShriekWindup && Age+epsilon>=ShriekWindup){Phase=AbilityPhase.ShriekBurst;Age=Math.Max(0,Age-ShriekWindup);}
        else if(Phase==AbilityPhase.ShriekBurst && Age+epsilon>=ShriekBurst){Phase=AbilityPhase.ShriekRecovery;Age=Math.Max(0,Age-ShriekBurst);}
        else if(Phase==AbilityPhase.ShriekRecovery && Age+epsilon>=ShriekRecovery)Cancel();
    }
    void Damage(IReadOnlyList<Monster> monsters,RectangleF bounds,int damage,double now)
    {
        bounds.Inflate(5,5);
        foreach(var monster in monsters)
        {
            if(monster.Dead || hit.Contains(monster.Id) || !bounds.IntersectsWith(monster.Body)) continue;
            hit.Add(monster.Id);monster.HP=Math.Max(0,monster.HP-damage);monster.HitUntil=now+.18;
            if(monster.Dead) monster.DeadAt=now;
        }
    }
}
