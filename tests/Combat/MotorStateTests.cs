using Tinderhearth.Rules.Combat;
using Xunit;

namespace Tinderhearth.Rules.Tests.Combat;

/// <summary>
/// `GP-10` 运动状态机的守卫：跳跃初速与重力、落地归零、移动与面朝、冲刺、闪避无敌窗与方向锁定
/// （`GP-9`）、出招时地面定身且封锁跳闪冲。运动学的失效在实机上只表现为「手感不对」，不报错。
/// </summary>
public class MotorStateTests
{
    private static CombatInput Move(int dir) => new(dir, 0, false, false, false, false, false);

    private static CombatInput Jump() => new(0, 0, true, false, false, false, false);

    private static CombatInput Dodge(int dir) => new(dir, 0, false, false, false, true, false);

    private static CombatInput Dash(int dir) => new(dir, 0, false, false, false, false, true);

    /// <summary>只按纵深，不按横向。+1 向前（靠近镜头）。</summary>
    private static CombatInput Depth(int depthDir) => new(0, depthDir, false, false, false, false, false);

    /// <summary>两个轴同时按。</summary>
    private static CombatInput MoveDepth(int dir, int depthDir) =>
        new(dir, depthDir, false, false, false, false, false);

    /// <summary>带二维方向的闪避起手（`GP-9` 取按下瞬间）。</summary>
    private static CombatInput DodgeInto(int dir, int depthDir) =>
        new(dir, depthDir, false, false, false, true, false);

    /// <summary>一帧走多少纵深；换算走 <see cref="CombatFeel.FrameSeconds"/>，不写死 60。</summary>
    private static readonly double DepthStep =
        CombatFeel.DepthSpeedPixelsPerSecond * CombatFeel.FrameSeconds;

    private static readonly double DodgeDepthStep =
        CombatFeel.DodgeDepthSpeedPixelsPerSecond * CombatFeel.FrameSeconds;

    [Fact]
    public void 跳跃冲量帧给满初速且离地()
    {
        var m = new MotorState();
        m.Tick(Jump(), isOnFloor: true, attacking: false);

        Assert.Equal(-CombatFeel.JumpInitialPixelsPerSecond, m.VerticalVelocity, 3);
        Assert.Equal(MotorPhase.Airborne, m.Phase);
    }

    [Fact]
    public void 起跳后逐帧受重力减速上升()
    {
        var m = new MotorState();
        m.Tick(Jump(), isOnFloor: true, attacking: false);
        var v0 = m.VerticalVelocity;

        m.Tick(CombatInput.None, isOnFloor: false, attacking: false);
        var v1 = m.VerticalVelocity;

        var step = CombatFeel.GravityPixelsPerSecondSquared * CombatFeel.FrameSeconds;
        Assert.Equal(v0 + step, v1, 3);
        Assert.True(v1 > v0);
    }

    [Fact]
    public void 落地后竖速归零且相位回到地面()
    {
        var m = new MotorState();
        m.Tick(Jump(), isOnFloor: true, attacking: false);
        for (var i = 0; i < 200; i++)
        {
            m.Tick(CombatInput.None, isOnFloor: false, attacking: false);
        }

        m.Tick(CombatInput.None, isOnFloor: true, attacking: false);

        Assert.Equal(0.0, m.VerticalVelocity, 3);
        Assert.Equal(MotorPhase.Grounded, m.Phase);
    }

    [Fact]
    public void 地面移动速度取自登记值且移动更新面朝()
    {
        var m = new MotorState();

        m.Tick(Move(1), isOnFloor: true, attacking: false);
        Assert.Equal(CombatFeel.HorizontalAccelerationPixelsPerSecondSquared * CombatFeel.FrameSeconds, m.HorizontalVelocity, 3);
        Assert.Equal(1, m.Facing);

        m.Tick(Move(-1), isOnFloor: true, attacking: false);
        Assert.Equal(-1, m.Facing);
        Assert.True(m.HorizontalVelocity < 0.0);
    }

    [Fact]
    public void 冲刺快于移动且相位为冲刺()
    {
        var m = new MotorState();
        for (var frame = 0; frame < 11; frame++)
            m.Tick(Dash(1), isOnFloor: true, attacking: false);

        Assert.Equal((double)CombatFeel.DashSpeedPixelsPerSecond, m.HorizontalVelocity, 3);
        Assert.Equal(MotorPhase.Dash, m.Phase);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void HorizontalAccelerationAndBrakingAreClampedPerTick(bool floor)
    {
        var m = new MotorState();
        for (var frame = 1; frame <= 8; frame++)
        {
            m.Tick(Move(1), floor, false);
            Assert.Equal(Math.Min(104, frame * 1040.0 / 60), m.HorizontalVelocity, 8);
        }
        for (var frame = 1; frame <= 5; frame++)
        {
            m.Tick(CombatInput.None, floor, false);
            Assert.Equal(Math.Max(0, 104 - frame * 1560.0 / 60), m.HorizontalVelocity, 8);
        }
    }

    [Fact]
    public void DodgeOutputsAllEighteenTicksAndFallsOffLedge()
    {
        var m = new MotorState();
        double distance = 0;
        for (var frame = 0; frame < CombatFeel.DodgeDurationFrames; frame++)
        {
            m.Tick(frame == 0 ? Dodge(1) : Move(-1), frame == 0, false);
            Assert.Equal(168, m.HorizontalVelocity);
            Assert.Equal(frame * 980.0 / 60, m.VerticalVelocity, 8);
            distance += m.HorizontalVelocity * CombatFeel.FrameSeconds;
        }
        Assert.Equal(50.4, distance, 8);
        Assert.Equal(MotorPhase.Airborne, m.Phase);
        m.Tick(CombatInput.None, false, false);
        Assert.Equal(142, m.HorizontalVelocity);
    }

    [Fact]
    public void CollisionFeedbackClearsMomentumWithoutTickingStatuses()
    {
        var m = new MotorState();
        m.Tick(new CombatInput(1, 0, true, false, false, false, false), true, false);
        Assert.True(m.HorizontalVelocity > 0);
        m.Statuses.Apply(StatusKind.Invulnerable, 4);
        m.AfterMove(false, true, true, 0);
        Assert.Equal(0, m.VerticalVelocity);
        Assert.Equal(0, m.HorizontalVelocity);
        Assert.Equal(4, m.Statuses.Get(StatusKind.Invulnerable).RemainingFrames);
        m.Tick(CombatInput.None, false, false);
        Assert.Equal(980.0 / 60, m.VerticalVelocity, 8);
    }

    [Fact]
    public void 闪避无敌窗恰好覆盖登记的帧区间()
    {
        var m = new MotorState();
        m.Tick(Dodge(1), isOnFloor: true, attacking: false);
        Assert.Equal(MotorPhase.Dodge, m.Phase);

        var invuln = new List<bool> { m.IsInvulnerable };
        for (var i = 1; i <= CombatFeel.DodgeDurationFrames; i++)
        {
            m.Tick(CombatInput.None, isOnFloor: true, attacking: false);
            invuln.Add(m.IsInvulnerable);
        }

        for (var frame = 0; frame < CombatFeel.DodgeDurationFrames; frame++)
        {
            var expected = frame >= CombatFeel.DodgeInvulnStartFrame
                && frame < CombatFeel.DodgeInvulnEndFrame;
            Assert.Equal(expected, invuln[frame]);
        }

        Assert.Equal(MotorPhase.Grounded, m.Phase);
    }

    [Fact]
    public void 闪避方向锁定按下瞬间中途改向无效()
    {
        var m = new MotorState();
        m.Tick(Dodge(1), isOnFloor: true, attacking: false);
        Assert.True(m.HorizontalVelocity > 0.0);

        m.Tick(Move(-1), isOnFloor: true, attacking: false);
        Assert.True(m.HorizontalVelocity > 0.0);
    }

    [Fact]
    public void 无方向输入的闪避取面朝方向()
    {
        var m = new MotorState();
        m.Tick(Move(-1), isOnFloor: true, attacking: false);

        m.Tick(Dodge(0), isOnFloor: true, attacking: false);

        Assert.True(m.HorizontalVelocity < 0.0);
    }

    [Fact]
    public void 无敌读统一载体且运动机只推进一次所有状态()
    {
        var m = new MotorState();
        m.Statuses.Apply(StatusKind.Invulnerable, 3);
        m.Statuses.Apply(StatusKind.Hitstun, 2);
        Assert.True(m.IsInvulnerable);
        m.Tick(Dash(1), isOnFloor: true, attacking: false);
        Assert.True(m.IsInvulnerable);
        Assert.Equal(2, m.Statuses.Get(StatusKind.Invulnerable).RemainingFrames);
        Assert.Equal(1, m.Statuses.Get(StatusKind.Hitstun).RemainingFrames);
        m.Tick(CombatInput.None, isOnFloor: true, attacking: true);
        Assert.True(m.IsInvulnerable);
        Assert.False(m.Statuses.Has(StatusKind.Hitstun));
        m.Tick(CombatInput.None, isOnFloor: false, attacking: false);
        Assert.False(m.IsInvulnerable);
    }

    [Fact]
    public void 闪避只在窗口起点注册且退出不主动清除其他来源无敌()
    {
        var m = new MotorState();
        for (var frame = 0; frame < CombatFeel.DodgeDurationFrames; frame++)
        {
            m.Tick(frame == 0 ? Dodge(1) : CombatInput.None, isOnFloor: true, attacking: false);
            var expected = frame >= CombatFeel.DodgeInvulnStartFrame && frame < CombatFeel.DodgeInvulnEndFrame
                ? CombatFeel.DodgeInvulnEndFrame - frame : 0;
            Assert.Equal(expected, m.Statuses.Get(StatusKind.Invulnerable).RemainingFrames);
        }

        m.Tick(Dodge(1), isOnFloor: true, attacking: false);
        for (var frame = 1; frame < CombatFeel.DodgeInvulnEndFrame; frame++)
        {
            m.Tick(CombatInput.None, isOnFloor: true, attacking: false);
        }
        m.Statuses.Apply(StatusKind.Invulnerable, CombatFeel.DodgeDurationFrames);
        for (var frame = CombatFeel.DodgeInvulnEndFrame; frame < CombatFeel.DodgeDurationFrames; frame++)
        {
            m.Tick(CombatInput.None, isOnFloor: true, attacking: false);
        }
        Assert.Equal(MotorPhase.Grounded, m.Phase);
        Assert.True(m.IsInvulnerable);
    }

    [Fact]
    public void 出招时地面定身且封锁跳闪冲()
    {
        var m = new MotorState();
        var everything = new CombatInput(1, 1, true, false, false, true, true);

        m.Tick(everything, isOnFloor: true, attacking: true);

        Assert.Equal(0.0, m.HorizontalVelocity, 3);
        Assert.Equal(0.0, m.VerticalVelocity, 3);
        // 纵深与横向一同定身（`GP-15`）：出招时纵深速度归零、位置一像素都不动。
        Assert.Equal(0.0, m.DepthVelocity, 8);
        Assert.Equal(DepthBand.CenterWorldPx, m.DepthWorldPx, 8);
        Assert.Equal(MotorPhase.Grounded, m.Phase);
        Assert.False(m.IsInvulnerable);
    }

    // ── `GP-15` 纵深轴 ──────────────────────────────────────────────────
    //
    // 下面这些判据对着的失效都是**不报错**的：纵深飘出带外（角色站在没有地面的地方）、
    // 纵深被按成固定轨道（「往里挪半步」变成「换道」）、某个轴的输入串到另一轴（跳一下顺带
    // 往里挪了）、空中还能挪纵深（跳跃与纵深挪步这两种对策失去区别）。

    [Fact]
    public void 纵深起点在带中线且不按方向不动()
    {
        var m = new MotorState();
        Assert.Equal(DepthBand.CenterWorldPx, m.DepthWorldPx, 8);

        for (var i = 0; i < 10; i++)
        {
            m.Tick(Move(1), isOnFloor: true, attacking: false);
        }

        Assert.Equal(DepthBand.CenterWorldPx, m.DepthWorldPx, 8);
        Assert.Equal(0.0, m.DepthVelocity, 8);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(-1)]
    public void 纵深按登记速度逐帧连续推进且不落在固定轨道上(int depthDir)
    {
        var m = new MotorState();

        for (var frame = 1; frame <= 5; frame++)
        {
            m.Tick(Depth(depthDir), isOnFloor: true, attacking: false);
            Assert.Equal(DepthBand.CenterWorldPx + depthDir * frame * DepthStep, m.DepthWorldPx, 8);
            Assert.Equal(depthDir * (double)CombatFeel.DepthSpeedPixelsPerSecond, m.DepthVelocity, 8);
            Assert.True(DepthBand.Contains(m.DepthWorldPx));
        }

        // 连续的证据：停下的地方既不在带沿、也不在 16px 的排位上。轨道化会让它只能取到排位值。
        Assert.NotEqual(0.0, m.DepthWorldPx % DepthBand.RowSpacingWorldPx, 8);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(-1)]
    public void 纵深钳在带内且钳住后不留假速度(int depthDir)
    {
        var m = new MotorState();
        var edge = depthDir > 0 ? DepthBand.FrontWorldPx : DepthBand.BackWorldPx;
        // 走满整条带还多按 20 帧：越界的那一帧必须被钳住，之后一直贴着带沿。
        for (var i = 0; i < DepthBand.WidthWorldPx / DepthStep + 20; i++)
        {
            m.Tick(Depth(depthDir), isOnFloor: true, attacking: false);
            Assert.True(DepthBand.Contains(m.DepthWorldPx));
        }

        Assert.Equal(edge, m.DepthWorldPx, 8);
        Assert.Equal(0.0, m.DepthVelocity, 8);

        // 反向仍走得回来 —— 钳住的是位置不是输入。
        m.Tick(Depth(-depthDir), isOnFloor: true, attacking: false);
        Assert.Equal(edge - depthDir * DepthStep, m.DepthWorldPx, 8);
    }

    [Fact]
    public void 三轴互不串_横向与跳跃都不改纵深_纵深也不改另两轴()
    {
        var m = new MotorState();

        // 只按横向 + 冲刺：纵深一像素不动。
        for (var i = 0; i < 12; i++)
        {
            m.Tick(Dash(1), isOnFloor: true, attacking: false);
        }
        Assert.Equal((double)CombatFeel.DashSpeedPixelsPerSecond, m.HorizontalVelocity, 3);
        Assert.Equal(DepthBand.CenterWorldPx, m.DepthWorldPx, 8);

        // 只按纵深：横向按减速曲线自己停下，竖向保持贴地，纵深照走。
        var depthAt = m.DepthWorldPx;
        for (var i = 0; i < 8; i++)
        {
            m.Tick(Depth(1), isOnFloor: true, attacking: false);
        }
        Assert.Equal(0.0, m.HorizontalVelocity, 8);
        Assert.Equal(0.0, m.VerticalVelocity, 8);
        Assert.Equal(depthAt + 8 * DepthStep, m.DepthWorldPx, 8);
        Assert.Equal(MotorPhase.Grounded, m.Phase);

        // 只按跳跃：纵深不动（起跳那一帧就已经离地）。
        depthAt = m.DepthWorldPx;
        m.Tick(Jump(), isOnFloor: true, attacking: false);
        Assert.Equal(-CombatFeel.JumpInitialPixelsPerSecond, m.VerticalVelocity, 3);
        Assert.Equal(depthAt, m.DepthWorldPx, 8);
        Assert.True(m.IsDepthAirLocked);
    }

    [Fact]
    public void 两轴同按时各走自己的速度_不做对角归一()
    {
        var m = new MotorState();
        var depthAt = m.DepthWorldPx;
        for (var frame = 1; frame <= 8; frame++)
        {
            m.Tick(MoveDepth(1, 1), isOnFloor: true, attacking: false);
            // 横向仍按自己的加速曲线，纵深仍按自己的匀速 —— 一个轴的存在不缩另一个轴。
            Assert.Equal(Math.Min(CombatFeel.MoveSpeedPixelsPerSecond,
                frame * CombatFeel.HorizontalAccelerationPixelsPerSecondSquared * CombatFeel.FrameSeconds),
                m.HorizontalVelocity, 8);
            Assert.Equal(depthAt + frame * DepthStep, m.DepthWorldPx, 8);
        }
    }

    [Fact]
    public void 离地期间纵深锁定_落地即解锁()
    {
        var m = new MotorState();
        // 起跳那一帧起就锁着，全程按住向前也不动。
        m.Tick(new CombatInput(0, 1, true, false, false, false, false), isOnFloor: true, attacking: false);
        var depthAt = m.DepthWorldPx;
        Assert.True(m.IsDepthAirLocked);

        for (var i = 0; i < 30; i++)
        {
            m.Tick(Depth(1), isOnFloor: false, attacking: false);
            Assert.True(m.IsDepthAirLocked);
            Assert.Equal(0.0, m.DepthVelocity, 8);
            Assert.Equal(depthAt, m.DepthWorldPx, 8);
        }

        // 落地：同一份按住的输入立刻又生效，不需要重新按。
        m.Tick(Depth(1), isOnFloor: true, attacking: false);
        Assert.False(m.IsDepthAirLocked);
        Assert.Equal(depthAt + DepthStep, m.DepthWorldPx, 8);
    }

    [Fact]
    public void 纯纵深闪避不带横向位移且不改朝向()
    {
        var m = new MotorState();
        m.Tick(Move(-1), isOnFloor: true, attacking: false);
        Assert.Equal(-1, m.Facing);
        var depthAt = m.DepthWorldPx;

        m.Tick(DodgeInto(0, 1), isOnFloor: true, attacking: false);

        Assert.Equal(MotorPhase.Dodge, m.Phase);
        Assert.Equal(0.0, m.HorizontalVelocity, 8);
        Assert.Equal(-1, m.Facing);
        Assert.Equal(depthAt + DodgeDepthStep, m.DepthWorldPx, 8);
    }

    [Fact]
    public void 闪避的纵深方向锁在按下瞬间_全程给同一份纵深速度()
    {
        var m = new MotorState();
        m.PlaceDepth(DepthBand.BackWorldPx);
        var moved = 0.0;

        for (var frame = 0; frame < CombatFeel.DodgeDurationFrames; frame++)
        {
            // 第 1 帧起把纵深输入反过来按：`GP-9` 口径下它不该有任何影响。
            m.Tick(frame == 0 ? DodgeInto(0, 1) : Depth(-1), isOnFloor: true, attacking: false);
            Assert.Equal((double)CombatFeel.DodgeDepthSpeedPixelsPerSecond, m.DepthVelocity, 8);
            moved += m.DepthVelocity * CombatFeel.FrameSeconds;
        }

        Assert.Equal(DodgeDepthStep * CombatFeel.DodgeDurationFrames, moved, 8);
        Assert.Equal(DepthBand.BackWorldPx + moved, m.DepthWorldPx, 8);
        // 一次纵深闪避走不完整条带 —— 否则落点由钳制决定而不是由输入决定。
        Assert.True(moved < DepthBand.WidthWorldPx);
    }

    [Fact]
    public void 闪避途中掉出平台_纵深当帧起锁住()
    {
        var m = new MotorState();
        m.Tick(DodgeInto(1, 1), isOnFloor: true, attacking: false);
        var depthAt = m.DepthWorldPx;
        Assert.False(m.IsDepthAirLocked);

        for (var i = 1; i < CombatFeel.DodgeDurationFrames; i++)
        {
            m.Tick(CombatInput.None, isOnFloor: false, attacking: false);
            Assert.True(m.IsDepthAirLocked);
            Assert.Equal(depthAt, m.DepthWorldPx, 8);
        }

        // 横向翻滚位移照旧给满，锁的只有纵深这一轴。
        Assert.Equal((double)CombatFeel.DodgeSpeedPixelsPerSecond, m.HorizontalVelocity, 8);
    }

    [Fact]
    public void 摆位只改纵深位置不产生速度且钳进带内()
    {
        var m = new MotorState();
        m.PlaceDepth(DepthBand.FrontWorldPx + 100);
        Assert.Equal(DepthBand.FrontWorldPx, m.DepthWorldPx, 8);
        Assert.Equal(0.0, m.DepthVelocity, 8);

        m.PlaceDepth(DepthBand.BackWorldPx - 100);
        Assert.Equal(DepthBand.BackWorldPx, m.DepthWorldPx, 8);
        Assert.Equal(0.0, m.DepthVelocity, 8);
    }
}
