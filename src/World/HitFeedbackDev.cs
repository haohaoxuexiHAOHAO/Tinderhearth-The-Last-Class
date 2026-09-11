using Godot;
using Tinderhearth.Platform;
using Tinderhearth.Rules.Combat;
using Tinderhearth.Rules.Ui;
using Tinderhearth.UI;

namespace Tinderhearth.World;

/// <summary>GP-13 独立命中体验与真实物理帧探针，不接完整训练房工具。</summary>
public partial class HitFeedbackDev : Node2D
{
    private PlayerActor _player = null!;
    private TrainingDummy _dummy = null!;
    private GameCamera _camera = null!;
    private Hitbox _hitbox = null!;
    private readonly Hitstop _stop = new();
    private bool _probe;
    private int _frame;
    private int _passed;
    private int _checked;
    private int _frozen;
    private bool _freezeValid = true;
    private Vector2 _playerAt;
    private Vector2 _dummyAt;
    private Vector2 _cameraAt;
    private Vector2 _offset;
    // 纵深由规则层持有（`GP-15`），不在 Position 里，所以「冻结期间什么都不动」要单独记它一份 ——
    // 否则第三个轴在顿帧里偷偷推进也照样全绿。本场景一次纵深输入都没给，所以它全程该是常量。
    private double _depthAt;
    private double _sequenceDepth;
    private int _spriteFrame;
    private int _phaseFrame;
    private int _stun;
    private bool _heavy;
    private float _hitX;
    private bool _capturing;

    /// <summary>木桩「在轻击框内」的放置距离：两个 18 宽实体刚好不互插的最小间距。</summary>
    private const int NearX = Hurtbox.WidthWorldPx;

    /// <summary>木桩「在框外」的放置距离。够远就行，只用来验证离开与重入。</summary>
    private const int FarX = NearX + 100;
    private bool _sawShake;
    private bool _shakeEnabled;
    private bool _flashSaved;
    private InputRouter _router = null!;
    private bool _inputHeld;
    private int _inputCase = -1;
    private int _inputTick;
    private int _inputFrozen;
    private bool _sequenceValid;
    private Vector2 _sequenceAt;
    private double _expectedSpeed;
    private int _drawn = -1;
    private int _tickSlips;
    private int _focusLost;
    private static readonly string[] PulseActions = [InputActions.Jump, InputActions.AttackLight, InputActions.AttackHeavy];
    private string InputCaseName => _inputCase < 3 ? new[] { "jump", "attack-light", "attack-heavy" }[_inputCase] : "sprint";

    public override void _Ready()
    {
        _probe = OS.GetCmdlineUserArgs().Contains("--gp13-probe");
        var router = new InputRouter();
        _router = router;
        AddChild(router);
        _player = new PlayerActor { ManualPhysics = true, Position = new Vector2(160, 140) };
        _player.Controllers.Assign(_player.ActorId, new LocalPlayerController(_player.ActorId) { Router = router });
        AddChild(_player);
        _player.AddChild(new Hurtbox { Actor = _player });
        _hitbox = new Hitbox();
        _player.AddChild(_hitbox);
        _dummy = new TrainingDummy { Position = new Vector2(184, 140) };
        AddChild(_dummy);
        _camera = new GameCamera(CameraView.SideView) { ManualAdvance = true, FollowTarget = _player, Router = router };
        _camera.Rig.ShakeEnabled = !OS.GetCmdlineUserArgs().Contains("--no-shake");
        _shakeEnabled = _camera.Rig.ShakeEnabled;
        AddChild(_camera);
        // 与 PlayerDev 同一条前提：断言按物理帧计数、注入走 deferred 队列，所以一次迭代只准
        // 跑一个物理帧，否则按下与松开会落在同一次刷里。理由与实测见 PlayerDev._Ready。
        if (_probe) Engine.MaxPhysicsStepsPerFrame = 1;
        var ground = new StaticBody2D { Position = new Vector2(160, 150) };
        ground.AddChild(new CollisionShape2D { Shape = new RectangleShape2D { Size = new Vector2(1600, 20) } });
        ground.AddChild(new Polygon2D { Polygon = [new(-800, -10), new(800, -10), new(800, 10), new(-800, 10)], Color = new Color("485750") });
        AddChild(ground);
    }

    /// <summary>
    /// 窗口失焦次数。Godot 失焦时释放全部按下的动作，探针合成的「按住」会被悄悄松开 ——
    /// 表现是需要持续输入的判据失败，而失败信息指向玩法。记成独立判据：这一轮能不能算。
    /// </summary>
    public override void _Notification(int what)
    {
        if (what == NotificationApplicationFocusOut) _focusLost++;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_capturing) return;
        _frame++;
        if (_probe)
        {
            // 渲染帧号没前进的物理帧：一次迭代补跑了多帧，或窗口被遮住导致渲染停摆。
            var drawn = (int)Engine.GetFramesDrawn();
            if (_drawn >= 0 && drawn == _drawn) _tickSlips++;
            _drawn = drawn;
        }
        if (_inputCase >= 0)
        {
            AdvanceInputProbe(delta);
            return;
        }
        var before = _stop.RemainingFrames;
        if (_stop.Tick())
        {
            if (_probe)
            {
                _frozen++;
                if (!_heavy && _frozen == 1)
                {
                    Press(InputActions.Jump, true);
                    Press(InputActions.Sprint, true);
                    if (!_flashSaved) SaveFlash();
                }
                if (!_heavy && _frozen == 2) Press(InputActions.Jump, false);
                _freezeValid &= _stop.RemainingFrames == before - 1 && _player.Position == _playerAt
                    && _dummy.Position == _dummyAt && _camera.Position == _cameraAt && _camera.Offset == _offset
                    && _player.Combat.Motor.DepthWorldPx == _depthAt
                    && _player.Sprite.Frame == _spriteFrame && _player.Combat.Combo.FrameInPhase == _phaseFrame
                    && _dummy.Statuses.Get(StatusKind.Hitstun).RemainingFrames == _stun
                    && _dummy.FlashRemaining == CombatFeel.FlashFrames;
            }
            return;
        }
        if (_probe && _frozen > 0)
        {
            Check($"{Kind}-freeze", _freezeValid && _frozen == (_heavy ? 5 : 3));
            if (!_heavy)
            {
                _inputHeld = _router.IsPressed(InputActions.Sprint);
                Check("held-input-survives", _inputHeld);
                Press(InputActions.Sprint, false);
            }
            _frozen = 0;
        }
        _player.AdvanceCombat();
        _dummy.AdvanceCombat();
        _camera.Advance(delta);
        var hits = _hitbox.Resolve(_player, Feedback);
        if (!_probe) return;
        if (_frame == 10) Press(InputActions.AttackLight, true);
        if (_frame == 11) Press(InputActions.AttackLight, false);
        if (hits > 0)
        {
            Check($"{Kind}-active-hit", _player.Combat.Combo.IsHitActive && _player.Combat.Combo.FrameInPhase == 0 && hits == 1);
            Check($"{Kind}-fresh-stun", _dummy.Statuses.Get(StatusKind.Hitstun).RemainingFrames == (_heavy ? 18 : 10));
            _hitX = _dummy.Position.X;
            _playerAt = _player.Position; _dummyAt = _dummy.Position; _cameraAt = _camera.Position;
            _offset = _camera.Offset; _spriteFrame = _player.Sprite.Frame; _phaseFrame = _player.Combat.Combo.FrameInPhase;
            _depthAt = _player.Combat.Motor.DepthWorldPx;
            _stun = _dummy.Statuses.Get(StatusKind.Hitstun).RemainingFrames;
        }
        _sawShake |= _camera.Offset != Vector2.Zero;
        if (_frame == 38 || _frame == 90)
        {
            Check($"{Kind}-distance", Math.Abs(_dummy.Position.X - _hitX - (_heavy ? 16 : 8)) < 0.002);
            Check($"{Kind}-expired", !_dummy.Statuses.Has(StatusKind.Hitstun) && _dummy.FlashRemaining == 0);
            Check($"{Kind}-dedup", _dummy.HitCount == (_heavy ? 2 : 1));
            Check($"{Kind}-shake", _heavy && _shakeEnabled ? _sawShake : !_sawShake);
            Check($"{Kind}-input-no-replay", _player.IsOnFloor());
            Check($"{Kind}-inactive", !_hitbox.IsActive);
            if (!_heavy)
            {
                _heavy = true;
                _dummy.Position = new Vector2(184, 140);
                Press(InputActions.AttackHeavy, true);
            }
            else BeginInputProbe();
        }
        if (_frame == 39) Press(InputActions.AttackHeavy, false);
        if (_frame > 120) { Check("timeout", false); Capture(); }
    }

    // Independent idle stages after both real hits isolate input expiry from attack locks.
    private bool ReadyForInput => _player.IsOnFloor() && !_player.Combat.Combo.IsAttacking
        && _player.Combat.Motor.Phase == MotorPhase.Grounded && _player.Velocity == Vector2.Zero;

    private void BeginInputProbe()
    {
        _inputCase++;
        _inputTick = 0;
        _inputFrozen = 0;
        _sequenceValid = true;
        _sequenceAt = _player.Position;
        _sequenceDepth = _player.Combat.Motor.DepthWorldPx;
        _expectedSpeed = 0;
        Check($"{InputCaseName}-ready", ReadyForInput);
        _stop.Begin(CombatFeel.HeavyHitstopFrames);
    }

    private void AdvanceInputProbe(double delta)
    {
        // Tick's return owns this frame, including remaining=0 on the last frozen frame.
        if (_stop.Tick())
        {
            _inputFrozen++;
            _sequenceValid &= _player.Position == _sequenceAt && ReadyForInput
                && _player.Combat.Motor.DepthWorldPx == _sequenceDepth
                && _stop.RemainingFrames == CombatFeel.HeavyHitstopFrames - _inputFrozen;
            if (_inputFrozen == 1)
            {
                Press(_inputCase < 3 ? PulseActions[_inputCase] : InputActions.MoveRight, true);
                if (_inputCase == 3) Press(InputActions.Sprint, true);
            }
            if (_inputFrozen == 2)
            {
                _sequenceValid &= _router.IsPressed(_inputCase < 3 ? PulseActions[_inputCase] : InputActions.MoveRight);
                if (_inputCase < 3) Press(PulseActions[_inputCase], false);
                else _sequenceValid &= _router.IsPressed(InputActions.Sprint);
            }
            if (_inputFrozen >= 3 && _inputCase < 3)
                _sequenceValid &= !_router.IsPressed(PulseActions[_inputCase]);
            return;
        }
        _inputTick++;
        if (_inputTick == 1)
            Check($"{InputCaseName}-frozen-sequence", _sequenceValid && _inputFrozen == CombatFeel.HeavyHitstopFrames);
        var at = _player.Position;
        _player.AdvanceCombat();
        _dummy.AdvanceCombat();
        _camera.Advance(delta);
        if (_inputCase < 3)
        {
            if (_inputTick <= 8)
            {
                _sequenceValid &= ReadyForInput && _player.Position == _sequenceAt;
                if (_inputTick == 1) Check($"{InputCaseName}-first-resume", _sequenceValid);
                if (_inputTick == 8)
                {
                    Check($"{InputCaseName}-no-replay", _sequenceValid);
                    Press(PulseActions[_inputCase], true);
                }
            }
            if (_inputTick == 9)
            {
                Check($"{InputCaseName}-fresh-press", _inputCase == 0
                    ? !_player.IsOnFloor() && _player.Velocity.Y < 0 && _player.Position.Y < at.Y
                    : _player.Combat.Combo.IsAttacking && _player.Combat.Combo.Kind == (_inputCase == 1 ? ComboKind.Light : ComboKind.Heavy));
                Press(PulseActions[_inputCase], false);
            }
            if (_inputTick > 9 && ReadyForInput) BeginInputProbe();
        }
        else
        {
            var target = _inputTick <= 12 ? CombatFeel.DashSpeedPixelsPerSecond
                : _inputTick <= 17 ? CombatFeel.MoveSpeedPixelsPerSecond : 0;
            var step = (_expectedSpeed > target ? CombatFeel.HorizontalDecelerationPixelsPerSecondSquared
                : CombatFeel.HorizontalAccelerationPixelsPerSecondSquared) * CombatFeel.FrameSeconds;
            _expectedSpeed += Math.Clamp(target - _expectedSpeed, -step, step);
            var motor = _player.Combat.Motor;
            var valid = Math.Abs(motor.HorizontalVelocity - _expectedSpeed) < 0.001
                && Math.Abs(_player.Velocity.X - _expectedSpeed) < 0.001
                && Math.Abs(_player.Position.X - at.X - _expectedSpeed * CombatFeel.FrameSeconds) < 0.002
                && Math.Abs(_player.Position.Y - at.Y) < 0.002 && !motor.IsInvulnerable
                // 横向冲刺 22 帧不许把纵深带走一丝：三轴不串在真实输入路径上的形状（`GP-15`）。
                && motor.DepthWorldPx == _sequenceDepth && motor.DepthVelocity == 0
                && motor.Phase == (_inputTick <= 12 ? MotorPhase.Dash : MotorPhase.Grounded);
            _sequenceValid &= valid;
            if (_inputTick == 1) Check("sprint-first-resume", valid);
            if (_inputTick == 12)
            {
                Check("sprint-full-speed", _sequenceValid && _expectedSpeed == CombatFeel.DashSpeedPixelsPerSecond);
                Press(InputActions.Sprint, false);
            }
            if (_inputTick == 13) Check("sprint-release-first", valid);
            if (_inputTick == 17)
            {
                Check("sprint-normal-motion", _sequenceValid && _expectedSpeed == CombatFeel.MoveSpeedPixelsPerSecond);
                Press(InputActions.MoveRight, false);
            }
            if (_inputTick == 22)
            {
                Check("sprint-direction-release", _sequenceValid && ReadyForInput);
                BoundaryProbe();
            }
        }
        if (_inputTick > 120) { Check("input-timeout", false); Capture(); }
    }

    private string Kind => _heavy ? "heavy" : "light";
    private void Feedback(HitReaction reaction)
    {
        _stop.Begin(reaction.HitstopFrames);
        if (reaction.IsHeavy) _camera.Rig.Shake();
    }

    private static void Press(string action, bool pressed) => Callable.From(() => Input.ParseInputEvent(new InputEventAction { Action = action, Pressed = pressed })).CallDeferred();
    private void Check(string name, bool value)
    {
        _checked++;
        if (value) _passed++;
        GD.Print($"[GP13] {(value ? "PASS" : "FAIL")} {name}");
    }

    private async void BoundaryProbe()
    {
        _capturing = true;
        var combo = _player.Combat.Combo;
        // 「近」取实体刚好不互插的最小间距（两个 18 宽实体 → 18），而不是写死一个数：
        // 重叠条件是 距离 ≤ 轻击伸展 + 受击框半宽，而**伸展是从美术量出来的**（`ART-6`）。
        // 原来写死 24 正好踩在那个上界（15+9），于是首次命中后的击退把木桩推开 2px 就掉出框外，
        // 美术一改这条判据就断 —— 实测 2026-09-09 轻击伸展由 18 变 15 时它就是这样失败的。
        // 下面那条前置判据把「18 真的在轻击框内」判死，将来伸展再缩小会当场说清原因。
        Check("probe-near-in-reach",
            NearX <= CombatFeel.LightHitboxWidthWorldPx + Hurtbox.WidthWorldPx / 2);
        _dummy.Position = _player.Position + new Vector2(NearX, 0);
        await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        combo.Tick(true, false, true);
        Check("startup-gate", _hitbox.Resolve(_player, _ => { }) == 0 && !_hitbox.IsActive);
        for (var i = 0; i < CombatFeel.LightStartupFrames; i++) combo.Tick(false, false, true);
        Check("pre-overlap", _hitbox.Resolve(_player, _ => { }) == 1);
        // 进出都用**绝对**定位：命中会施加击退（Resolve 里 dummy.Receive），相对位移会把那点漂移
        // 累加进后面每一次「回到近处」，于是判据实际测的距离一轮比一轮远。
        _dummy.Position = _player.Position + new Vector2(FarX, 0);
        await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        Check("exit-active", _hitbox.Resolve(_player, _ => { }) == 0);
        _dummy.Position = _player.Position + new Vector2(NearX, 0);
        await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        Check("reenter-dedup", _hitbox.Resolve(_player, _ => { }) == 0);
        for (var i = 0; i < CombatFeel.LightActiveFrames; i++) combo.Tick(false, false, true);
        Check("first-recovery", _hitbox.Resolve(_player, _ => { }) == 0 && !_hitbox.IsActive && combo.FrameInPhase == 0);
        while (!combo.IsComboWindowOpen) combo.Tick(false, false, true);
        combo.Tick(true, false, true);
        _hitbox.Resolve(_player, _ => { });
        _dummy.Position = _player.Position + new Vector2(FarX, 0);
        await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        for (var i = 0; i < CombatFeel.LightStartupFrames; i++) combo.Tick(false, false, true);
        Check("active-empty", _hitbox.Resolve(_player, _ => { }) == 0);
        combo.Tick(false, false, true);
        combo.Tick(false, false, true);
        _dummy.Position = _player.Position + new Vector2(NearX, 0);
        await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        Check("last-active-entry", combo.FrameInPhase == CombatFeel.LightActiveFrames - 1 && _hitbox.Resolve(_player, _ => { }) == 1);
        combo.Tick(false, false, true);
        Check("next-recovery", _hitbox.Resolve(_player, _ => { }) == 0);
        while (combo.IsAttacking) combo.Tick(false, false, true);
        combo.Tick(true, false, false);
        for (var i = 0; i < CombatFeel.LightStartupFrames; i++) combo.Tick(false, false, false);
        combo.AfterMove(true);
        Check("landing-cancel", _hitbox.Resolve(_player, _ => { }) == 0 && !combo.IsAttacking);
        // 下面这几条测的是**击退记账**（新命中替换剩余位移、到期不尾滑、碰墙被挡），不是贴身距离。
        // 所以先把木桩挪到两侧都有空地的地方：上面那段把它放在 NearX，那是「实体刚好相邻」，
        // 向左的击退会被主角实体挡住，于是记账判据会因为一个与它无关的碰撞而失败。
        _dummy.Position = _player.Position + new Vector2(FarX, 0);
        await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        _dummy.Receive(HitResolution.Resolve(ComboKind.Heavy), 1);
        for (var i = 0; i < 4; i++) _dummy.AdvanceCombat();
        var start = _dummy.Position.X;
        _dummy.Receive(HitResolution.Resolve(ComboKind.Light), -1);
        for (var i = 0; i < 10; i++) _dummy.AdvanceCombat();
        Check("refresh-distance", Math.Abs(_dummy.Position.X - start + 8) < 0.002 && !_dummy.Statuses.Has(StatusKind.Hitstun));
        start = _dummy.Position.X;
        _dummy.AdvanceCombat();
        Check("no-tail-slide", _dummy.Position.X == start);
        var wall = new StaticBody2D { Position = _dummy.Position + new Vector2(15, -16) };
        wall.AddChild(new CollisionShape2D { Shape = new RectangleShape2D { Size = new Vector2(4, 64) } });
        AddChild(wall);
        await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        _dummy.Receive(HitResolution.Resolve(ComboKind.Heavy), 1);
        for (var i = 0; i < 18; i++) _dummy.AdvanceCombat();
        Check("wall-block", _dummy.Position.X > start && _dummy.Position.X < start + 4.01f);
        wall.QueueFree();
        await ReachSplitProbe();
        await DepthToleranceProbe();
        Capture();
    }

    /// <summary>
    /// 判定框轻重分开之后的**行为级**证明（`ART-6`）：同一个距离，轻击打空、重击打到。
    /// </summary>
    /// <remarks>
    /// 为什么不能只靠「常量不相等」：常量分开了但 <c>Hitbox</c> 忘了按轻重换尺寸，两个数照样
    /// 各自正确、测试照样全绿，而画面上重击仍然打不到远处的东西。这里用真实物理世界查询走一遍：
    /// 木桩摆在木桩受击框左沿 21px 处（轻 18 够不到、重 22 刚够到），两条判据一正一反。
    /// </remarks>
    private async Task ReachSplitProbe()
    {
        var combo = _player.Combat.Combo;
        Check("reach-split-shape",
            Hitbox.SizeFor(ComboKind.Heavy).X > Hitbox.SizeFor(ComboKind.Light).X);
        while (combo.IsAttacking) combo.Tick(false, false, true);
        _dummy.Position = _player.Position + new Vector2(30, 0);
        await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        combo.Tick(true, false, true);
        for (var i = 0; i < CombatFeel.LightStartupFrames; i++) combo.Tick(false, false, true);
        Check("light-reach-miss", combo.IsHitActive && _hitbox.Resolve(_player, _ => { }) == 0);
        while (combo.IsAttacking) combo.Tick(false, false, true);
        combo.Tick(false, true, true);
        for (var i = 0; i < CombatFeel.HeavyStartupFrames; i++) combo.Tick(false, false, true);
        Check("heavy-reach-hit", combo.IsHitActive && combo.Kind == ComboKind.Heavy
            && _hitbox.Resolve(_player, _ => { }) == 1);
        while (combo.IsAttacking) combo.Tick(false, false, true);
        // 这一段真打中了，所以木桩正在闪白。把它推到闪白与硬直都结束，好让紧接着的
        // restored-pixel 量的是「恢复后的颜色」而不是本段留下的白 —— 上一版漏了这一步，
        // 表现就是 restored-pixel 失败，而原因与它自己要测的东西无关。
        for (var i = 0; i < 64 && (_dummy.FlashRemaining > 0
                 || _dummy.Statuses.Has(StatusKind.Hitstun)); i++)
        {
            _dummy.AdvanceCombat();
        }
        Check("reach-split-drained", _dummy.FlashRemaining == 0
            && !_dummy.Statuses.Has(StatusKind.Hitstun));
    }

    /// <summary>
    /// 纵深容差的**行为级**证明（`GP-16`）：同一个横向距离，纵深对齐时打到、错开一排时打空。
    /// </summary>
    /// <remarks>
    /// 为什么不能只靠规则层单测：<c>DepthOverlap</c> 全绿而 <c>Hitbox</c> 忘了调它，命中照旧是单
    /// 平面的 —— 两边各自正确、测试照样全绿，而画面上隔着一排也能打中。这里走真实物理查询，横向
    /// 距离全程钉死在 <see cref="NearX"/>（已由 <c>probe-near-in-reach</c> 判死在轻击框内），
    /// **只有纵深在变**，于是打不到只能是纵深那一半造成的。
    ///
    /// 「纵深对齐时打到」这一头其实已经被前面 60 多条判据全体覆盖了 —— 本场景两个角色都在带中线，
    /// 纵深条件一旦漏判，那些判据会成片变红。所以这里只补它们盖不到的四种形状：错开一排打空、
    /// 同一次挥击里挪回同排能打中（打空没被登记成打过）、正好差一个容差仍打中、击退不碰纵深。
    ///
    /// 距离一律从常量导出，不写死（踩坑记录 48）：「错开」取正典的一排间距，「边界」取容差本身。
    /// </remarks>
    private async Task DepthToleranceProbe()
    {
        var combo = _player.Combat.Combo;
        var motor = _player.Combat.Motor;
        var aligned = motor.DepthWorldPx;
        var edge = aligned + CombatFeel.HitDepthToleranceWorldPx;
        var offRow = aligned + DepthBand.RowSpacingWorldPx;
        // 前提判据：本阶段要用的两个纵深都得落在带内，且「错开一排」真的在容差之外。前一半要紧是
        // 因为 `PlaceDepth` 会钳 —— 主角的纵深恒在带内，所以钳过只会让**实际纵深差变小**，于是
        // 「该打空的」可能变成打中。容差调到一排以上时这条当场说清原因，而不是让下面那条莫名失败。
        Check("probe-depth-rows-derived",
            CombatFeel.HitDepthToleranceWorldPx < DepthBand.RowSpacingWorldPx
            && DepthBand.Contains(edge) && DepthBand.Contains(offRow));

        // 先让判定框看见一个非 Active 帧。**这不是仪式，是量具的前提**：每挥击一个去重集合靠
        // `Resolve` 在相邻两次调用之间观察到的 Active 上升沿清空（见 `Hitbox.Resolve` 的调用契约），
        // 而上一阶段最后一次调用停在 Active 上。不先清一次，本阶段整段都会被当成「这次挥击已经打过
        // 它了」而静默打空 —— 2026-09-11 实测踩过：depth-off-row-miss 因此**假绿**（返回 0 是被去重
        // 挡的，不是纵深挡的），下游三条同时变红，而失败信息指向纵深。
        while (combo.IsAttacking) combo.Tick(false, false, true);
        Check("depth-probe-fresh-swing", _hitbox.Resolve(_player, _ => { }) == 0 && !_hitbox.IsActive);

        _dummy.Position = _player.Position + new Vector2(NearX, 0);
        _dummy.PlaceDepth(offRow);
        // 只有**物理位置**要等一帧让空间状态刷新；纵深不参与引擎碰撞，改它不需要等（也等不到）。
        await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        combo.Tick(true, false, true);
        for (var i = 0; i < CombatFeel.LightStartupFrames; i++) combo.Tick(false, false, true);
        var hitsBefore = _dummy.HitCount;
        // `RejectedAsAlreadyHit == 0` 是这条判据的**量具**：它说明这个 0 是纵深挡下来的，而不是
        // 去重挡下来的。少了它，去重集合一脏这条就假绿，而它声称测的东西压根没被测到。
        Check("depth-off-row-miss", combo.IsHitActive
            && DepthOverlap.SeparationWorldPx(motor.DepthWorldPx, _dummy.DepthWorldPx)
                == DepthBand.RowSpacingWorldPx
            && _hitbox.Resolve(_player, _ => { }) == 0 && _dummy.HitCount == hitsBefore
            && _hitbox.RejectedAsAlreadyHit == 0);

        // **同一次挥击**里只把纵深挪回同一排：打空没有被登记成打过，所以这一下必须中。去重排在
        // 纵深判定之前也不报错，只表现为「刚才错开那一下把这次挥击用掉了」—— 玩家侧的形状是
        // 「贴着敌人挥空一次之后，站对了也还是空」，而那会被归因于判定不准。
        _dummy.PlaceDepth(aligned);
        Check("depth-realign-hits-same-swing", combo.IsHitActive
            && DepthOverlap.SeparationWorldPx(motor.DepthWorldPx, _dummy.DepthWorldPx) == 0.0
            && _hitbox.Resolve(_player, _ => { }) == 1 && _dummy.HitCount == hitsBefore + 1
            && _hitbox.RejectedAsAlreadyHit == 0);

        // 容差边界：正好差一个容差仍要打中（边界含在内）。这一条把那个常量钉在真实物理世界里 ——
        // 规则层单测只证明 `Within` 自己含边界，证不了引擎传的是同一个容差。
        while (combo.IsAttacking) combo.Tick(false, false, true);
        // 同 depth-probe-fresh-swing：换挥击要让本机看见一个非 Active 帧，否则去重集合还留着上一次
        // 的内容。下面那条判据里的 `RejectedAsAlreadyHit == 0` 就是它没生效时的报警。
        _hitbox.Resolve(_player, _ => { });
        _dummy.PlaceDepth(edge);
        combo.Tick(true, false, true);
        for (var i = 0; i < CombatFeel.LightStartupFrames; i++) combo.Tick(false, false, true);
        Check("depth-tolerance-edge-hit", combo.IsHitActive
            && DepthOverlap.SeparationWorldPx(motor.DepthWorldPx, _dummy.DepthWorldPx)
                == CombatFeel.HitDepthToleranceWorldPx
            && _hitbox.Resolve(_player, _ => { }) == 1
            && _hitbox.RejectedAsAlreadyHit == 0);

        // 击退**只沿横向**（`GP-16` 定，理由记在 issue 笔记）：纵深上不施加击退。整段硬直跑完再看，
        // 不抽查一帧 —— 击退是按硬直帧数摊开的，「每帧往前偷挪一点纵深」这种形状抽查看不出来。
        //
        // **纵深那一半按精确相等判，横向那一半按引擎自报的接触边距判。** 两个轴的量具不同不是偷懒：
        // 纵深位置在规则层、没有引擎介入，本该分毫不动；横向位置由 `MoveAndCollide` 推进，而木桩这时
        // **正贴着主角**（NearX 是两个 18 宽实体刚好不互插的间距），第一帧会带一次脱离接触的推出。
        // 实测 dx=8.0748 对 8，超出量落在 `SafeMargin`（引擎默认 0.08）以内 —— 容差因此取引擎自报的
        // 那个边距，不自己挑一个刚好能过的数（踩坑记录 49）。方向另判：位移符号必须等于攻击者朝向。
        //
        // `freshHit` 不是装饰：本阶段前面那一下也是轻击、击退量一模一样，所以边界那一下万一没打中，
        // 这里会**静默量到上一次命中留下的硬直**，判据照样全绿。2026-09-11 反证时正是这个形状 ——
        // 把边界改成不含之后 depth-tolerance-edge-hit 变红，而本条仍然 PASS。所以要钉「刚打中」。
        var depthBefore = _dummy.DepthWorldPx;
        var xBefore = _dummy.Position.X;
        var freshHit = _dummy.Statuses.Get(StatusKind.Hitstun).RemainingFrames
            == CombatFeel.LightHitstunFrames;
        for (var i = 0; i < CombatFeel.LightHitstunFrames; i++) _dummy.AdvanceCombat();
        var dx = _dummy.Position.X - xBefore;
        GD.Print($"[GP13] knockback dx={dx:F4} expect={CombatFeel.LightKnockbackWorldPx}"
            + $" margin={_dummy.SafeMargin} depth={depthBefore}→{_dummy.DepthWorldPx}");
        Check("knockback-horizontal-only", freshHit && _dummy.DepthWorldPx == depthBefore
            && Math.Sign(dx) == motor.Facing
            && Math.Abs(dx - CombatFeel.LightKnockbackWorldPx) <= _dummy.SafeMargin
            && !_dummy.Statuses.Has(StatusKind.Hitstun));

        // 还原摆位：本阶段把木桩挪近了、纵深也错开了，而 restored-pixel 是按木桩自身变换取屏幕
        // 像素的 —— 纵深偏移会把取样点顶到柱子边沿之外。理由与 reach-split-drained 那段同类：
        // 别让下一条判据因为上一段留下的状态而失败。
        _dummy.PlaceDepth(aligned);
        _dummy.Position = _player.Position + new Vector2(30, 0);
        Check("depth-probe-restored", _dummy.FlashRemaining == 0
            && !_dummy.Statuses.Has(StatusKind.Hitstun)
            && DepthOverlap.SeparationWorldPx(motor.DepthWorldPx, _dummy.DepthWorldPx) == 0.0);
    }

    private async void SaveFlash()
    {
        _flashSaved = true;
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var image = GetViewport().GetTexture().GetImage();
        var at = _dummy.GetGlobalTransformWithCanvas() * new Vector2(0, -16);
        var scale = new Vector2(image.GetWidth(), image.GetHeight()) / GetViewport().GetVisibleRect().Size;
        var pixel = image.GetPixel((int)(at.X * scale.X), (int)(at.Y * scale.Y));
        Check("flash-pixel", pixel.R > 0.99 && pixel.G > 0.99 && pixel.B > 0.99
            && image.SavePng(OS.GetEnvironment("GP13_SHOT") + ".flash.png") == Error.Ok);
    }

    private async void Capture()
    {
        _capturing = true;
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var image = GetViewport().GetTexture().GetImage();
        var at = _dummy.GetGlobalTransformWithCanvas() * new Vector2(0, -16);
        var scale = new Vector2(image.GetWidth(), image.GetHeight()) / GetViewport().GetVisibleRect().Size;
        var pixel = image.GetPixel((int)(at.X * scale.X), (int)(at.Y * scale.Y));
        Check("restored-pixel", pixel.R > pixel.G && pixel.G < 0.8 && _dummy.FlashRemaining == 0);
        Check("screenshot", image.GetWidth() > 0 && image.SavePng(OS.GetEnvironment("GP13_SHOT")) == Error.Ok);
        // 两条前提判据，理由同 PlayerDev：前提不成立就说前提不成立，不让它去毁一条无关判据。
        GD.Print($"[GP13] physicsFrames={_frame} tickSlips={_tickSlips} focusLost={_focusLost}");
        Check("tick-frame-1to1", _tickSlips == 0 && _frame > 0);
        Check("focus-kept", _focusLost == 0);
        GD.Print($"[GP13] Summary {_passed}/{_checked}");
        GetTree().Quit(_passed == _checked ? 0 : 1);
    }
}
