namespace AlienCat.Core.Pet;

/// <summary>
/// Decides what the cat does and where it is.
/// On the ground <see cref="Position"/> is the bottom-centre of the sprite; on walls and glass it is the sprite centre;
/// on a thread it is the end of the thread (top-centre of the upside-down sprite).
/// </summary>
public sealed class PetBrain
{
    public const double WalkSpeed = 60;
    public const double RunSpeed = 140;
    public const double ChaseSpeed = 120;
    public const double ToyMouseSpeed = 140;
    public const double Gravity = 1800;
    public const double FlyHeight = 120;
    public const double FlySpeed = 80;
    public const double CursorStillNeeded = 2;
    public const double CursorReach = 200;
    public static readonly TimeSpan CursorCooldown = TimeSpan.FromMinutes(10);
    public const double MaxGameSeconds = 25;
    public const double MaxCursorHuntSeconds = 8;

    // screen pounce
    public const double PounceBoost = 2;
    public const double PounceLeapSeconds = 0.5;
    public const double PounceClingSeconds = 0.6;
    public const double PounceSlideSpeed = 70;
    public const double PounceDropSeconds = 0.35;
    /// <summary>Claw tips relative to the sprite centre, as a fraction of the drawn sprite size (two per front paw).</summary>
    public static readonly Vec[] ClawOffsets = [new(-0.30, -0.20), new(-0.26, -0.20), new(0.26, -0.20), new(0.30, -0.20)];

    // walls
    public const double ClimbSpeed = 55;
    public const double WallDescendSpeed = 60;

    // web
    public const double RiseSpeed = 170;
    public const double ThreadSpeed = 80;
    public const double ThreadClimbSpeed = 90;
    public const double MaxSwing = 0.45;   // radians

    // cursor steal
    public const double StealIdleSeconds = 10;
    public const double StealReachAbove = 180;
    public const double StealMaxSeconds = 3;
    public const double StealMaxDistance = 300;
    public const double StealGrabSeconds = 0.3;
    public const double CarrySpeed = 100;

    // away
    public const double AwayMinSeconds = 60;
    public const double AwayMaxSeconds = 300;
    public const double AwayWalkSpeed = 90;
    public const double ReturnRunSpeed = 200;

    public const double OutfitChangeSeconds = 1.0;

    readonly IRandom _rng;
    double _duration;
    double _vy;
    DateTimeOffset _nextGameAt;
    DateTimeOffset? _lastCursorHunt;
    bool _reminderQueued;
    double _phaseTime;
    bool _caught;
    int _toyDirection = 1;
    PetContext? _last;

    TrickSchedule? _schedule;
    OutfitPicker? _outfits;
    Outfit? _pendingOutfit;
    Vec _from;
    Vec _target;
    double _hold;
    double _threadLength;
    double _threadTarget;
    double _swingSeconds;
    Vec _stealOrigin;
    double _stealTime;
    double _goneSeconds;
    bool _returnFast;

    public PetBrain(IRandom rng, DateTimeOffset start, Vec position)
    {
        _rng = rng;
        Position = position;
        _nextGameAt = start + NextGameInterval(Storage.ActivityLevel.Normal);
        _duration = R(3, 8);
    }

    public PetActivity Activity { get; private set; } = PetActivity.Idle;
    public HuntPhase Phase { get; private set; } = HuntPhase.Crouch;
    public TrickPhase Trick { get; private set; } = TrickPhase.None;
    public Surface Surface { get; private set; } = Surface.Ground;
    public Vec Position { get; set; }
    public int Facing { get; set; } = 1;
    public bool Sad { get; private set; }
    public bool Nagging { get; set; }
    public double ActivityTime { get; private set; }
    public Vec? ToyPosition { get; private set; }

    /// <summary>Extra size factor while pouncing at the screen (1 otherwise).</summary>
    public double Boost { get; private set; } = 1;
    /// <summary>Top end of the web thread, while hanging.</summary>
    public Vec? ThreadAnchor { get; private set; }
    /// <summary>Where the cat stuck to the glass; scratches run from here to the current position.</summary>
    public Vec? GlassStart { get; private set; }
    /// <summary>Where the app should put the real mouse cursor this frame, while the cat carries it.</summary>
    public Vec? CursorTarget { get; private set; }

    public Outfit Outfit => _outfits?.Current ?? Outfit.None;

    public bool IsVisible =>
        Activity != PetActivity.Hidden
        && !(Trick == TrickPhase.Gone && Activity is PetActivity.Away or PetActivity.WebHang);

    public event Action<PetActivity>? ActivityChanged;

    /// <summary>Where the cat's mouth is on the ground (cursor-carrying point).</summary>
    public Vec Mouth(double spriteSize) => new(Position.X + Facing * 0.2 * spriteSize, Position.Y - 0.4 * spriteSize);

    static bool Allowed(PetActivity a, TrickSettings t) => a switch
    {
        PetActivity.ScreenPounce => t.ScreenPounce,
        PetActivity.WallClimb => t.WallClimb,
        PetActivity.WebHang => t.WebHang,
        PetActivity.CursorSteal => t.CursorSteal,
        PetActivity.Away => t.Away,
        _ => true,
    };

    static bool IsTrick(PetActivity a) =>
        a is PetActivity.ScreenPounce or PetActivity.WallClimb or PetActivity.WebHang
            or PetActivity.CursorSteal or PetActivity.Away;

    // ------------------------------------------------------------------ input

    public void BeginDrag(Vec p)
    {
        if (!IsVisible) return;
        if (Activity == PetActivity.Remind) _reminderQueued = true;
        CleanupTrick();
        Position = p;
        Set(PetActivity.Dragged, double.MaxValue);
    }

    public void DragTo(Vec p)
    {
        if (Activity == PetActivity.Dragged) Position = p;
    }

    public void EndDrag(PetContext ctx)
    {
        if (Activity != PetActivity.Dragged) return;
        var ground = ctx.WorkArea.Bottom;
        if (Position.Y < ground - 1)
        {
            _vy = 0;
            Set(PetActivity.Fall, double.MaxValue);
        }
        else
        {
            Position = Position with { Y = ground };
            ToIdleOrReminder();
        }
    }

    public void Click()
    {
        if (Activity is PetActivity.Hidden or PetActivity.Dragged or PetActivity.Remind or PetActivity.Fall
            || IsTrick(Activity)) return;
        Set(PetActivity.Purr, 2);
    }

    /// <summary>«Позвать котика»: come back from wherever he is.</summary>
    public void Call()
    {
        if (Activity == PetActivity.Away)
        {
            switch (Trick)
            {
                case TrickPhase.Gone:
                    _returnFast = true;
                    _phaseTime = _goneSeconds;      // return on the next update
                    break;
                case TrickPhase.Leave:
                    Facing = -Facing;
                    _target = new Vec(CenterX(), 0);
                    _returnFast = true;
                    Trick = TrickPhase.Return;
                    break;
                case TrickPhase.FlyUp:
                    FallFromHere(Position);
                    break;
                case TrickPhase.Return:
                    _returnFast = true;
                    break;
            }
            return;
        }
        if (IsTrick(Activity)) ComeDown();
    }

    public void StartReminder()
    {
        if (Activity is PetActivity.Dragged or PetActivity.Fall or PetActivity.Fly or PetActivity.Hidden)
        {
            _reminderQueued = true;
            return;
        }
        if (Activity == PetActivity.Away)
        {
            _reminderQueued = true;
            Call();
            return;
        }
        if (IsTrick(Activity))
        {
            _reminderQueued = true;
            ComeDown();
            if (Activity == PetActivity.Fall) return;   // reminds after landing
            _reminderQueued = false;
        }
        if (Activity == PetActivity.ChangeOutfit) WearPending();
        if (_last is not null) Position = Position with { Y = _last.WorkArea.Bottom };
        Set(PetActivity.Remind, double.MaxValue);
    }

    public void EndReminder(bool done)
    {
        _reminderQueued = false;
        Nagging = false;
        if (Activity == PetActivity.Remind || Activity == PetActivity.Dragged && done)
        {
            if (done) Set(PetActivity.Happy, 2.5);
            else ToIdle();
        }
    }

    /// <summary>The app noticed the user moving the mouse while the cat holds the cursor.</summary>
    public void UserMovedCursor()
    {
        if (Activity != PetActivity.CursorSteal) return;
        CursorTarget = null;
        Trick = TrickPhase.None;
        ToIdleOrReminder();
    }

    // ----------------------------------------------------------------- update

    public void Update(PetContext ctx, double dt)
    {
        _last = ctx;
        var ground = ctx.WorkArea.Bottom;
        var half = ctx.SpriteSize / 2;
        var minX = ctx.WorkArea.Left + half;
        var maxX = ctx.WorkArea.Right - half;

        if (ctx.OtherAppFullscreen)
        {
            if (Activity == PetActivity.Away && Trick == TrickPhase.Gone) { Sad = false; return; }
            if (Activity is not (PetActivity.Hidden or PetActivity.Dragged))
            {
                if (Activity == PetActivity.Remind) _reminderQueued = true;
                CleanupTrick();
                Position = new Vec(Math.Clamp(Position.X, minX, Math.Max(minX, maxX)), ground);
                ToyPosition = null;
                Set(PetActivity.Hidden, double.MaxValue);
            }
            Sad = false;
            return;
        }
        if (Activity == PetActivity.Hidden)
        {
            ToIdleOrReminder();
            return;
        }

        // a trick switched off in the settings stops at once
        if (ctx.Tricks is { } allowed && IsTrick(Activity) && !Allowed(Activity, allowed))
        {
            if (Activity == PetActivity.Away) Call();
            else ComeDown();
        }

        ActivityTime += dt;
        _phaseTime += dt;

        switch (Activity)
        {
            case PetActivity.Dragged:
            case PetActivity.Remind:
                break;

            case PetActivity.Fall:
                _vy += Gravity * dt;
                var y = Position.Y + _vy * dt;
                if (y >= ground)
                {
                    Position = Position with { Y = ground };
                    Set(PetActivity.Land, 0.4);
                }
                else Position = Position with { Y = y };
                break;

            case PetActivity.Walk:
                StepX(WalkSpeed * dt, minX, maxX);
                break;

            case PetActivity.Fly:
                UpdateFly(ground, dt, minX, maxX);
                break;

            case PetActivity.YarnPlay:
                UpdateYarn(ctx, dt, minX, maxX);
                break;

            case PetActivity.ButterflyHunt:
                UpdateButterfly(ctx, dt);
                break;

            case PetActivity.MouseChase:
                UpdateMouseChase(ctx, dt, minX, maxX);
                break;

            case PetActivity.CursorHunt:
                UpdateCursorHunt(ctx, dt, minX, maxX);
                break;

            case PetActivity.ScreenPounce:
                UpdatePounce(ctx, dt);
                break;

            case PetActivity.WallClimb:
                UpdateWall(ctx, dt, minX, maxX);
                break;

            case PetActivity.WebHang:
                UpdateWeb(ctx, dt, minX, maxX);
                break;

            case PetActivity.CursorSteal:
                UpdateSteal(ctx, dt, minX, maxX);
                break;

            case PetActivity.Away:
                UpdateAway(ctx, dt);
                break;

            case PetActivity.ChangeOutfit:
                if (ActivityTime >= OutfitChangeSeconds / 2) WearPending();
                break;
        }

        var free = Activity is PetActivity.Dragged or PetActivity.Fall or PetActivity.Fly
            or PetActivity.Away or PetActivity.WebHang or PetActivity.ScreenPounce or PetActivity.WallClimb;
        if (!free && Surface == Surface.Ground)
            Position = new Vec(Math.Clamp(Position.X, minX, Math.Max(minX, maxX)), ground);

        if (ActivityTime >= _duration && Activity is not (PetActivity.Fly or PetActivity.Remind or PetActivity.Dragged or PetActivity.Fall)
            && !IsTrick(Activity))
        {
            if (Activity == PetActivity.ChangeOutfit) WearPending();
            if (Activity == PetActivity.Idle) ChooseNext(ctx);
            else ToIdleOrReminder();
        }

        if (Activity == PetActivity.Idle) MaybeChangeOutfit(ctx);

        if (Activity is PetActivity.Idle or PetActivity.Sit or PetActivity.Walk or PetActivity.Groom && CanHuntCursor(ctx))
        {
            _lastCursorHunt = ctx.Now;
            Phase = HuntPhase.Crouch;
            Facing = ctx.Cursor!.Value.X >= Position.X ? 1 : -1;
            Set(PetActivity.CursorHunt, MaxCursorHuntSeconds);
        }

        Sad = ctx.HasOverdue && Activity is PetActivity.Idle or PetActivity.Sit;
    }

    // ----------------------------------------------------------------- tricks

    /// <summary>Starts a trick now if it is possible; false when conditions are not met (used by the scheduler and tests).</summary>
    internal bool BeginTrick(PetActivity trick, PetContext ctx)
    {
        _last = ctx;
        switch (trick)
        {
            case PetActivity.ScreenPounce:
            {
                var a = ctx.WorkArea;
                var w = a.Right - a.Left;
                var h = a.Bottom - a.Top;
                _from = new Vec(Position.X, a.Bottom - ctx.SpriteSize / 2);
                _target = new Vec(R(a.Left + 0.25 * w, a.Right - 0.25 * w), R(a.Top + 0.35 * h, a.Top + 0.55 * h));
                Surface = Surface.Glass;
                Position = _from;
                Begin(PetActivity.ScreenPounce, TrickPhase.Leap);
                return true;
            }
            case PetActivity.WallClimb:
            {
                var minX = ctx.WorkArea.Left + ctx.SpriteSize / 2;
                var maxX = ctx.WorkArea.Right - ctx.SpriteSize / 2;
                Facing = Position.X - minX <= maxX - Position.X ? -1 : 1;
                Begin(PetActivity.WallClimb, TrickPhase.Approach);
                return true;
            }
            case PetActivity.WebHang:
                Begin(PetActivity.WebHang, TrickPhase.FlyUp);
                return true;
            case PetActivity.CursorSteal:
            {
                if (!CanSteal(ctx)) return false;
                _stealOrigin = ctx.Cursor!.Value;
                Facing = _stealOrigin.X >= Position.X ? 1 : -1;
                _stealTime = 0;
                Begin(PetActivity.CursorSteal, TrickPhase.Approach);
                return true;
            }
            case PetActivity.Away:
            {
                _returnFast = false;
                Facing = Position.X <= (ctx.WorkArea.Left + ctx.WorkArea.Right) / 2 ? -1 : 1;
                Begin(PetActivity.Away, _rng.NextDouble() < 0.5 ? TrickPhase.Leave : TrickPhase.FlyUp);
                return true;
            }
            default:
                return false;
        }
    }

    void Begin(PetActivity activity, TrickPhase phase)
    {
        Phase = HuntPhase.Crouch;
        ToyPosition = null;
        Set(activity, double.MaxValue);
        Trick = phase;
        _phaseTime = 0;
    }

    void Next(TrickPhase phase)
    {
        Trick = phase;
        _phaseTime = 0;
    }

    bool TryStartScheduledTrick(PetContext ctx)
    {
        if (ctx.Tricks is not { } tricks) return false;
        _schedule ??= new TrickSchedule(_rng, ctx.Now, ctx.Level);
        foreach (var kind in new[] { TrickKind.Away, TrickKind.ScreenPounce, TrickKind.CursorSteal, TrickKind.Climb })
        {
            if (!_schedule.IsDue(kind, ctx.Now)) continue;
            if (!tricks.Enabled(kind))
            {
                _schedule.Done(kind, ctx.Now, ctx.Level);
                continue;
            }
            var activity = kind switch
            {
                TrickKind.Away => PetActivity.Away,
                TrickKind.ScreenPounce => PetActivity.ScreenPounce,
                TrickKind.CursorSteal => PetActivity.CursorSteal,
                _ => tricks.WallClimb && tricks.WebHang
                    ? (_rng.NextDouble() < 0.5 ? PetActivity.WallClimb : PetActivity.WebHang)
                    : tricks.WallClimb ? PetActivity.WallClimb : PetActivity.WebHang,
            };
            if (BeginTrick(activity, ctx))
            {
                _schedule.Done(kind, ctx.Now, ctx.Level);
                return true;
            }
            // cursor steal not possible right now: stays due, tried again later
        }
        return false;
    }

    void UpdatePounce(PetContext ctx, double dt)
    {
        var size = ctx.SpriteSize;
        var ground = ctx.WorkArea.Bottom;
        switch (Trick)
        {
            case TrickPhase.Leap:
            {
                var t = Math.Min(1, _phaseTime / PounceLeapSeconds);
                var e = 1 - (1 - t) * (1 - t);
                Position = new Vec(_from.X + (_target.X - _from.X) * e,
                    _from.Y + (_target.Y - _from.Y) * e - 60 * Math.Sin(Math.PI * t));
                Boost = 1 + (PounceBoost - 1) * e;
                if (t >= 1)
                {
                    Position = _target;
                    Boost = PounceBoost;
                    GlassStart = _target;
                    Next(TrickPhase.Cling);
                }
                break;
            }
            case TrickPhase.Cling:
                if (_phaseTime >= PounceClingSeconds) Next(TrickPhase.Slide);
                break;
            case TrickPhase.Slide:
                Position = Position with { Y = Position.Y + PounceSlideSpeed * dt };
                if (Position.Y + size * Boost / 2 >= ground)
                {
                    _from = Position;
                    Next(TrickPhase.Drop);
                }
                break;
            case TrickPhase.Drop:
            {
                var t = Math.Min(1, _phaseTime / PounceDropSeconds);
                Boost = PounceBoost - (PounceBoost - 1) * t;
                Position = Position with { Y = _from.Y + (ground - size / 2 - _from.Y) * t };
                if (t >= 1)
                {
                    LandOnGround(new Vec(Position.X, ground));
                    Set(PetActivity.Land, 0.4);
                }
                break;
            }
        }
    }

    void UpdateWall(PetContext ctx, double dt, double minX, double maxX)
    {
        var ground = ctx.WorkArea.Bottom;
        var size = ctx.SpriteSize;
        switch (Trick)
        {
            case TrickPhase.Approach:
            {
                var x = Position.X + Facing * RunSpeed * dt;
                if (x <= minX || x >= maxX)
                {
                    x = Math.Clamp(x, minX, maxX);
                    Surface = Facing < 0 ? Surface.LeftWall : Surface.RightWall;
                    Position = new Vec(x, ground - size / 2);
                    var h = ground - ctx.WorkArea.Top;
                    _target = new Vec(x, Math.Max(ctx.WorkArea.Top + size / 2, Position.Y - R(0.3, 0.66) * h));
                    Next(TrickPhase.Climb);
                }
                else Position = Position with { X = x };
                break;
            }
            case TrickPhase.Climb:
                Position = Position with { Y = Position.Y - ClimbSpeed * dt };
                if (Position.Y <= _target.Y)
                {
                    Position = Position with { Y = _target.Y };
                    _hold = R(3, 6);
                    Next(TrickPhase.Hold);
                }
                break;
            case TrickPhase.Hold:
                if (_phaseTime >= _hold)
                {
                    if (_rng.NextDouble() < 0.6) Next(TrickPhase.Descend);
                    else JumpOffWall(ctx);
                }
                break;
            case TrickPhase.Descend:
                Position = Position with { Y = Position.Y + WallDescendSpeed * dt };
                if (Position.Y >= ground - size / 2)
                {
                    LandOnGround(new Vec(Position.X, ground));
                    ToIdleOrReminder();
                }
                break;
        }
    }

    void JumpOffWall(PetContext ctx)
    {
        var inward = Surface == Surface.LeftWall ? 1 : -1;
        var size = ctx.SpriteSize;
        var bottom = new Vec(Position.X + inward * size * 0.4, Position.Y + size / 2);
        FallFromHere(bottom);
        Facing = inward;
        _vy = -250;
    }

    void UpdateWeb(PetContext ctx, double dt, double minX, double maxX)
    {
        var top = ctx.WorkArea.Top;
        switch (Trick)
        {
            case TrickPhase.FlyUp:
                Position = new Vec(Position.X + Facing * 15 * dt, Position.Y - RiseSpeed * dt);
                if (Position.Y < top - 8)
                {
                    _goneSeconds = 1.5;
                    Next(TrickPhase.Gone);
                }
                break;
            case TrickPhase.Gone:
                if (_phaseTime >= _goneSeconds)
                {
                    var anchor = new Vec(R(minX, Math.Max(minX, maxX)), top);
                    ThreadAnchor = anchor;
                    Surface = Surface.Thread;
                    _threadLength = 0;
                    _threadTarget = R(0.35, 0.6) * (ctx.WorkArea.Bottom - top);
                    Position = anchor;
                    Next(TrickPhase.Descend);
                }
                break;
            case TrickPhase.Descend:
                _threadLength = Math.Min(_threadTarget, _threadLength + ThreadSpeed * dt);
                Position = ThreadAnchor!.Value with { Y = ThreadAnchor.Value.Y + _threadLength };
                if (_threadLength >= _threadTarget)
                {
                    _swingSeconds = R(8, 14);
                    Next(TrickPhase.Swing);
                }
                break;
            case TrickPhase.Swing:
            {
                var t = _phaseTime;
                var amp = MaxSwing * Math.Sin(Math.PI * Math.Min(1, t / _swingSeconds));
                var period = 1.6 + _threadLength / 400;
                var theta = amp * Math.Sin(2 * Math.PI * t / period);
                var a = ThreadAnchor!.Value;
                Position = new Vec(a.X + Math.Sin(theta) * _threadLength, a.Y + Math.Cos(theta) * _threadLength);
                if (t >= _swingSeconds) Next(TrickPhase.Ascend);
                break;
            }
            case TrickPhase.Ascend:
            {
                _threadLength = Math.Max(0, _threadLength - ThreadClimbSpeed * dt);
                var a = ThreadAnchor!.Value;
                Position = new Vec(a.X, a.Y + _threadLength);
                if (_threadLength <= 0)
                {
                    // back above the screen edge, then drop down to the floor
                    FallFromHere(new Vec(a.X, top));
                }
                break;
            }
        }
    }

    bool CanSteal(PetContext ctx)
    {
        if (ctx.OtherAppFullscreen || ctx.MouseButtonDown || ctx.UserIdleSeconds < StealIdleSeconds || ctx.Cursor is not { } c) return false;
        var a = ctx.WorkArea;
        if (c.X < a.Left || c.X > a.Right) return false;
        var above = a.Bottom - 0.4 * ctx.SpriteSize - c.Y;
        return above >= -20 && above <= StealReachAbove;
    }

    void UpdateSteal(PetContext ctx, double dt, double minX, double maxX)
    {
        var size = ctx.SpriteSize;
        if (ctx.MouseButtonDown && Trick is TrickPhase.Grab or TrickPhase.Carry) { Abort(); return; }
        switch (Trick)
        {
            case TrickPhase.Approach:
            {
                // any sign of the user (input, a moved cursor, a held button) cancels before we touch the cursor
                if (ctx.UserIdleSeconds < 2 || _phaseTime > 8 || ctx.MouseButtonDown
                    || ctx.Cursor is not { } now || now.DistanceTo(_stealOrigin) > 4) { Abort(); return; }
                var targetX = Math.Clamp(_stealOrigin.X - Facing * 0.2 * size, minX, maxX);
                var dx = targetX - Position.X;
                var step = RunSpeed * dt;
                if (Math.Abs(dx) <= step)
                {
                    Position = Position with { X = targetX };
                    Next(TrickPhase.Grab);
                }
                else
                {
                    Facing = dx > 0 ? 1 : -1;
                    Position = Position with { X = Position.X + Facing * step };
                }
                break;
            }
            case TrickPhase.Grab:
            {
                _stealTime += dt;
                var t = Math.Min(1, _phaseTime / StealGrabSeconds);
                var mouth = Mouth(size);
                CursorTarget = new Vec(_stealOrigin.X + (mouth.X - _stealOrigin.X) * t, _stealOrigin.Y + (mouth.Y - _stealOrigin.Y) * t);
                if (t >= 1)
                {
                    Facing = -Facing;   // turn round and run off with it
                    CursorTarget = Mouth(size);
                    Next(TrickPhase.Carry);
                }
                break;
            }
            case TrickPhase.Carry:
            {
                var before = Position;
                StepX(CarrySpeed * dt, minX, maxX);
                var mouth = Mouth(size);
                var tooFar = mouth.DistanceTo(_stealOrigin) > StealMaxDistance;
                if (tooFar)
                {
                    Position = before;
                    mouth = Mouth(size);
                }
                _stealTime += dt;
                if (tooFar || _stealTime + dt > StealMaxSeconds || Position.X <= minX || Position.X >= maxX)
                {
                    CursorTarget = null;
                    Trick = TrickPhase.None;
                    Set(PetActivity.Purr, 2);   // very pleased with itself
                    return;
                }
                CursorTarget = mouth;
                break;
            }
        }

        void Abort()
        {
            CursorTarget = null;
            Trick = TrickPhase.None;
            ToIdleOrReminder();
        }
    }

    void UpdateAway(PetContext ctx, double dt)
    {
        var a = ctx.WorkArea;
        var size = ctx.SpriteSize;
        switch (Trick)
        {
            case TrickPhase.Leave:
                Position = Position with { X = Position.X + Facing * AwayWalkSpeed * dt };
                if (Position.X < a.Left - size || Position.X > a.Right + size) GoAway();
                break;
            case TrickPhase.FlyUp:
                Position = new Vec(Position.X + Facing * 20 * dt, Position.Y - RiseSpeed * dt);
                if (Position.Y < a.Top - 8) GoAway();
                break;
            case TrickPhase.Gone:
                if (_phaseTime >= _goneSeconds)
                {
                    var fromLeft = _rng.NextDouble() < 0.5;
                    Position = new Vec(fromLeft ? a.Left - size * 0.45 : a.Right + size * 0.45, a.Bottom);
                    Facing = fromLeft ? 1 : -1;
                    var w = a.Right - a.Left;
                    _target = new Vec(R(a.Left + 0.25 * w, a.Right - 0.25 * w), a.Bottom);
                    // sometimes comes back in different clothes
                    if (_outfits is not null && _outfits.Desired(ctx.Now, ctx.Zone, ctx.OutfitMode) is { } o) _outfits.Wear(o);
                    Next(TrickPhase.Return);
                }
                break;
            case TrickPhase.Return:
            {
                var speed = _returnFast || _reminderQueued ? ReturnRunSpeed : AwayWalkSpeed;
                Position = new Vec(Position.X + Facing * speed * dt, a.Bottom);
                if (Facing > 0 ? Position.X >= _target.X : Position.X <= _target.X)
                {
                    Trick = TrickPhase.None;
                    _returnFast = false;
                    ToIdleOrReminder();
                }
                break;
            }
        }

        void GoAway()
        {
            _goneSeconds = _returnFast || _reminderQueued ? 0 : R(AwayMinSeconds, AwayMaxSeconds);
            Next(TrickPhase.Gone);
        }
    }

    double CenterX() => _last is null ? Position.X : (_last.WorkArea.Left + _last.WorkArea.Right) / 2;

    /// <summary>Abandons a trick and gets the cat back to the floor (used by reminders and «Позвать котика»).</summary>
    void ComeDown()
    {
        var size = _last?.SpriteSize ?? 128;
        switch (Activity)
        {
            case PetActivity.ScreenPounce:
                FallFromHere(new Vec(Position.X, Position.Y + size * Boost / 2));
                break;
            case PetActivity.WallClimb:
                if (Surface == Surface.Ground) { Trick = TrickPhase.None; ToIdle(); }
                else if (_last is not null) JumpOffWall(_last);
                break;
            case PetActivity.WebHang:
                if (Trick == TrickPhase.Gone && _last is not null)
                    FallFromHere(new Vec(R(_last.WorkArea.Left + size / 2, _last.WorkArea.Right - size / 2), _last.WorkArea.Top));
                else if (Surface == Surface.Thread)
                    FallFromHere(new Vec(Position.X, Position.Y + size));
                else
                    FallFromHere(Position);
                break;
            case PetActivity.CursorSteal:
                CursorTarget = null;
                Trick = TrickPhase.None;
                ToIdle();
                break;
        }
    }

    /// <summary>Drops from <paramref name="bottom"/> (bottom-centre) under gravity, leaving any trick.</summary>
    void FallFromHere(Vec bottom)
    {
        LandOnGround(bottom);
        _vy = 0;
        Set(PetActivity.Fall, double.MaxValue);
    }

    void LandOnGround(Vec bottom)
    {
        Surface = Surface.Ground;
        Boost = 1;
        ThreadAnchor = null;
        GlassStart = null;
        CursorTarget = null;
        Trick = TrickPhase.None;
        Position = bottom;
    }

    void CleanupTrick()
    {
        if (Activity == PetActivity.ChangeOutfit) WearPending();
        var bottom = Surface switch
        {
            Surface.Glass => new Vec(Position.X, Position.Y + (_last?.SpriteSize ?? 128) * Boost / 2),
            Surface.LeftWall or Surface.RightWall => new Vec(Position.X, Position.Y + (_last?.SpriteSize ?? 128) / 2),
            Surface.Thread => new Vec(Position.X, Position.Y + (_last?.SpriteSize ?? 128)),
            _ => Position,
        };
        LandOnGround(bottom);
    }

    // ---------------------------------------------------------------- outfits

    void MaybeChangeOutfit(PetContext ctx)
    {
        if (_outfits is null && ctx.OutfitMode == OutfitMode.None) return;
        _outfits ??= new OutfitPicker(_rng, ctx.Now);
        if (_outfits.Desired(ctx.Now, ctx.Zone, ctx.OutfitMode) is { } o)
        {
            _pendingOutfit = o;
            Set(PetActivity.ChangeOutfit, OutfitChangeSeconds);
        }
    }

    void WearPending()
    {
        if (_pendingOutfit is { } o) _outfits?.Wear(o);
        _pendingOutfit = null;
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>Starts an activity directly (tests and debug).</summary>
    internal void Start(PetActivity activity, double duration)
    {
        Phase = HuntPhase.Crouch;
        _caught = false;
        ToyPosition = null;
        if (activity == PetActivity.MouseChase || activity == PetActivity.YarnPlay || activity == PetActivity.ButterflyHunt)
            _toyDirection = Facing;
        Set(activity, duration);
    }

    void Set(PetActivity activity, double duration)
    {
        var changed = activity != Activity;
        Activity = activity;
        _duration = duration;
        ActivityTime = 0;
        _phaseTime = 0;
        if (!IsTrick(activity)) Trick = TrickPhase.None;
        if (activity is not (PetActivity.YarnPlay or PetActivity.ButterflyHunt or PetActivity.MouseChase)) ToyPosition = null;
        if (changed) ActivityChanged?.Invoke(activity);
    }

    void ToIdle() => Set(PetActivity.Idle, R(3, 8));

    void ToIdleOrReminder()
    {
        if (_reminderQueued)
        {
            _reminderQueued = false;
            Set(PetActivity.Remind, double.MaxValue);
        }
        else ToIdle();
    }

    void ChooseNext(PetContext ctx)
    {
        if (TryStartScheduledTrick(ctx)) return;

        if (ctx.Now >= _nextGameAt)
        {
            _nextGameAt = ctx.Now + NextGameInterval(ctx.Level);
            var pick = _rng.NextDouble();
            var game = pick < 1.0 / 3 ? PetActivity.YarnPlay : pick < 2.0 / 3 ? PetActivity.ButterflyHunt : PetActivity.MouseChase;
            Start(game, R(12, MaxGameSeconds));
            return;
        }

        var weights = ActivityWeights.For(ctx.Level, ActivityWeights.IsNight(ctx.Now, ctx.Zone));
        var next = ActivityWeights.Pick(weights, _rng.NextDouble());
        switch (next)
        {
            case PetActivity.Walk:
                if (_rng.NextDouble() < 0.5) Facing = -Facing;
                Set(next, R(3, 10));
                break;
            case PetActivity.Sit: Set(next, R(5, 15)); break;
            case PetActivity.Groom: Set(next, R(3, 5)); break;
            case PetActivity.Stretch: Set(next, 2); break;
            case PetActivity.Yawn: Set(next, 1.5); break;
            case PetActivity.Sleep: Set(next, R(30, 120)); break;
            case PetActivity.Fly: Set(next, R(3, 6)); break;
            default: ToIdle(); break;
        }
    }

    TimeSpan NextGameInterval(Storage.ActivityLevel level)
    {
        var (min, max) = ActivityWeights.GameIntervalMinutes(level);
        return TimeSpan.FromMinutes(R(min, max));
    }

    bool CanHuntCursor(PetContext ctx) =>
        ctx.CursorHuntEnabled
        && ctx.Cursor is { } c
        && ctx.CursorStillSeconds >= CursorStillNeeded
        && c.DistanceTo(Position) <= CursorReach
        && (_lastCursorHunt is null || ctx.Now - _lastCursorHunt.Value >= CursorCooldown);

    void StepX(double distance, double minX, double maxX)
    {
        var x = Position.X + Facing * distance;
        if (x >= maxX) { x = maxX; Facing = -1; }
        else if (x <= minX) { x = minX; Facing = 1; }
        Position = Position with { X = x };
    }

    void UpdateFly(double ground, double dt, double minX, double maxX)
    {
        var rise = FlyHeight / FlySpeed;
        var hover = _duration;
        var t = ActivityTime;
        double height;
        if (t < rise) height = t * FlySpeed;
        else if (t < rise + hover) height = FlyHeight;
        else height = FlyHeight - (t - rise - hover) * FlySpeed;

        StepX(30 * dt, minX, maxX);
        if (height <= 0 && t > rise)
        {
            Position = Position with { Y = ground };
            ToIdleOrReminder();
            return;
        }
        Position = Position with { Y = ground - Math.Max(0, height) };
    }

    void UpdateYarn(PetContext ctx, double dt, double minX, double maxX)
    {
        var ground = ctx.WorkArea.Bottom;
        var toy = ToyPosition ?? new Vec(Position.X + Facing * ctx.SpriteSize * 0.55, ground);
        var tx = toy.X + _toyDirection * 35 * dt;
        var edgeMin = ctx.WorkArea.Left + 12;
        var edgeMax = ctx.WorkArea.Right - 12;
        if (tx >= edgeMax || tx <= edgeMin) { _toyDirection = -_toyDirection; tx = Math.Clamp(tx, edgeMin, edgeMax); }
        ToyPosition = new Vec(tx, ground);
        Facing = tx >= Position.X ? 1 : -1;
        if (Math.Abs(tx - Position.X) > ctx.SpriteSize * 0.6) StepX(45 * dt, minX, maxX);
    }

    void UpdateButterfly(PetContext ctx, double dt)
    {
        var ground = ctx.WorkArea.Bottom;
        var t = ActivityTime;
        var pounceAt = Math.Max(0, _duration - 2.5);
        if (Phase == HuntPhase.Crouch && t >= pounceAt)
        {
            Phase = HuntPhase.Pounce;
            _phaseTime = 0;
        }
        else if (Phase == HuntPhase.Pounce && _phaseTime >= 0.6)
        {
            _caught = _rng.NextDouble() < 0.4;
            Phase = _caught ? HuntPhase.Caught : HuntPhase.Puzzled;
            _phaseTime = 0;
        }

        if (Phase is HuntPhase.Crouch or HuntPhase.Pounce)
        {
            ToyPosition = new Vec(
                Position.X + Facing * ctx.SpriteSize * 0.7 + 18 * Math.Sin(1.3 * t),
                ground - ctx.SpriteSize * 0.95 + 10 * Math.Sin(3.1 * t));
        }
        else if (Phase == HuntPhase.Caught)
        {
            ToyPosition = null;
        }
        else if (ToyPosition is { } p)
        {
            ToyPosition = new Vec(p.X + Facing * 40 * dt, p.Y - 120 * dt); // flies away
        }
    }

    void UpdateMouseChase(PetContext ctx, double dt, double minX, double maxX)
    {
        var ground = ctx.WorkArea.Bottom;
        if (Phase == HuntPhase.Puzzled)
        {
            if (_phaseTime >= 2) ToIdleOrReminder();
            return;
        }
        var toy = ToyPosition ?? new Vec(Position.X + _toyDirection * ctx.SpriteSize * 1.2, ground);
        var tx = toy.X + _toyDirection * ToyMouseSpeed * dt;
        if (tx > ctx.WorkArea.Right + 20 || tx < ctx.WorkArea.Left - 20)
        {
            ToyPosition = null;
            Phase = HuntPhase.Puzzled;
            _phaseTime = 0;
            return;
        }
        ToyPosition = new Vec(tx, ground);
        Facing = _toyDirection;
        var x = Math.Clamp(Position.X + Facing * ChaseSpeed * dt, minX, maxX);
        Position = Position with { X = x };
    }

    void UpdateCursorHunt(PetContext ctx, double dt, double minX, double maxX)
    {
        if (ctx.CursorStillSeconds < 0.5 && Phase == HuntPhase.Crouch || ctx.Cursor is null)
        {
            ToIdleOrReminder();
            return;
        }
        var target = ctx.Cursor.Value.X;
        switch (Phase)
        {
            case HuntPhase.Crouch:
                Facing = target >= Position.X ? 1 : -1;
                if (Math.Abs(target - Position.X) > ctx.SpriteSize * 0.35)
                    Position = Position with { X = Math.Clamp(Position.X + Facing * 20 * dt, minX, maxX) };
                if (_phaseTime >= 3) { Phase = HuntPhase.Pounce; _phaseTime = 0; }
                break;
            case HuntPhase.Pounce:
                Position = Position with { X = Math.Clamp(Position.X + Facing * 160 * dt, minX, maxX) };
                if (_phaseTime >= 0.6) { Phase = HuntPhase.Puzzled; _phaseTime = 0; }
                break;
            case HuntPhase.Puzzled:
                if (_phaseTime >= 1.5) ToIdleOrReminder();
                break;
        }
    }

    double R(double min, double max) => min + (max - min) * _rng.NextDouble();
}
