using Tinderhearth.Rules.Combat;
using Xunit;

namespace Tinderhearth.Rules.Tests.Combat;

/// <summary>
/// `GP-10` 运动状态机的守卫：跳跃初速与重力、落地归零、移动与面朝、冲刺、闪避无敌窗与方向锁定
/// （`GP-9`）、出招时地面定身且封锁跳闪冲。运动学的失效在实机上只表现为「手感不对」，不报错。
/// </summary>
public class MotorStateTests
{
    private static CombatInput Move(int dir) => new(dir, false, false, false, false, false);

    private static CombatInput Jump() => new(0, true, false, false, false, false);

    private static CombatInput Dodge(int dir) => new(dir, false, false, false, true, false);

    private static CombatInput Dash(int dir) => new(dir, false, false, false, false, true);

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
        Assert.Equal((double)CombatFeel.MoveSpeedPixelsPerSecond, m.HorizontalVelocity, 3);
        Assert.Equal(1, m.Facing);

        m.Tick(Move(-1), isOnFloor: true, attacking: false);
        Assert.Equal(-1, m.Facing);
        Assert.True(m.HorizontalVelocity < 0.0);
    }

    [Fact]
    public void 冲刺快于移动且相位为冲刺()
    {
        var m = new MotorState();
        m.Tick(Dash(1), isOnFloor: true, attacking: false);

        Assert.Equal((double)CombatFeel.DashSpeedPixelsPerSecond, m.HorizontalVelocity, 3);
        Assert.Equal(MotorPhase.Dash, m.Phase);
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
        var everything = new CombatInput(1, true, false, false, true, true);

        m.Tick(everything, isOnFloor: true, attacking: true);

        Assert.Equal(0.0, m.HorizontalVelocity, 3);
        Assert.Equal(0.0, m.VerticalVelocity, 3);
        Assert.Equal(MotorPhase.Grounded, m.Phase);
        Assert.False(m.IsInvulnerable);
    }
}
