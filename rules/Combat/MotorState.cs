namespace Tinderhearth.Rules.Combat;

/// <summary>主角的运动相位。</summary>
public enum MotorPhase
{
    /// <summary>站在地面，待机或走动，两者由横向速度区分。</summary>
    Grounded,

    /// <summary>离地，上升或下落。</summary>
    Airborne,

    /// <summary>闪避中，其间有一段无敌窗。</summary>
    /// <remarks>
    /// 是闪步不是翻滚：身体不绕轴翻转、头始终朝上，短距突进之后起身。标识符叫 <c>Dodge</c> 没错，
    /// 它说的是「带无敌窗的规避手段」这个角色。
    /// </remarks>
    Dodge,

    /// <summary>冲刺中，没有无敌帧。</summary>
    Run,

    /// <summary>受击硬直：不接受输入，只吃横向击退，纵深不动。</summary>
    Hurt,
}

/// <summary>主角的运动状态机：移动、跳跃、闪避、冲刺。逐帧推进，纯逻辑、不碰引擎。</summary>
/// <remarks>
/// 三个轴各自独立：横向、跳跃高度、纵深。任一轴的输入不得改动另一轴，空间模型见设计仓
/// canon/gameplay/战斗与关卡.md 的「战斗关卡的空间模型：带纵深的横版」一节。
///
/// 横向与竖向是规则层持速度、引擎层持位置：本机每帧算出期望速度，引擎抄进 <c>CharacterBody2D</c>
/// 做 <c>MoveAndSlide</c> 并回报踩不踩着地面。纵深反过来，位置也在规则层 —— 引擎那边没有第三个轴，
/// 若让它持纵深位置，带宽钳制就得写进引擎层或者两处各写一份，而钳制正是要能脱引擎单测的东西。
///
/// 于是有一条代码拦不住的约束：引擎层只能读 <see cref="DepthWorldPx"/>，绝不许自己再积分一遍纵深
/// 速度 —— 那会得到两倍位移，而且不报错。纵深上的阻挡也不在本类，它由引擎层在外面把挤进来的一方
/// 夹回去（判定在 <see cref="DepthBlocking"/>，落地在 <c>src/World/DepthBlocker.cs</c>，写入口仍是
/// <see cref="PlaceDepth"/>）—— 本类不认识别的角色。
/// </remarks>
public sealed class MotorState
{
    private static readonly double Dt = CombatFeel.FrameSeconds;

    private double _verticalVelocity;
    private int _dodgeFrame;
    private int _dodgeDirection = 1;
    private int _dodgeDepthDirection;
    private double _knockbackVelocity;

    /// <summary>当前运动相位。</summary>
    public MotorPhase Phase { get; private set; } = MotorPhase.Grounded;

    /// <summary>面朝方向，−1 左、+1 右，默认朝右。</summary>
    /// <remarks>纵深输入不改朝向 —— 侧视精灵只有左右两面。</remarks>
    public int Facing { get; private set; } = 1;

    /// <summary>本帧期望的横向速度，世界像素／秒。引擎乘 delta 后施加。</summary>
    public double HorizontalVelocity { get; private set; }

    /// <summary>本帧的纵向速度，世界像素／秒，负为向上。</summary>
    public double VerticalVelocity => _verticalVelocity;

    /// <summary>当前纵深位置，世界像素，恒在 <see cref="DepthBand"/> 内。它是连续量，不是轨道号。</summary>
    /// <remarks>引擎层只读它，不得自己再积分一遍纵深速度，理由见类注释。</remarks>
    public double DepthWorldPx { get; private set; } = DepthBand.CenterWorldPx;

    /// <summary>本帧真实发生的纵深速度，世界像素／秒，正为向前（靠近镜头）。</summary>
    /// <remarks>
    /// 钳在带沿上时它是 0，不是「按着键所以还在动」的那个目标值。留着假速度会让绘制排序与影子
    /// 以为角色还在挪，而它已经贴住带沿 —— 这件事不报错。
    /// </remarks>
    public double DepthVelocity { get; private set; }

    /// <summary>离地期间纵深锁定中吗。</summary>
    /// <remarks>
    /// 一旦不在地面，纵深输入不生效、纵深速度为零，落地即自动解锁；闪步途中掉出平台也照锁。空中
    /// 还能挪纵深的话，「跳起来躲横扫」与「往里挪半步躲横扫」会变成同一次输入里能一起做完的事。
    ///
    /// 名字里带 Air 是有意的：出招定身时纵深速度也是零，但那和横向是同一条封锁、不是这个锁。叫
    /// <c>IsDepthLocked</c> 会被读成「纵深现在动不了」，于是出招时它为假就成了看起来的矛盾。
    /// </remarks>
    public bool IsDepthAirLocked { get; private set; }

    /// <summary>角色状态的唯一载体。本机 Tick 在每个非顿帧逻辑帧开头推进它，外层不得再推进一次。</summary>
    public StatusEffects Statuses { get; } = new();

    /// <summary>这一帧处于无敌吗。唯一依据是 <see cref="Statuses"/>。</summary>
    public bool IsInvulnerable => Statuses.Has(StatusKind.Invulnerable);

    /// <summary>把角色摆到带内某个纵深上。带外的值被钳进带内。</summary>
    /// <remarks>
    /// 只在摆位时调：它直接改位置、不产生速度，运动途中调等于让角色瞬移。
    ///
    /// 调用方是单测（要把起点摆到带后沿，才量得出一次纵深闪避的全长而不被钳制截断）与纵深探针
    /// （验排序要两具身体重叠着站在不同纵深上）。关卡按站位摆敌人纵深将来走同一条路。
    /// </remarks>
    public void PlaceDepth(double depthWorldPx)
    {
        DepthWorldPx = DepthBand.Clamp(depthWorldPx);
        DepthVelocity = 0.0;
    }

    /// <summary>
    /// 进入受击硬直：<paramref name="hitstunFrames"/> 帧内不接受输入，横向吃
    /// <paramref name="knockbackVelocity"/>（世界像素／秒，已含方向）的击退，纵深不动。
    /// </summary>
    /// <remarks>
    /// 硬直经 <see cref="StatusEffects"/> 的 <see cref="StatusKind.Hitstun"/> 计时，与木桩同一套
    /// 算法。本方法只管移动与相位 —— 受击帧、顿帧、震屏那几样表现都在引擎层。
    /// </remarks>
    public void Stagger(int hitstunFrames, double knockbackVelocity)
    {
        Statuses.Apply(StatusKind.Hitstun, hitstunFrames);
        _knockbackVelocity = knockbackVelocity;
        Phase = MotorPhase.Hurt;
    }

    /// <summary>推进一帧。</summary>
    /// <param name="input">本帧输入。</param>
    /// <param name="isOnFloor">引擎回报角色此刻是否踩在地面。</param>
    /// <param name="attacking">连段机是否正在出招（<see cref="ComboStateMachine.IsAttacking"/>）。</param>
    public void Tick(in CombatInput input, bool isOnFloor, bool attacking)
    {
        Statuses.Tick();
        // 受击硬直优先于一切：挨打期间不接受输入，不能出招、闪避或冲刺，只吃横向击退。排在闪避
        // 之前 —— 被打到就该从任何相位进入硬直。
        if (Phase == MotorPhase.Hurt || Statuses.Has(StatusKind.Hitstun))
        {
            AdvanceHurt(isOnFloor);
            return;
        }
        if (Phase == MotorPhase.Dodge)
        {
            // 闪避排他且不可打断：一起手就走完全程，期间忽略其它输入 —— 中途能被打断的话，
            // 那段无敌窗就不可信了。
            AdvanceDodge(isOnFloor);
            return;
        }

        var dir = input.HorizontalDirection;
        if (dir != 0)
        {
            Facing = dir;
        }

        // 起手闪避：地面、非出招。两个轴的方向此刻一起锁定，闪步途中改方向不影响。
        if (input.DodgePressed && isOnFloor && !attacking)
        {
            // 只按纵深时横向为 0，是一次纯纵深闪步；两个方向都没按才退回面朝方向。
            _dodgeDirection = input.HasDirection ? dir : Facing;
            _dodgeDepthDirection = input.DepthDirection;
            _dodgeFrame = 0;
            AdvanceDodge(isOnFloor);
            return;
        }

        // 跳跃：地面、非出招。冲量帧给满初速、当帧不扣重力，否则起跳速度永远差一个重力步。
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

        // 冲刺：地面、非出招、按住冲刺键且有横向方向。纵深不冲刺 —— 多一个纵深冲刺速度，就多一个
        // 没有设计需求、又得实机调的数。
        var dashing = grounded && !attacking && input.RunHeld && dir != 0;

        // 出招时在地面上定身：攻击不能取消成位移。空中出招仍可微调横向漂移，纵深本来就锁着。
        if (attacking && grounded)
        {
            HorizontalVelocity = 0.0;
        }
        else
        {
            var target = dir * (double)(dashing ? CombatFeel.RunSpeedPixelsPerSecond : CombatFeel.MoveSpeedPixelsPerSecond);
            var braking = HorizontalVelocity * target < 0 || Math.Abs(target) < Math.Abs(HorizontalVelocity);
            var step = (braking ? CombatFeel.HorizontalDecelerationPixelsPerSecondSquared
                : CombatFeel.HorizontalAccelerationPixelsPerSecondSquared) * Dt;
            HorizontalVelocity += Math.Clamp(target - HorizontalVelocity, -step, step);
        }

        // 纵深：离地锁定，出招时与横向一同定身；其余情况按输入走行走速度。
        IsDepthAirLocked = !grounded;
        AdvanceDepth(grounded && !attacking
            ? input.DepthDirection * (double)CombatFeel.DepthSpeedPixelsPerSecond
            : 0.0);

        Phase = !grounded
            ? MotorPhase.Airborne
            : dashing ? MotorPhase.Run : MotorPhase.Grounded;
    }

    /// <summary>碰撞之后校正竖速，免得撞顶后下一帧重新施加向上速度。</summary>
    /// <remarks>不碰纵深：纵深轴上没有引擎碰撞体，引擎没有可回报的东西。</remarks>
    public void AfterMove(bool onFloor, bool onCeiling, bool onWall = false, double horizontalVelocity = 0)
    {
        if (onWall) HorizontalVelocity = horizontalVelocity;
        if ((onCeiling && _verticalVelocity < 0) || (onFloor && _verticalVelocity > 0))
        {
            _verticalVelocity = 0;
        }
        if (onFloor && Phase == MotorPhase.Airborne)
        {
            Phase = MotorPhase.Grounded;
        }
    }

    private void AdvanceDodge(bool isOnFloor)
    {
        HorizontalVelocity = _dodgeDirection * (double)CombatFeel.DodgeSpeedPixelsPerSecond;
        _verticalVelocity = isOnFloor ? 0.0
            : _verticalVelocity + CombatFeel.GravityPixelsPerSecondSquared * Dt;
        // 闪步不是纵深的豁免：离地（含途中掉出平台）照锁。
        IsDepthAirLocked = !isOnFloor;
        AdvanceDepth(isOnFloor
            ? _dodgeDepthDirection * (double)CombatFeel.DodgeDepthSpeedPixelsPerSecond
            : 0.0);
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
            // 退出相位但保留本 Tick 的位移输出：引擎还没消费最后那一帧的横向速度，而纵深位移已经在
            // 上面 AdvanceDepth 里发生过了。所以两个轴的闪避总位移都正好是全程帧数乘各自速度。
            Phase = isOnFloor ? MotorPhase.Grounded : MotorPhase.Airborne;
        }
    }

    /// <summary>受击硬直推进：只吃横向击退，纵深锁定、受重力；硬直结束就停下，回到地面或空中相位。</summary>
    private void AdvanceHurt(bool isOnFloor)
    {
        var stunned = Statuses.Has(StatusKind.Hitstun);
        var grounded = isOnFloor && _verticalVelocity >= 0.0;
        HorizontalVelocity = stunned ? _knockbackVelocity : 0.0;
        _verticalVelocity = grounded ? 0.0
            : _verticalVelocity + CombatFeel.GravityPixelsPerSecondSquared * Dt;
        // 击退只沿横向，纵深不动；离地照锁，与别处同一条口径。
        IsDepthAirLocked = !grounded;
        DepthVelocity = 0.0;
        // 硬直还在就停在 Hurt；结束了就停下回到地面或空中，不尾滑。
        Phase = stunned ? MotorPhase.Hurt : grounded ? MotorPhase.Grounded : MotorPhase.Airborne;
    }

    /// <summary>纵深积分与钳制。每个 Tick 恰好调一次，普通与闪避两条路径各自调它。</summary>
    private void AdvanceDepth(double velocity)
    {
        var next = DepthWorldPx + velocity * Dt;
        var clamped = DepthBand.Clamp(next);
        DepthVelocity = clamped == next ? velocity : (clamped - DepthWorldPx) / Dt;
        DepthWorldPx = clamped;
    }
}
