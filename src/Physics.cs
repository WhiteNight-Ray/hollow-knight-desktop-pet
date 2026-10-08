namespace HollowKnightPet;

internal record Platform(nint Id, RectangleF Bounds, bool Floor = false);
internal readonly record struct ScreenEdges(float? Left, float? Right);
// Side is the direction toward the wall: +1 for a window's left edge, -1 for its right.
internal readonly record struct WindowEdge(nint Id, int Side, float X, float Top, float Bottom, RectangleF WindowBounds);

internal sealed class Physics
{
    public const float Width = 42, Height = 94;
    // Reference Knight prefab: run 8.3, jump 16.65, fall cap 20 units/s.
    // 40 desktop pixels / unit; gravity is calibrated for this desktop scale.
    public const float PixelsPerUnit = 40, RunSpeed = 8.3f * PixelsPerUnit * 1.15f * 1.15f;
    public const float JumpSpeed = 16.65f * PixelsPerUnit, FallCap = 20 * PixelsPerUnit;
    public const float Gravity = 60 * .79f * PixelsPerUnit;
    public const float MinRiseTime = .08f, MaxRiseTime = .20f;
    public const float SnapDistance = 18, EdgeInset = 12;
    public const float WindowGap = 6;
    public float X, Y, VX, VY;
    public bool Grounded;
    public nint Support;
    public bool SolidWindows;
    public bool AutoAttach = true;
    public int AttachedSide { get; private set; }
    public nint AttachedWindow { get; private set; }
    public bool WallJumping => wallKickTime > 0;
    public RectangleF Body => new(X, Y, Width, Height);
    private readonly HashSet<nint> embedded = [];
    private float coyote, jumpBuffer;
    private float riseTime, attachCooldown, wallKickTime;
    private bool rising;
    private float windowOffsetY;

    public void Reset(float x, float y)
    {
        X = x; Y = y; VX = VY = 0; Support = 0; Grounded = false;
        coyote = jumpBuffer = riseTime = attachCooldown = wallKickTime = 0;
        rising = false; AttachedSide = 0; AttachedWindow = 0; embedded.Clear();
    }

    public void ReleaseEdge()
    {
        AttachedSide = 0; AttachedWindow = 0; attachCooldown = .25f; Grounded = false; Support = 0;
    }

    public void CancelInput()
    {
        VX=0; jumpBuffer=0; wallKickTime=0; rising=false;
        if(VY<0) VY=0;
    }

    private void HoldEdge()
    {
        VX = VY = 0; Grounded = false; Support = 0; rising = false; coyote = jumpBuffer = 0;
    }

    private static float WindowAttachX(WindowEdge edge) => edge.Side > 0 ? edge.X - Width - WindowGap : edge.X + WindowGap;

    private bool TryAttachWindow(IReadOnlyList<WindowEdge> walls, int direction, float oldX, RectangleF area)
    {
        if (!AutoAttach || Grounded || attachCooldown > 0) return false;
        WindowEdge? nearest = null;
        float distance = float.MaxValue;
        float contactY = Y + Height * .5f;
        foreach (var wall in walls)
        {
            if (direction == -wall.Side || embedded.Contains(wall.Id)) continue;
            if (contactY < wall.Top + 1 || contactY >= wall.Bottom - 1) continue;
            // Only approach from outside. Being in front of a window must not teleport
            // the pet through its contents to the far side.
            if (wall.Side > 0 ? oldX + Width > wall.X + .5f : oldX < wall.X - .5f) continue;
            float targetX = WindowAttachX(wall);
            if (targetX < area.Left || targetX + Width > area.Right) continue;
            float delta = Math.Abs(X - targetX);
            bool crossed = wall.Side > 0 ? oldX <= targetX && X >= targetX : oldX >= targetX && X <= targetX;
            if ((delta <= SnapDistance || crossed) && delta < distance) { nearest = wall; distance = delta; }
        }
        if (!nearest.HasValue) return false;
        var chosen = nearest.Value;
        X = WindowAttachX(chosen); AttachedSide = chosen.Side; AttachedWindow = chosen.Id;
        windowOffsetY = Y - chosen.WindowBounds.Top; HoldEdge();
        return true;
    }

    private bool TryAttach(ScreenEdges edges, int direction)
    {
        if (!AutoAttach || Grounded || attachCooldown > 0) return false;
        float left = edges.Left.GetValueOrDefault() + EdgeInset;
        float right = edges.Right.GetValueOrDefault() - Width - EdgeInset;
        if (edges.Left.HasValue && X <= left + SnapDistance && direction <= 0)
        { X = left; AttachedSide = -1; }
        else if (edges.Right.HasValue && X >= right - SnapDistance && direction >= 0)
        { X = right; AttachedSide = 1; }
        else return false;
        AttachedWindow = 0; HoldEdge();
        return true;
    }

    public void Step(float dt, int direction, bool jump, IReadOnlyList<Platform> platforms, RectangleF area,
                     bool jumpHeld = true, ScreenEdges? edges = null, IReadOnlyList<WindowEdge>? walls = null)
    {
        dt = Math.Clamp(dt, 0, 1f / 60);
        if (dt == 0) return;
        attachCooldown = Math.Max(0, attachCooldown - dt);
        wallKickTime = Math.Max(0, wallKickTime - dt);
        if (AttachedSide != 0)
        {
            int side = AttachedSide;
            float? targetX = null;
            float targetY = Y;
            if (AttachedWindow != 0)
            {
                foreach (var wall in walls ?? [])
                {
                    if (wall.Id != AttachedWindow || wall.Side != side) continue;
                    float newY = wall.WindowBounds.Top + windowOffsetY;
                    float contactY = newY + Height * .5f;
                    float newX = WindowAttachX(wall);
                    if (contactY < wall.Top + 1 || contactY >= wall.Bottom - 1 || newX < area.Left || newX + Width > area.Right || newY < area.Top || newY + Height > area.Bottom) continue;
                    targetX = newX; targetY = newY; break;
                }
            }
            else
            {
                float? edge = side < 0 ? edges?.Left : edges?.Right;
                if (edge.HasValue) targetX = side < 0 ? edge.Value + EdgeInset : edge.Value - Width - EdgeInset;
            }
            if (!AutoAttach || !targetX.HasValue) ReleaseEdge();
            else if (jump || direction == -side)
            {
                X = targetX.Value; Y = targetY;
                ReleaseEdge();
                if (jump)
                {
                    VX = -side * 16 * PixelsPerUnit; VY = -JumpSpeed;
                    rising = true; riseTime = 0; wallKickTime = .10f;
                    jump = false; jumpBuffer = coyote = 0;
                }
            }
            else
            {
                X = targetX.Value;
                Y = Math.Clamp(targetY, area.Top, Math.Max(area.Top, area.Bottom - Height));
                VX = VY = 0; return;
            }
        }
        // A moved/maximized window may envelop the pet. Let it leave naturally.
        embedded.RemoveWhere(id => !platforms.Any(p => p.Id == id && p.Bounds.IntersectsWith(Body)));
        foreach (var p in platforms)
            if (!p.Floor && p.Bounds.IntersectsWith(new RectangleF(X + .5f, Y + .5f, Width - 1, Height - 1)))
                embedded.Add(p.Id);

        coyote = Grounded ? .08f : Math.Max(0, coyote - dt);
        jumpBuffer = jump ? .10f : Math.Max(0, jumpBuffer - dt);
        // The original controller sets horizontal velocity directly, including in air.
        if (wallKickTime <= 0) VX = direction * RunSpeed;
        if (jumpBuffer > 0 && coyote > 0)
        {
            VY = -JumpSpeed; rising = true; riseTime = 0;
            Grounded = false; Support = 0; coyote = jumpBuffer = 0;
        }
        float oldX = X, oldY = Y;
        X += VX * dt;
        if (SolidWindows)
        {
            foreach (var p in platforms.Where(p => !p.Floor && !embedded.Contains(p.Id)))
            {
                var r = p.Bounds;
                if (Y + Height <= r.Top + .1f || Y >= r.Bottom - .1f) continue;
                if (VX > 0 && oldX + Width <= r.Left + .1f && X + Width >= r.Left)
                { X = Math.Min(X, r.Left - Width); VX = 0; }
                else if (VX < 0 && oldX >= r.Right - .1f && X <= r.Right)
                { X = Math.Max(X, r.Right); VX = 0; }
            }
        }
        X = Math.Clamp(X, area.Left, Math.Max(area.Left, area.Right - Width));
        if (edges.HasValue && TryAttach(edges.Value, direction))
        {
            Y = Math.Clamp(Y, area.Top, Math.Max(area.Top, area.Bottom - Height));
            return;
        }
        if (walls != null && TryAttachWindow(walls, direction, oldX, area)) return;
        if (!jumpHeld && VY < 0 && riseTime >= MinRiseTime) { VY = 0; rising = false; }
        if (rising)
        {
            // Hold upward speed for a bounded rise phase. Release after the minimum
            // rise cancels upward velocity; a long hold continues into a ballistic arc.
            if (!jumpHeld && riseTime >= MinRiseTime) { VY = 0; rising = false; }
            else if (riseTime < MaxRiseTime) VY = -JumpSpeed;
            else rising = false;
            riseTime += dt;
        }
        VY = Math.Min(VY + Gravity * dt, FallCap);
        float nextY = Y + VY * dt;
        Grounded = false; Support = 0;
        foreach (var p in platforms)
        {
            var r = p.Bounds;
            if (X + Width <= r.Left || X >= r.Right) continue;
            if (VY >= 0 && oldY + Height <= r.Top + .6f && nextY + Height >= r.Top)
            {
                nextY = r.Top - Height;
                VY = 0; Grounded = true; Support = p.Id; rising = false;
            }
            else if (SolidWindows && !p.Floor && !embedded.Contains(p.Id) && VY < 0 &&
                     oldY >= r.Bottom - .1f && nextY <= r.Bottom)
            { nextY = r.Bottom; VY = 0; rising = false; }
        }
        Y = nextY;
        if (Y < area.Top) { Y = area.Top; VY = Math.Max(0, VY); rising = false; }
        if (Y + Height > area.Bottom)
        { Y = area.Bottom - Height; VY = 0; Grounded = true; Support = 0; rising = false; }
    }
}
