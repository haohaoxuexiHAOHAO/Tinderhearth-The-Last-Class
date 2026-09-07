namespace Tinderhearth.Rules.Combat;

/// <summary>主角的运动相位。</summary>
public enum MotorPhase
{
    /// <summary>站在地面（待机或走动，由横向速度区分）。</summary>
    Grounded,

    /// <summary>离地（上升或下落）。</summary>
    Airborne,

    /// <summary>闪避翻滚中，其间有无敌窗。</summary>
    Dodge,

    /// <summary>冲刺中，无无敌帧。</summary>
    Dash,
}

/// <summary>
/// 主角的运动状态机（`GP-10`）：移动、跳跃、闪避、冲刺。逐帧推进，纯逻辑、不碰引擎。
/// </summary>
/// <remarks>
/// **规则层持有速度，引擎层持有位置。** 本机每帧算出期望的横向与纵向速度（世界像素／秒），引擎把
/// 它抄进 <c>CharacterBody2D</c> 做 <c>move_and_slide</c> 并回报 <c>isOnFloor</c>。跳跃的初速与重力
/// 因此可脱引擎测（给定固定物理步长，纵向速度的逐帧曲线是确定的），而与平台的碰撞交给引擎。
///
/// **闪避方向取按下瞬间的输入**（`GP-9`）：起手时锁定方向，翻滚途中改方向不影响 —— 食指离键那一瞬
/// 方向变了是玩家主动松键的结果，用旧方向反而违反「操作结果与意图一致」。无方向输入时用面朝方向。
///
/// **出招时地面定身**：`attacking` 为真且在地面时横向速度归零，且不接受跳跃/闪避/冲刺的起手 ——
/// 攻击不能取消到位移（`GP-4` 不做取消）。空中攻击仍可微调漂移，落地打断由连段机负责。
///
/// **闪避是排他且不可打断的**：一旦起手就走完 <see cref="CombatFeel.DodgeDurationFrames"/> 帧，期间
/// 忽略其它输入 —— 翻滚中途可被打断的话无敌窗就不可信了。
/// </remarks>
public sealed class MotorState
{
    private static readonly double Dt = CombatFeel.FrameSeconds;

    private double _verticalVelocity;
    private int _dodgeFrame;
    private int _dodgeDirection = 1;

    /// <summary>当前运动相位。</summary>
    public MotorPhase Phase { get; private set; } = MotorPhase.Grounded;

    /// <summary>面朝方向，−1 左 / +1 右。默认朝右。</summary>
    public int Facing { get; private set; } = 1;

    /// <summary>本帧期望的横向速度，世界像素／秒。引擎乘 delta 后施加。</summary>
    public double HorizontalVelocity { get; private set; }

    /// <summary>本帧的纵向速度，世界像素／秒，负为向上。</summary>
    public double VerticalVelocity => _verticalVelocity;

    /// <summary>角色唯一状态载体；本机 Tick 在每个非顿帧逻辑帧开头推进，外层不得再 Tick。</summary>
    public StatusEffects Statuses { get; } = new();

    /// <summary>这一帧处于无敌吗；唯一依据是统一载体。</summary>
    public bool IsInvulnerable => Statuses.Has(StatusKind.Invulnerable);

    /// <summary>推进一帧。</summary>
    /// <param name="input">本帧输入。</param>
    /// <param name="isOnFloor">引擎回报角色此刻是否踩在地面。</param>
    /// <param name="attacking">连段机是否正在出招（<see cref="ComboStateMachine.IsAttacking"/>）。</param>
    public void Tick(in CombatInput input, bool isOnFloor, bool attacking)
    {
        Statuses.Tick();
        if (Phase == MotorPhase.Dodge)
        {
            AdvanceDodge(isOnFloor);
            return;
        }

        var dir = input.Direction;
        if (dir != 0)
        {
            Facing = dir;
        }

        // 起手闪避：地面、非出招。方向此刻锁定（GP-9）。
        if (input.DodgePressed && isOnFloor && !attacking)
        {
            _dodgeDirection = dir != 0 ? dir : Facing;
            Facing = _dodgeDirection;
            _dodgeFrame = 0;
            AdvanceDodge(isOnFloor);
            return;
        }

        // 跳跃：地面、非出招。冲量帧给满初速、当帧不扣重力 —— 否则起跳速度永远差一个重力步。
        var justJumped = false;
        if (input.JumpPressed && isOnFloor && !attacking)
        {
            _verticalVelocity = -CombatFeel.JumpInitialPixelsPerSecond;
            justJumped = true;
        }

        // 竖向：在地面且不再上升就贴地，否则受重力（冲量帧除外）。
        var grounded = isOnFloor && _verticalVelocity >= 0.0 && !justJumped;
        if (grounded)
        {
            _verticalVelocity = 0.0;
        }
        else if (!justJumped)
        {
            _verticalVelocity += CombatFeel.GravityPixelsPerSecondSquared * Dt;
        }

        // 冲刺：地面、非出招、按住冲刺且有方向。
        var dashing = grounded && !attacking && input.SprintHeld && dir != 0;

        if (attacking && grounded)
        {
            HorizontalVelocity = 0.0;
        }
        else if (dashing)
        {
            HorizontalVelocity = dir * (double)CombatFeel.DashSpeedPixelsPerSecond;
        }
        else
        {
            HorizontalVelocity = dir * (double)CombatFeel.MoveSpeedPixelsPerSecond;
        }

        Phase = !grounded
            ? MotorPhase.Airborne
            : dashing ? MotorPhase.Dash : MotorPhase.Grounded;
    }

    private void AdvanceDodge(bool isOnFloor)
    {
        HorizontalVelocity = _dodgeDirection * (double)CombatFeel.DodgeSpeedPixelsPerSecond;
        _verticalVelocity = 0.0;
        if (_dodgeFrame == CombatFeel.DodgeInvulnStartFrame)
        {
            Statuses.Apply(StatusKind.Invulnerable,
                CombatFeel.DodgeInvulnEndFrame - CombatFeel.DodgeInvulnStartFrame);
        }
        Phase = MotorPhase.Dodge;

        _dodgeFrame++;
        if (_dodgeFrame >= CombatFeel.DodgeDurationFrames)
        {
            _dodgeFrame = 0;
            HorizontalVelocity = 0.0;
            Phase = isOnFloor ? MotorPhase.Grounded : MotorPhase.Airborne;
        }
    }
}
