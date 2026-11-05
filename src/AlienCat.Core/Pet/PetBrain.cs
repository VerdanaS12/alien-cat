namespace AlienCat.Core.Pet;

/// <summary>Decides what the cat does and where it is. Position is the bottom-centre of the sprite in screen units.</summary>
public sealed class PetBrain
{
    public const double WalkSpeed = 60;
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

    public PetBrain(IRandom rng, DateTimeOffset start, Vec position)
    {
        _rng = rng;
        Position = position;
        _nextGameAt = start + NextGameInterval(Storage.ActivityLevel.Normal);
        _duration = R(3, 8);
    }

    public PetActivity Activity { get; private set; } = PetActivity.Idle;
    public HuntPhase Phase { get; private set; } = HuntPhase.Crouch;
    public Vec Position { get; set; }
    public int Facing { get; set; } = 1;
    public bool Sad { get; private set; }
    public bool Nagging { get; set; }
    public double ActivityTime { get; private set; }
    public Vec? ToyPosition { get; private set; }

    public event Action<PetActivity>? ActivityChanged;

    // ------------------------------------------------------------------ input

    public void BeginDrag(Vec p)
    {
        if (Activity == PetActivity.Hidden) return;
        if (Activity == PetActivity.Remind) _reminderQueued = true;
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
        if (Activity is PetActivity.Hidden or PetActivity.Dragged or PetActivity.Remind or PetActivity.Fall) return;
        Set(PetActivity.Purr, 2);
    }

    public void StartReminder()
    {
        if (Activity is PetActivity.Dragged or PetActivity.Fall or PetActivity.Fly or PetActivity.Hidden)
        {
            _reminderQueued = true;
            return;
        }
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
            if (Activity is not (PetActivity.Hidden or PetActivity.Dragged))
            {
                if (Activity == PetActivity.Remind) _reminderQueued = true;
                Position = Position with { Y = ground };
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
        }

        if (Activity is not (PetActivity.Dragged or PetActivity.Fall or PetActivity.Fly))
            Position = new Vec(Math.Clamp(Position.X, minX, Math.Max(minX, maxX)), ground);

        if (ActivityTime >= _duration && Activity is not (PetActivity.Fly or PetActivity.Remind or PetActivity.Dragged or PetActivity.Fall))
        {
            if (Activity == PetActivity.Idle) ChooseNext(ctx);
            else ToIdleOrReminder();
        }

        if (Activity is PetActivity.Idle or PetActivity.Sit or PetActivity.Walk or PetActivity.Groom && CanHuntCursor(ctx))
        {
            _lastCursorHunt = ctx.Now;
            Phase = HuntPhase.Crouch;
            Facing = ctx.Cursor!.Value.X >= Position.X ? 1 : -1;
            Set(PetActivity.CursorHunt, MaxCursorHuntSeconds);
        }

        Sad = ctx.HasOverdue && Activity is PetActivity.Idle or PetActivity.Sit;
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
