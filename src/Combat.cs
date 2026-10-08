namespace HollowKnightPet;

internal sealed class Monster
{
    public const float Width=62, Height=43;
    public int Id, Facing=1, HP=2;
    public float X, Y, Speed;
    public double Born, HitUntil, DeadAt=-1;
    public RectangleF Lane;
    public RectangleF Body => new(X,Y,Width,Height);
    public bool Dead => HP<=0;
}

internal sealed class AttackState
{
    public const float Duration=.26f, Cooldown=.36f;
    public int Facing { get; private set; } = 1;
    public double Started { get; private set; } = double.NegativeInfinity;
    double nextAllowed;
    readonly HashSet<int> hit=[];
    public bool Start(double now,int facing)
    {
        if(now<nextAllowed) return false;
        Started=now; nextAllowed=now+Cooldown; Facing=facing; hit.Clear(); return true;
    }
    public double Age(double now) => now-Started;
    public bool Visible(double now) => Age(now)>=0 && Age(now)<Duration;
    public bool Damaging(double now) => Age(now)>=.035 && Age(now)<.19;
    public RectangleF Hitbox(RectangleF hero) => new(Facing>0?hero.Right-8:hero.Left-82,hero.Top+28,90,70);
    public void Cancel() { Started=double.NegativeInfinity; hit.Clear(); }
    public bool TryHit(Monster monster,RectangleF hero,double now)
    {
        if(monster.Dead || !Damaging(now) || hit.Contains(monster.Id) || !Hitbox(hero).IntersectsWith(monster.Body)) return false;
        hit.Add(monster.Id); monster.HP--; monster.HitUntil=now+.18;
        monster.X=Math.Clamp(monster.X+Facing*18,monster.Lane.Left+4,monster.Lane.Right-Monster.Width-4);
        if(monster.Dead) monster.DeadAt=now;
        return true;
    }
}

internal sealed class MonsterWorld
{
    public const int MaxMonsters=6;
    readonly Random random;
    public List<Monster> Monsters { get; }=[];
    public double NextSpawn { get; private set; }
    public bool Enabled { get; private set; }
    public int Interval { get; private set; }=15;
    int serial;
    public MonsterWorld(int? seed=null) { random=seed.HasValue?new Random(seed.Value):new Random(); }
    public void Configure(bool enabled,int seconds,double now)
    {
        Enabled=enabled; Interval=Math.Clamp(seconds,2,300);
        NextSpawn=now+Interval;
        if(!enabled) Monsters.Clear();
    }
    public bool Spawn(double now,IReadOnlyList<RectangleF> lanes,RectangleF hero)
    {
        if(!Enabled || Monsters.Count>=MaxMonsters || lanes.Count==0) return false;
        for(int attempt=0;attempt<24;attempt++)
        {
            var lane=lanes[random.Next(lanes.Count)];
            if(lane.Width<Monster.Width+40 || lane.Height<Monster.Height) continue;
            float x=lane.Left+20+(float)random.NextDouble()*(lane.Width-Monster.Width-40);
            var body=new RectangleF(x,lane.Bottom-Monster.Height,Monster.Width,Monster.Height);
            var margin=body; margin.Inflate(50,10);
            if(margin.IntersectsWith(hero) || Monsters.Any(m=>margin.IntersectsWith(m.Body))) continue;
            Monsters.Add(new Monster {Id=++serial,X=x,Y=body.Y,Lane=lane,Born=now,Facing=random.Next(2)==0?-1:1,Speed=18+random.Next(19)});
            return true;
        }
        return false;
    }
    public void Update(double now,float dt,IReadOnlyList<RectangleF> lanes,RectangleF hero,AttackState attack)
    {
        if(!Enabled) return;
        if(now>=NextSpawn)
        {
            Spawn(now,lanes,hero);
            NextSpawn=now+Interval; // Never burst-spawn after sleep, menu dialogs, or a full cap.
        }
        foreach(var monster in Monsters)
        {
            var lane=lanes.FirstOrDefault(r=>r.Left==monster.Lane.Left && r.Top==monster.Lane.Top);
            if(lane.Width<Monster.Width+8) { monster.HP=0; monster.DeadAt=now-1; continue; }
            monster.Lane=lane; monster.Y=lane.Bottom-Monster.Height;
            if(!monster.Dead && now>=monster.HitUntil)
            {
                monster.X+=monster.Facing*monster.Speed*dt;
                if(monster.X<lane.Left+4) { monster.X=lane.Left+4; monster.Facing=1; }
                if(monster.X+Monster.Width>lane.Right-4) { monster.X=lane.Right-Monster.Width-4; monster.Facing=-1; }
            }
            attack.TryHit(monster,hero,now);
        }
        Monsters.RemoveAll(m=>(m.Dead && now-m.DeadAt>.3) || now-m.Born>180);
    }
}
