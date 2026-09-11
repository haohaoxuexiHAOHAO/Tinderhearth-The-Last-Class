using Godot;
using Tinderhearth.Platform;
using Tinderhearth.Rules.Combat;
using Tinderhearth.Rules.Ui;
using Tinderhearth.UI;

namespace Tinderhearth.World;

/// <summary>仅主角的开发场景，不含 GP-13 木桩或 GP-14 调优工具。</summary>
public partial class PlayerDev : Node2D
{
    private PlayerActor _player = null!;
    private int _frame;
    private bool _probe;
    private int _passed;
    private int _checked;
    private float _startX;
    private InputRouter _router = null!;
    private PlayerActor? _subject;
    private int _stage;
    private int _tick;
    private Vector2 _origin;
    private bool _hitCeiling;
    private bool _fell;
    private bool _landed;
    private bool _dodgeValid = true;
    private bool _chainStarted;
    private readonly HashSet<string> _seen = new();
    private bool _previousFloor;
    private double _previousVy;
    // 精灵与规则相位是否始终一致：逐帧累积，任一帧对不上就永久为假。
    private bool _spritePhaseValid = true;
    private int _dodgeSpriteFrame = -1;
    private bool _dodgeSpriteValid = true;
    private int _spritePhaseFrames;
    private int _drawn = -1;
    private int _tickSlips;
    private int _focusLost;
    // `GP-15` 纵深阶段的累积量。逐帧累积、末尾一次报，理由同 sprite-phase：纵深接线的失效方式是
    // 慢慢跑偏（少走一帧、某一帧串到 X 上），抽查一帧看不出来。
    private Vector2 _depthOrigin;
    private double _depthPrev;
    private int _depthDir;
    private bool _depthWalkValid = true;
    private int _depthAdvanceFrames;
    private bool _depthAirValid = true;
    private bool _depthWalkVisual = true;
    private int _depthAirFrames;
    private double _depthAtTakeoff = double.NaN;
    private int _depthLandTick = -1;

    /// <summary>
    /// 窗口失焦次数。Godot 在失焦时**释放全部按下的动作**，于是探针合成的「按住右移」会被
    /// 悄悄松开 —— 表现是 `move`／`dash` 这类需要持续输入的判据失败，而失败信息指向运动学，
    /// 与真实原因（有别的窗口抢了焦点）毫无关系。所以把它记成一条独立判据：这一轮到底能不能算。
    /// </summary>
    public override void _Notification(int what)
    {
        if (what == NotificationApplicationFocusOut) _focusLost++;
    }

    public override void _Ready()
    {
        _probe = OS.GetCmdlineUserArgs().Contains("--gp12-probe");
        var router = new InputRouter();
        _router = router;
        AddChild(router);
        _player = new PlayerActor { Position = new Vector2(160, 130) };
        _player.Controllers.Assign(_player.ActorId, new LocalPlayerController(_player.ActorId) { Router = router });
        AddChild(_player);
        AddChild(new GameCamera(CameraView.SideView) { FollowTarget = _player, Router = router });
        Platform(new Vector2(160, 150), new Vector2(2000, 20));
        Platform(new Vector2(280, 113), new Vector2(70, 10));
        ProcessPhysicsPriority = 10;
        // 探针的断言按**物理帧**计数，注入却走 CallDeferred，那是主循环迭代里刷的队列。默认
        // 一次迭代最多补跑 8 个物理帧，于是「第 65 帧按下、第 66 帧松开」可能落在同一次刷里 ——
        // 按下与松开一起生效，闪避压根没起来，而失败报出来的是「闪避没无敌」这种**指向错方向**
        // 的结论。实测三次连跑各失败一条不同的判据（move／jump+air-attack／dodge-exclusive），
        // 同一份代码不同结果正是两个时钟不同步的签名。
        //
        // 钉成 1 之后一次迭代只跑一个物理帧：帧慢就慢，但每两个物理帧之间必有一次队列刷新。
        // 这不改单帧物理行为（步长仍是定值），只放弃「掉帧后追赶」——探针不需要追赶。
        // 前提是否真的成立不靠相信：逐帧核渲染帧号有没有前进，末尾报 tick-frame-1to1。
        if (_probe) Engine.MaxPhysicsStepsPerFrame = 1;
    }

    private void Platform(Vector2 position, Vector2 size)
    {
        var body = new StaticBody2D { Position = position };
        body.AddChild(new CollisionShape2D { Shape = new RectangleShape2D { Size = size } });
        body.AddChild(new Polygon2D { Polygon = [new(-size.X / 2, -size.Y / 2), new(size.X / 2, -size.Y / 2), new(size.X / 2, size.Y / 2), new(-size.X / 2, size.Y / 2)], Color = new Color("485750") });
        AddChild(body);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!_probe) return;
        _frame++;
        // 「每个物理帧之间都有一次渲染」是本探针的前提而不是巧合，见 _Ready 里那一行。
        // 数的是渲染帧号没有前进的物理帧，它同时盖住两种异常：一次迭代补跑了多个物理帧，
        // 以及窗口被遮住／最小化导致渲染整段停摆（实测失焦那一轮滑了 121 帧）。
        var drawn = (int)Engine.GetFramesDrawn();
        if (_drawn >= 0 && drawn == _drawn) _tickSlips++;
        _drawn = drawn;
        // 跟的是**当前在动的那个角色**：主阶段是 _player，扩展阶段换成 _subject。
        TrackSpritePhase(_subject ?? _player);
        if (_stage > 0) { ExtendedProbe(); return; }
        // 在角色推进后断言；注入延迟到回调外，让下一物理帧读取真实边沿。
        switch (_frame)
        {
            case 10: Check("floor", _player.IsOnFloor()); _startX = _player.Position.X; Press(InputActions.MoveRight, true); break;
            case 20: Check("move", _player.Position.X > _startX + 10); Press(InputActions.Sprint, true); break;
            case 25: Check("dash", _player.Combat.Motor.Phase == MotorPhase.Dash); Press(InputActions.Sprint, false); Press(InputActions.MoveRight, false); Press(InputActions.Jump, true); break;
            case 26: Check("jump", _player.Velocity.Y < 0); Press(InputActions.Jump, false); Press(InputActions.AttackLight, true); break;
            case 27: Check("air-attack", _player.Combat.Combo.IsAttacking); Press(InputActions.AttackLight, false); break;
            case 65: Check("land", _player.IsOnFloor() && !_player.Combat.Combo.IsAttacking); Press(InputActions.Dodge, true); break;
            case 66: Press(InputActions.Dodge, false); Press(InputActions.AttackHeavy, true); break;
            case 68: Check("dodge-exclusive", !_player.Combat.Combo.IsAttacking && _player.Combat.Motor.IsInvulnerable); Press(InputActions.AttackHeavy, false); break;
            case 90: Press(InputActions.AttackHeavy, true); break;
            // 重击现在有真图（`ART-6` 接仓），所以判据从「退回几何占位」翻成「精灵可见且是 heavy 表」。
            case 91: Check("heavy", _player.Combat.Combo.Kind == ComboKind.Heavy && _player.VisualAction == "heavy" && _player.Sprite.Visible && _player.Sprite.Animation == "heavy"); Press(InputActions.AttackHeavy, false); break;
            case 125: Press(InputActions.AttackLight, true); break;
            case 126: Check("light-sprite", _player.Sprite.Visible && _player.Sprite.Animation == "light"); Press(InputActions.AttackLight, false); break;
            // 前摇只准出第 0 帧、Active 出第 1–2 帧：命中姿不许在判定框开之前先亮出来。
            case 129: Check("startup-frame", _player.Combat.Combo.Phase == AttackPhase.Startup && _player.Sprite.Frame == 0); break;
            case 130: Check("active-frame", _player.Combat.Combo.IsHitActive && _player.Sprite.Frame >= 1 && _player.Sprite.Frame <= 2); break;
            case 140:
                _player.Controllers.Assign(_player.ActorId, new IdleController());
                Press(InputActions.MoveRight, true);
                break;
            case 142: Check("controller-replaced", _player.Combat.Motor.HorizontalVelocity == 0); Press(InputActions.MoveRight, false); break;
            case 145:
                Platform(new Vector2(-200, 75), new Vector2(100, 10));
                SpawnSubject(new Vector2(-200, 140));
                _stage = 1;
                break;
        }
    }

    private void SpawnSubject(Vector2 position)
    {
        if (_subject != null) { _subject.SetPhysicsProcess(false); _subject.QueueFree(); }
        _subject = new PlayerActor { Position = position };
        _subject.Controllers.Assign(_subject.ActorId, new LocalPlayerController(_subject.ActorId) { Router = _router });
        AddChild(_subject);
        _tick = 0;
    }

    private void ExtendedProbe()
    {
        var actor = _subject!;
        _tick++;
        if (_tick > 240) { Check("stage-timeout", false); Capture(); return; }
        if (_stage == 1)
        {
            if (_tick == 5) Press(InputActions.Jump, true);
            if (_tick == 6) Press(InputActions.Jump, false);
            if (_hitCeiling)
            {
                Check("ceiling-next-frame", actor.Position.Y > _origin.Y && Math.Abs(actor.Combat.Motor.VerticalVelocity - 980.0 / 60) < 0.001 && Math.Abs(actor.Velocity.Y - 980.0 / 60) < 0.001);
                SpawnSubject(new Vector2(-50, 140)); _stage = 2;
            }
            else if (actor.IsOnCeiling())
            {
                Check("ceiling-hit", actor.Combat.Motor.VerticalVelocity == 0 && actor.Velocity.Y == 0);
                _hitCeiling = true; _origin = actor.Position;
            }
        }
        else if (_stage == 2)
        {
            if (_tick == 5) { _origin = actor.Position; Press(InputActions.Dodge, true); }
            if (_tick >= 6 && _tick <= 23)
                _dodgeValid &= Math.Abs(actor.Combat.Motor.HorizontalVelocity - 168) < 0.001 && Math.Abs(actor.Position.Y - _origin.Y) < 0.001;
            if (_tick == 6) Press(InputActions.Dodge, false);
            if (_tick == 23)
            {
                GD.Print($"[GP12] dodge displacement={actor.Position.X - _origin.X}");
                Check("dodge-18-ticks", _dodgeValid && Math.Abs(actor.Position.X - _origin.X - 50.4) < 0.002);
                Platform(new Vector2(0, 105), new Vector2(20, 10));
                SpawnSubject(new Vector2(0, 100)); _stage = 3;
            }
        }
        else if (_stage == 3)
        {
            if (_tick == 5) { _origin = actor.Position; Press(InputActions.Dodge, true); }
            if (_tick == 6) Press(InputActions.Dodge, false);
            if (_tick > 6 && !_previousFloor && !actor.IsOnFloor())
            {
                _fell = true;
                _dodgeValid &= actor.Position.Y > _origin.Y && Math.Abs(actor.Combat.Motor.VerticalVelocity - _previousVy - 980.0 / 60) < 0.001;
            }
            if (_fell && actor.IsOnFloor())
            {
                _landed = true;
                Check("dodge-ledge-collision", actor.Combat.Motor.VerticalVelocity == 0 && actor.Velocity.Y == 0);
                Check("dodge-ledge-fall", _dodgeValid && actor.Position.Y > 100);
                SpawnSubject(new Vector2(100, 140)); _stage = 4; _chainStarted = false;
            }
            _previousFloor = actor.IsOnFloor(); _previousVy = actor.Combat.Motor.VerticalVelocity;
            if (_tick == 23 && !_fell) { Check("dodge-ledge-fall", false); Capture(); }
        }
        else if (_stage <= 5)
        {
            var heavy = _stage == 4;
            var action = heavy ? InputActions.AttackHeavy : InputActions.AttackLight;
            var combo = actor.Combat.Combo;
            if (_tick == 5) Press(action, true);
            else Press(action, false);
            if (combo.IsAttacking)
            {
                _chainStarted = true;
                // 轻击续段要命中确认(`GP-10` 方案 b)。本场景无木桩,探针在命中相注入一次命中,
                // 好让轻击连段链走得下去;重击忽略这个标志。
                if (combo.IsHitActive) combo.RegisterHit();
                var name = $"{(heavy ? "heavy" : "light")}-{combo.Step + 1}-{combo.Phase.ToString().ToLowerInvariant()}";
                if (_seen.Add(name)) Check(name, combo.Kind == (heavy ? ComboKind.Heavy : ComboKind.Light));
                if (combo.IsComboWindowOpen && combo.Step < (heavy ? 1 : 2)) Press(action, true);
            }
            else if (_chainStarted)
            {
                if (heavy) { _stage = 5; _tick = 0; _chainStarted = false; }
                else
                {
                    Check("ledge-landed", _landed);
                    SpawnSubject(new Vector2(400, 140));
                    _stage = 6;
                }
            }
        }
        else
        {
            DepthProbe(actor);
        }
    }

    /// <summary>走满整条纵深带要几帧，加余量。</summary>
    /// <remarks>
    /// **不写死帧数**（踩坑记录 48 同一条理由）：纵深速度是 `GP-6` 的未校准初值、带宽可能被
    /// `ENG-16` 回改，写死的阶段长度只对今天这一版成立，改数之后判据会以「还没走到带沿」的形状
    /// 失败，而那与它要测的东西无关。
    /// </remarks>
    private static int DepthLegFrames => (int)Math.Ceiling(
        DepthBand.WidthWorldPx / (CombatFeel.DepthSpeedPixelsPerSecond * CombatFeel.FrameSeconds)) + 8;

    /// <summary>
    /// `GP-15` 纵深轴的**接线**判据：三轴分离本身由规则层单测钉死，这里只证明引擎那一段真的接上了。
    /// </summary>
    /// <remarks>
    /// 为什么值得占几十个物理帧：接线断掉的表现是「按 W／S 没反应」，而规则层测试照旧全绿 —— 它
    /// 测的是喂进去的 <see cref="CombatInput"/>，测不到喂进来的那条路（<c>InputRouter</c> 的移动
    /// 向量 → <c>DepthSign</c>）。**符号写反更坏**：它不报错，只让「往里走」变成「往外走」，要等
    /// `ENG-15` 把绘制排序接上才看得出来，而那时前后关系已经与命中判定相反了（正典点名这条）。
    ///
    /// 四条判据分两组。地面两条钉「按住向前／向后键，纵深逐帧走登记的步长、走到带沿被钳住、
    /// 钳后不留假速度」，同时逐帧核**引擎持有的 X 与 Y 一像素都没动** —— 那是三轴不串在引擎侧的
    /// 形状。空中两条钉「离地期间纵深一帧都不动，落地后同一份按住的输入立刻又生效」，这条最容易
    /// 在 `ENG-15` 接绘制时被改坏。
    ///
    /// 起手那一两帧不钉步长：输入经 deferred 队列生效，那几帧纵深还没开始动。**不是放松判据** ——
    /// 「按了键却一帧都没动」由推进帧数下限（由带宽与步长算出）判死，中途卡住也照旧判失败。
    /// </remarks>
    private void DepthProbe(PlayerActor actor)
    {
        var motor = actor.Combat.Motor;
        var step = CombatFeel.DepthSpeedPixelsPerSecond * CombatFeel.FrameSeconds;
        var depth = motor.DepthWorldPx;
        var frontEnd = 1 + DepthLegFrames;
        var backEnd = frontEnd + DepthLegFrames;
        if (_tick == 1)
        {
            _depthOrigin = actor.Position;
            _depthPrev = depth;
            _depthDir = 1;
            Press(InputActions.MoveDown, true);
            return;
        }

        // 横向一像素都不许动：整段一次横向输入都没给过。
        _depthWalkValid &= Math.Abs(actor.Position.X - _depthOrigin.X) < 0.001 && DepthBand.Contains(depth);

        if (_tick <= backEnd)
        {
            var delta = depth - _depthPrev;
            var edge = _depthDir > 0 ? DepthBand.FrontWorldPx : DepthBand.BackWorldPx;
            _depthPrev = depth;
            if (Math.Abs(delta) > 1e-9) _depthAdvanceFrames++;
            _depthWalkValid &= !motor.IsDepthAirLocked
                && Math.Abs(actor.Position.Y - _depthOrigin.Y) < 0.001
                && (_depthAdvanceFrames == 0
                    || Math.Abs(delta - _depthDir * step) < 1e-9
                    || (depth == edge && Math.Abs(delta) <= step + 1e-9));
            // 纵深在动就得是「在走」而不是待机。**只按 W／S 时横向速度是 0**，原来的动作选择
            // 只看横向，于是角色站着不动地在纵深上滑 —— 位置在变、判据全绿、只有眼睛看得出来
            // （作者 2026-09-09 实机报的）。所以逐帧钉住它，别再靠眼睛。
            if (Math.Abs(motor.DepthVelocity) > 0) _depthWalkVisual &= actor.VisualAction == "walk";

            if (_tick == frontEnd)
            {
                // 从带中线走到前沿：半条带的距离，所以推进帧数就是它除以步长，与输入延迟无关。
                Check("depth-front", _depthWalkValid && depth == DepthBand.FrontWorldPx
                    && motor.DepthVelocity == 0
                    && _depthAdvanceFrames >= Math.Ceiling(DepthBand.CenterWorldPx / step));
                _depthWalkValid = true;
                _depthAdvanceFrames = 0;
                _depthDir = -1;
                Press(InputActions.MoveDown, false);
                Press(InputActions.MoveUp, true);
            }
            else if (_tick == backEnd)
            {
                Check("depth-back", _depthWalkValid && depth == DepthBand.BackWorldPx
                    && motor.DepthVelocity == 0
                    && _depthAdvanceFrames >= Math.Ceiling(DepthBand.WidthWorldPx / step));
                // 起跳前把方向换成向前：带沿这一侧还有整条带的余量，锁没锁住一眼看得出来。
                Press(InputActions.MoveUp, false);
                Press(InputActions.MoveDown, true);
                Press(InputActions.Jump, true);
            }
            return;
        }

        if (_tick == backEnd + 2) Press(InputActions.Jump, false);
        if (!actor.IsOnFloor())
        {
            if (double.IsNaN(_depthAtTakeoff)) _depthAtTakeoff = depth;
            _depthAirValid &= motor.IsDepthAirLocked && depth == _depthAtTakeoff;
            _depthAirFrames++;
            return;
        }
        if (_depthAirFrames == 0) return;
        if (_depthLandTick < 0)
        {
            _depthLandTick = _tick;
            // 滞空帧数下限取「上升段」的理论帧数（初速 ÷ 重力），同样由手感常量导出而不是写死：
            // 它只用来排除「压根没起跳也算通过」。起跳时纵深必须还没贴到前沿，否则按住向前本来
            // 就动不了，这条判据会变成空判。
            var risingFrames = CombatFeel.JumpInitialPixelsPerSecond
                / (double)CombatFeel.GravityPixelsPerSecondSquared * CombatFeel.PhysicsTicksPerSecond;
            Check("depth-air-lock", _depthAirValid && _depthAirFrames >= risingFrames
                && _depthAtTakeoff < DepthBand.FrontWorldPx);
            return;
        }
        if (_tick == _depthLandTick + 2)
        {
            // 落地后**同一份**按住的输入立刻又生效：落地帧本身不推进（那一帧的 Tick 读到的还是
            // 落地前的 isOnFloor），所以两帧里恰好走两格。跳跃轴要回到同一块地面并静止 ——
            // 容差取引擎自报的碰撞安全边距（<c>SafeMargin</c>，默认 0.08），因为落地静止位置与
            // 起跳前本来就差一个边距（实测 0.025px）。挑一个刚好能过的数是量具骗自己。
            Check("depth-land-unlock", _depthWalkValid && !motor.IsDepthAirLocked
                && depth >= _depthAtTakeoff + 2 * step - 1e-9
                && motor.VerticalVelocity == 0
                && Math.Abs(actor.Position.Y - _depthOrigin.Y) <= actor.SafeMargin);
            // 走过整条带来回两趟，每一个纵深在动的帧都得是行走姿态；顺带核它真的量过（不是空判）。
            Check("depth-walk-visual", _depthWalkVisual
                && _depthAdvanceFrames >= Math.Ceiling(DepthBand.WidthWorldPx / step));
            Capture();
        }
    }

    /// <summary>
    /// 逐帧核「精灵帧」与「规则相位」是否一致（`ART-6`）。
    /// </summary>
    /// <remarks>
    /// 为什么必须逐帧核而不是抽查一帧：帧映射的失效方式是**慢慢跑偏** —— 渲染时钟自己跑一段
    /// 之后，命中姿会落到前摇里，玩家学到的时机就是错的。抽查某一帧看不出这件事，
    /// 只有「这段动作的每一帧都对」才排除得掉。任一帧对不上就永久记假，末尾一次报出来。
    /// </remarks>
    private void TrackSpritePhase(PlayerActor actor)
    {
        if (!actor.Sprite.Visible) return;
        var combo = actor.Combat.Combo;
        var frame = actor.Sprite.Frame;
        if (combo.IsAttacking)
        {
            var name = combo.Kind == ComboKind.Heavy ? "heavy" : "light";
            var count = actor.Sprite.SpriteFrames.GetFrameCount(name);
            var first = PlayerActor.AttackActiveFirstFrame;
            var last = first + PlayerActor.AttackActiveSpan - 1;
            _spritePhaseValid &= actor.Sprite.Animation == name && combo.Phase switch
            {
                AttackPhase.Startup => frame == 0,
                AttackPhase.Active => frame >= first && frame <= last,
                _ => frame > last && frame < count,
            };
            _spritePhaseFrames++;
            _dodgeSpriteFrame = -1;
        }
        else if (actor.Combat.Motor.Phase == MotorPhase.Dodge)
        {
            var count = actor.Sprite.SpriteFrames.GetFrameCount("dodge");
            // 闪避帧由规则相位等分映射，所以只准单调不回头、不越界。
            _dodgeSpriteValid &= actor.Sprite.Animation == "dodge"
                && frame >= _dodgeSpriteFrame && frame >= 0 && frame < count;
            _dodgeSpriteFrame = frame;
            _spritePhaseFrames++;
        }
        else
        {
            _dodgeSpriteFrame = -1;
        }
    }

    /// <summary>
    /// 逐像素核进仓表的脚底行与本体高度（`ART-6`）：证明「导入器量出来的」一路到「引擎渲染的」
    /// 都没走形，而不是只相信登记表。
    /// </summary>
    /// <remarks>
    /// 两条判据各对着一个不报错的缺陷：脚底行不一致 → 切动作时角色上下跳；本体超过 32px →
    /// 违反[玩法定位 · 像素基准]的「本体仍守 2 单位」，而画面上只是「这个动作看起来大了点」。
    /// 取像素走 <c>AtlasTexture.Atlas</c> 与它的 <c>Region</c>，不在探针里重写一遍资源路径。
    ///
    /// **它在 <see cref="Capture"/> 里跑，物理已经停掉，一个物理帧都不占。** 这不是洁癖：
    /// 第一版放在第 5 个物理帧，逐像素 <c>GetPixel</c> 让那一帧长到引擎开始追赶物理步，
    /// deferred 注入的输入因此比平时晚了几个 Tick，第 20 帧的位移判据（阈值 10px、正常值
    /// 11.27px）当场失败 —— **量具把被测系统搞坏了**。改到 <c>_Ready</c> 仍然失败，而且多带
    /// 坏一条闪避无敌判据。取像素也顺手改成原生 <c>GetRegion + GetUsedRect</c>，但真正的修法
    /// 是位置：**静态判据不许插进计时序列**，快多少都不该插。
    /// </remarks>
    private void CheckSheetGeometry()
    {
        var sheets = _player.Sprite.SpriteFrames;
        var names = sheets.GetAnimationNames();
        var footRow = PlayerActor.GroundRow - 1;
        var feet = new List<string>();
        var empty = new List<string>();
        var tallest = 0;
        var scannedFrames = 0;
        var actions = 0;
        var footOk = true;
        var belowGround = 0;
        foreach (var action in names)
        {
            var count = sheets.GetFrameCount(action);
            if (count == 0)
            {
                // SpriteFrames 出厂自带一个空的 "default"，它不是缺表；真缺表走 MissingSheets。
                empty.Add(action);
                continue;
            }
            var lowest = -1;
            var highest = PlayerActor.FrameHeight;
            Image? atlasImage = null;
            for (var i = 0; i < count; i++)
            {
                if (sheets.GetFrameTexture(action, i) is not AtlasTexture atlas)
                {
                    Check("foot-row", false);
                    return;
                }
                atlasImage ??= atlas.Atlas.GetImage();
                var used = atlasImage.GetRegion((Rect2I)atlas.Region).GetUsedRect();
                if (used.Size.Y == 0)
                {
                    Check("foot-row", false);
                    return;
                }
                lowest = Math.Max(lowest, used.Position.Y + used.Size.Y - 1);
                highest = Math.Min(highest, used.Position.Y);
                if (used.Position.Y + used.Size.Y - 1 > footRow) belowGround++;
                scannedFrames++;
            }
            feet.Add($"{action}={lowest}/{PlayerActor.GroundRow - highest}");
            footOk &= lowest == footRow;
            tallest = Math.Max(tallest, PlayerActor.GroundRow - highest);
            actions++;
        }
        GD.Print($"[GP12] Sheet geometry actions={actions} frames={scannedFrames} footRow={footRow} "
            + $"perAction(lowest/height) {string.Join(" ", feet)} tallest={tallest} "
            + $"empty=[{string.Join(",", empty)}]");
        Check("frame-count", _player.MissingSheets.Count == 0 && actions > 0
            && scannedFrames >= actions && empty.Count <= 1);
        Check("foot-row", footOk && belowGround == 0 && scannedFrames > 0);
        // 本体 ≤32px 是正典条款，动作帧超出精灵格的例外只放宽画布不放宽本体。
        Check("body-height", tallest > 0 && tallest <= 32);
    }

    private static void Press(string action, bool pressed) => Callable.From(() => Input.ParseInputEvent(new InputEventAction { Action = action, Pressed = pressed })).CallDeferred();
    private void Check(string name, bool value)
    {
        _checked++;
        if (value) _passed++;
        GD.Print($"[GP12] {(value ? "PASS" : "FAIL")} {name}");
    }

    private sealed class IdleController : Tinderhearth.Rules.Foundation.Actors.IActorController
    {
        public Tinderhearth.Rules.Foundation.Actors.ActorControllerKind Kind => Tinderhearth.Rules.Foundation.Actors.ActorControllerKind.Ai;
        public Tinderhearth.Rules.Foundation.Actors.ActorIntent Decide(in Tinderhearth.Rules.Foundation.Actors.ActorView view) => new("idle");
    }

    private async void Capture()
    {
        SetPhysicsProcess(false);
        // 物理停了才跑精灵表几何（理由见 CheckSheetGeometry 的注释）。
        CheckSheetGeometry();
        // 累积判据在这里一次报出。同时要求**真的跟过帧** —— 跟了 0 帧也会「全对」。
        GD.Print($"[GP12] Sprite phase frames tracked={_spritePhaseFrames} "
            + $"physicsFrames={_frame} tickSlips={_tickSlips} focusLost={_focusLost}");
        Check("sprite-phase", _spritePhaseValid && _spritePhaseFrames >= CombatFeel.DodgeDurationFrames);
        Check("dodge-sprite", _dodgeSpriteValid && _spritePhaseFrames > 0);
        // 两条**前提**判据。它们不测玩法，测的是「这一轮的测量条件成立吗」：
        // 滑帧会让注入晚到，失焦会让按住的键被引擎松开。前提不成立就直接说前提不成立，
        // 别让它去毁一条无关判据然后把人指向错的方向。
        Check("tick-frame-1to1", _tickSlips == 0 && _frame > 0);
        Check("focus-kept", _focusLost == 0);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var image = GetViewport().GetTexture().GetImage();
        var path = OS.GetEnvironment("GP12_SHOT");
        Check("screenshot", image.GetWidth() > 0 && image.SavePng(path) == Error.Ok);
        GD.Print($"[GP12] Summary {_passed}/{_checked}");
        GetTree().Quit(_passed == _checked ? 0 : 1);
    }
}
