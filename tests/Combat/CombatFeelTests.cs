using Tinderhearth.Rules.Combat;
using Xunit;

namespace Tinderhearth.Rules.Tests.Combat;

/// <summary>
/// `GP-10` 手感初值的量纲与区间守卫。**不测数值本身好不好** —— 那只能实机收敛，归 `GP-6`。
/// 这里钉的是「取消窗不超过后摇」「无敌窗落在闪避时长内」这类关系：改值不会失败，改坏关系才失败。
/// </summary>
public class CombatFeelTests
{
    [Fact]
    public void 所有帧数与时长都是正数()
    {
        Assert.True(CombatFeel.PhysicsTicksPerSecond > 0);
        Assert.True(CombatFeel.LightStartupFrames > 0);
        Assert.True(CombatFeel.LightActiveFrames > 0);
        Assert.True(CombatFeel.LightRecoveryFrames > 0);
        Assert.True(CombatFeel.HeavyStartupFrames > 0);
        Assert.True(CombatFeel.HeavyActiveFrames > 0);
        Assert.True(CombatFeel.HeavyRecoveryFrames > 0);
        Assert.True(CombatFeel.DodgeDurationFrames > 0);
    }

    [Fact]
    public void 连段至少有一段()
    {
        Assert.True(CombatFeel.LightChainLength >= 1);
        Assert.True(CombatFeel.HeavyChainLength >= 1);
    }

    [Fact]
    public void 取消窗不超过后摇_否则窗口区间为负()
    {
        Assert.InRange(CombatFeel.LightComboWindowFrames, 1, CombatFeel.LightRecoveryFrames);
        Assert.InRange(CombatFeel.HeavyComboWindowFrames, 1, CombatFeel.HeavyRecoveryFrames);
    }

    [Fact]
    public void 无敌窗落在闪避时长内且起早于止()
    {
        Assert.True(CombatFeel.DodgeInvulnStartFrame >= 0);
        Assert.True(CombatFeel.DodgeInvulnStartFrame < CombatFeel.DodgeInvulnEndFrame);
        Assert.True(CombatFeel.DodgeInvulnEndFrame <= CombatFeel.DodgeDurationFrames);
    }

    [Fact]
    public void 跳跃初速与重力为正()
    {
        Assert.True(CombatFeel.JumpInitialPixelsPerSecond > 0);
        Assert.True(CombatFeel.GravityPixelsPerSecondSquared > 0);
    }

    [Fact]
    public void 冲刺快于普通移动_它是位移手段而非常态()
    {
        Assert.True(CombatFeel.DashSpeedPixelsPerSecond > CombatFeel.MoveSpeedPixelsPerSecond);
    }
}
