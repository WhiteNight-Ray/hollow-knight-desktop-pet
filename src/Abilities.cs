namespace HollowKnightPet;

internal enum AbilityPhase { None, ShadowDash, DashRecovery, DiveWindup, DiveFall, DiveImpact }
internal readonly record struct SpecialMotion(float Horizontal,bool FreezeVertical,float? FallSpeed=null);

internal sealed class AbilityState
{
    public const float DashSpeed=20*Physics.PixelsPerUnit, DashDuration=.25f, DashRecovery=.12f, DashCooldown=1.5f;
    public const float DiveWindup=.25f, DiveSpeed=1600, DiveRecovery=.58f, DiveCooldown=.85f;
    readonly HashSet<int> hit=[];
    public AbilityPhase Phase {get;private set;}
    public float Age {get;private set;}
    public float DashRemaining {get;private set;}
    public float DiveRemaining {get;private set;}
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
    public void Step(float dt,Physics p,int direction,bool jump,IReadOnlyList<Platform> platforms,RectangleF area,
        bool held,ScreenEdges? edges,IReadOnlyList<WindowEdge>? walls,IReadOnlyList<Monster> monsters,double now)
    {
        dt=Math.Clamp(dt,0,1f/60);if(dt==0)return;
        DashRemaining=Math.Max(0,DashRemaining-dt);DiveRemaining=Math.Max(0,DiveRemaining-dt);
        var previousPhase=Phase;
        SpecialMotion? motion=Phase switch {
            AbilityPhase.ShadowDash=>new(DashSpeed*Facing,true),
            AbilityPhase.DiveWindup=>new(0,true),
            AbilityPhase.DiveFall=>new(0,false,DiveSpeed),
            AbilityPhase.DiveImpact=>new(0,false),
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
        // Integrate the full last tick before changing phase: 30 ticks at 120 Hz
        // give the intended 200-pixel dash, and exactly .25 seconds of windup.
        if(Busy && Phase==previousPhase) Age+=dt;
        const float epsilon=.000001f;
        if(Phase==AbilityPhase.ShadowDash && Age+epsilon>=DashDuration) {Phase=AbilityPhase.DashRecovery;Age=Math.Max(0,Age-DashDuration);hit.Clear();}
        else if(Phase==AbilityPhase.DashRecovery && Age+epsilon>=DashRecovery) Cancel();
        else if(Phase==AbilityPhase.DiveWindup && Age+epsilon>=DiveWindup) {Phase=AbilityPhase.DiveFall;Age=Math.Max(0,Age-DiveWindup);}
        else if(Phase==AbilityPhase.DiveImpact && Age+epsilon>=DiveRecovery) Cancel();
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
