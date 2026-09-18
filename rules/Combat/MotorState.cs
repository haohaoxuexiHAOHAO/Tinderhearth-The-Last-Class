namespace Tinderhearth.Rules.Combat;

/// <summary>主角的运动相位。</summary>
public enum MotorPhase
{
    /// <summary>站在地面（待机或走动，由横向速度区分）。</summary>
    Grounded,

    /// <summary>离地（上升或下落）。</summary>
    Airborne,

    /// <summary>闪避（闪步）中，其间有无敌窗。</summary>
    /// <remarks>
    /// **是闪步（quickstep）不是翻滚**：美术里身体不绕轴翻转、头始终朝上，
    /// 短距突进后起身。此前多处注释与素材描述写「翻滚」，那描述的是一个不存在的动作 —— 标识符
    /// <c>Dodge</c> 本身没错（它表达的是「带无敌窗的规避手段」这个玩法角色），错的只是文案。
    /// </remarks>
    Dodge,

    /// <summary>冲刺中，无无敌帧。</summary>
    Run,

    /// <summary>受击硬直（`GP-14` 接入的 GP-18 一小片）：不接受输入，只吃横向击退，纵深不动。</summary>
    Hurt,
}

/// <summary>
/// 主角的运动状态机（`GP-10`，`GP-15` 改成三轴）：移动、跳跃、闪避、冲刺。逐帧推进，纯逻辑、不碰引擎。
/// </summary>
/// <remarks>
/// **三个轴各自独立**（`GP-15`）：横向（<see cref="HorizontalVelocity"/>）、跳跃高度
/// （<see cref="VerticalVelocity"/>）、纵深（<see cref="DepthWorldPx"/>）。正典把战斗关卡定为带
/// 连续可行走纵深的横版，所以「往里挪半步」与「跳起来」是两件不同的事，任一轴的输入不得改动另一轴。
///
/// **横向与竖向：规则层持速度，引擎层持位置。** 本机每帧算出期望速度（世界像素／秒），引擎抄进
/// <c>CharacterBody2D</c> 做 <c>move_and_slide</c> 并回报 <c>isOnFloor</c>。
///
/// **纵深轴反过来：位置也由规则层持有。** 这是本文承认的一处不对称，理由是引擎那边没有第三个轴 ——
/// Godot 2D 的两个轴已经被横向与跳跃高度占满，纵深上仍然没有碰撞体。若让引擎持纵深位置，48px 带的
/// 钳制就只能写在引擎层或两处各写一份，而「钳制」正是本条要能脱引擎单测的东西。**代价要写明**：引擎层
/// 只读 <see cref="DepthWorldPx"/>，绝不许自己再积分一遍纵深速度 —— 那会得到两倍位移，而且不报错。
///
/// **「纵深上没有碰撞体」不等于「纵深上没有阻挡」**（`GP-17`）：实体阻挡确实存在，但它
/// 不是给纵深造一个碰撞体，而是**由引擎层在本类之外把挤进来的一方夹回去**（判定在
/// <see cref="DepthBlocking"/>，落地在 <c>src/World/DepthBlocker.cs</c>，写入口仍是
/// <see cref="PlaceDepth"/>）。本类不认识别的角色，所以这件事本来就不该在这里做。
///
/// **离地期间纵深锁定**（`GP-15`）：一旦不在地面，纵深输入不生效、纵深速度为零，落地即自动解锁
/// （<see cref="IsDepthAirLocked"/>）。空中还能挪纵深的话，「跳起来躲横扫」与「往里挪半步躲横扫」会
/// 变成同一次输入里能一起做完的事，那两种对策就没有区别了。闪步途中掉出平台也照锁。
///
/// **闪避方向取按下瞬间的输入**（`GP-9`，`GP-15` 起是二维）：起手时把横向与纵深两个方向一起锁定，
/// 闪步途中改方向不影响。只按纵深不按横向时是一次纯纵深闪步（横向速度为零、朝向不变）；两个方向
/// 都没按时才退回面朝方向。
///
/// **出招时地面定身**：`attacking` 为真且在地面时**横向与纵深一起**归零，且不接受跳跃/闪避/冲刺
/// 的起手 —— 攻击不能取消到位移（`GP-4` 不做取消）。空中攻击仍可微调横向漂移（纵深本就锁着），
/// 落地打断由连段机负责。
///
/// **冲刺只加横向。** 它是沿横向轴的突进（正典：无无敌帧的高速位移），按住冲刺键只改
/// <see cref="HorizontalVelocity"/> 的目标值；纵深仍走行走速度，也不进
/// <see cref="MotorPhase.Run"/>。多一个「纵深冲刺速度」就多一个没有设计需求的未校准量。
///
/// **闪避是排他且不可打断的**：一旦起手就走完 <see cref="CombatFeel.DodgeDurationFrames"/> 帧，期间
/// 忽略其它输入 —— 闪步中途可被打断的话无敌窗就不可信了。
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

    /// <summary>面朝方向，−1 左 / +1 右。默认朝右。**纵深输入不改朝向** —— 侧视精灵只有左右两面。</summary>
    public int Facing { get; private set; } = 1;

    /// <summary>本帧期望的横向速度，世界像素／秒。引擎乘 delta 后施加。</summary>
    public double HorizontalVelocity { get; private set; }

    /// <summary>本帧的纵向速度，世界像素／秒，负为向上。</summary>
    public double VerticalVelocity => _verticalVelocity;

    /// <summary>
    /// 当前纵深位置，世界像素，恒在 <see cref="DepthBand"/> 内。**连续量，不是轨道号。**
    /// </summary>
    /// <remarks>引擎层只读它（绘制偏移与排序归 `ENG-15`，命中容差归 `GP-16`），不得再积分一遍。</remarks>
    public double DepthWorldPx { get; private set; } = DepthBand.CenterWorldPx;

    /// <summary>
    /// 本帧**真实发生**的纵深速度，世界像素／秒，正为向前（靠近镜头）。
    /// </summary>
    /// <remarks>
    /// 钳在带沿上时它是 0，而不是「按着键所以还在动」的那个目标值。留假速度的后果是绘制排序与
    /// 影子（`ENG-15`）以为角色还在挪，而它已经贴住带沿 —— 而这件事不报错。
    /// </remarks>
    public double DepthVelocity { get; private set; }

    /// <summary>
    /// 离地锁定中吗（`GP-15` 的「空中锁纵深」）。
    /// </summary>
    /// <remarks>
    /// 名字里带 Air 是有意的：出招定身时纵深速度也是零，但那是与横向同一条封锁口径、不是这个锁。
    /// 叫 <c>IsDepthLocked</c> 会读成「纵深现在动不了」，于是出招时它为假就成了看起来的矛盾。
    /// </remarks>
    public bool IsDepthAirLocked { get; private set; }

    /// <summary>角色唯一状态载体；本机 Tick 在每个非顿帧逻辑帧开头推进，外层不得再 Tick。</summary>
    public StatusEffects Statuses { get; } = new();

    /// <summary>这一帧处于无敌吗；唯一依据是统一载体。</summary>
    public bool IsInvulnerable => Statuses.Has(StatusKind.Invulnerable);

    /// <summary>把角色摆到带内某个纵深上。带外的值被钳进带内。</summary>
    /// <remarks>
    /// **只在摆位时调用**：它直接改位置、不产生速度，运动途中调等于让角色瞬移。
    ///
    /// 调用方：单测（要把起点摆到带后沿，才量得出一次纵深闪避的全长而不被钳制截断）与 `ENG-15`
    /// 的纵深探针（验排序要两具身体重叠着站在不同纵深上）。关卡按站位摆敌人纵深将来走同一条路。
    /// </remarks>
    public void PlaceDepth(double depthWorldPx)
    {
        DepthWorldPx = DepthBand.Clamp(depthWorldPx);
        DepthVelocity = 0.0;
    }

    /// <summary>
    /// 进入受击硬直（`GP-14` 接入的 GP-18 一小片）：<paramref name="hitstunFrames"/> 帧内不接受输入，
    /// 横向吃 <paramref name="knockbackVelocity"/>（世界像素／秒，已含方向）的击退，纵深不动。
    /// </summary>
    /// <remarks>
    /// 硬直经 <see cref="StatusEffects"/> 的 <see cref="StatusKind.Hitstun"/> 计时，与木桩同一套口径。
    /// **只处理移动与相位**：受击帧、顿帧、震屏那些表现在引擎层；连段被命中打断归更完整的 `GP-18`，
    /// 现在的靶是被动的、本就不出招，所以那条不触发。
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
        // 受击硬直（`GP-14`）优先于一切：挨打期间不接受输入、不能出招/闪避/冲刺，只吃横向击退。
        // 放在闪避之前 —— 被打到就该从任何相位进入硬直（当前只有被动靶用到；闪避无敌窗内不会中招）。
        if (Phase == MotorPhase.Hurt || Statuses.Has(StatusKind.Hitstun))
        {
            AdvanceHurt(isOnFloor);
            return;
        }
        if (Phase == MotorPhase.Dodge)
        {
            AdvanceDodge(isOnFloor);
            return;
        }

        var dir = input.HorizontalDirection;
        if (dir != 0)
        {
            Facing = dir;
        }

        // 起手闪避：地面、非出招。两个轴的方向此刻一起锁定（`GP-9`）。
        if (input.DodgePressed && isOnFloor && !attacking)
        {
            // 只按纵深时横向为 0（纯纵深闪步）；两个方向都没按才退回面朝方向。
            _dodgeDirection = input.HasDirection ? dir : Facing;
            _dodgeDepthDirection = input.DepthDirection;
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

        // 冲刺：地面、非出招、按住冲刺且有**横向**方向。纵深不冲刺，见类注释。
        var dashing = grounded && !attacking && input.RunHeld && dir != 0;

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

        // 纵深：离地锁定，出招与横向一同定身；其余情况按输入走行走速度。
        IsDepthAirLocked = !grounded;
        AdvanceDepth(grounded && !attacking
            ? input.DepthDirection * (double)CombatFeel.DepthSpeedPixelsPerSecond
            : 0.0);

        Phase = !grounded
            ? MotorPhase.Airborne
            : dashing ? MotorPhase.Run : MotorPhase.Grounded;
    }

    /// <summary>碰撞后校正竖速，避免撞顶后下一帧重新施加向上速度。</summary>
    /// <remarks>**不碰纵深**：纵深轴上没有引擎碰撞体，引擎没有可回报的东西（见类注释）。</remarks>
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
            // 退出相位但保留本 Tick 位移输出，引擎尚未消费第18帧速度。
            // **两个轴在这一帧的含义不同**：横向那份速度还等着引擎消费，而纵深位移已经在上面
            // AdvanceDepth 里发生过了（纵深位置在规则层）。所以闪避总位移 = 18 帧 × 各自速度，
            // 两轴都不多不少一帧 —— 单测按这个数钉住。
            Phase = isOnFloor ? MotorPhase.Grounded : MotorPhase.Airborne;
        }
    }

    /// <summary>受击硬直推进：只吃横向击退，纵深锁定、受重力；硬直结束即停下回到地面/空中相位。</summary>
    private void AdvanceHurt(bool isOnFloor)
    {
        var stunned = Statuses.Has(StatusKind.Hitstun);
        var grounded = isOnFloor && _verticalVelocity >= 0.0;
        HorizontalVelocity = stunned ? _knockbackVelocity : 0.0;
        _verticalVelocity = grounded ? 0.0
            : _verticalVelocity + CombatFeel.GravityPixelsPerSecondSquared * Dt;
        // 击退只沿横向（`GP-16`），纵深不动；离地照锁，与别处同一口径。
        IsDepthAirLocked = !grounded;
        DepthVelocity = 0.0;
        // 硬直还在就停在 Hurt；结束了停下回到地面/空中，不尾滑（同木桩「到期不尾滑」）。
        Phase = stunned ? MotorPhase.Hurt : grounded ? MotorPhase.Grounded : MotorPhase.Airborne;
    }

    /// <summary>纵深积分与钳制。**每个 Tick 恰好调一次**，两条路径（普通与闪避）各自调它。</summary>
    private void AdvanceDepth(double velocity)
    {
        var next = DepthWorldPx + velocity * Dt;
        var clamped = DepthBand.Clamp(next);
        DepthVelocity = clamped == next ? velocity : (clamped - DepthWorldPx) / Dt;
        DepthWorldPx = clamped;
    }
}
