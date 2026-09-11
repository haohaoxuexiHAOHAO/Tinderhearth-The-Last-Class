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

    [Fact]
    public void 纵深两个速度为正且闪避比行走快()
    {
        Assert.True(CombatFeel.DepthSpeedPixelsPerSecond > 0);
        Assert.True(CombatFeel.DodgeDepthSpeedPixelsPerSecond > CombatFeel.DepthSpeedPixelsPerSecond);
    }

    /// <summary>
    /// 一次纵深闪避不许走完整条带（`GP-15`）。**这条关系就是纵深闪避另设一个速度的理由**：走满
    /// 整条带的话每次纵深闪避都撞在带沿上，落点由钳制决定而不是由输入决定，「往里挪半步」与「翻
    /// 到最里侧」变成同一个结果。今天的数：横向闪避 18 帧走 50.4px，已经超过 48px 的带宽，所以
    /// 照搬横向那个速度正好踩在这条线外面。**不把横向那个比较写成断言** —— 横向速度归 `GP-6` 调，
    /// 调小了不该让这条判据失败。
    /// </summary>
    [Fact]
    public void 一次纵深闪避走不完整条纵深带()
    {
        var distance = CombatFeel.DodgeDepthSpeedPixelsPerSecond
            * CombatFeel.DodgeDurationFrames / (double)CombatFeel.PhysicsTicksPerSecond;
        Assert.InRange(distance, DepthBand.RowSpacingWorldPx, DepthBand.WidthWorldPx - 1);
    }

    /// <summary>
    /// 判定框轻重分开之后的关系守卫（`ART-6`）。**不测 18 与 22 这两个数对不对** —— 它们由
    /// <c>tools/import_role_sheets.py</c> 从精灵表量出、由 <c>tools/check_assets.py</c> 逐条
    /// 比对，那是守卫的活。这里只钉「重击必须比轻击伸得远」这条关系：它是本轮改动的**理由**，
    /// 一旦谁把两个数改成相等或倒过来，画面与判定就又对不上了，而那不报错。
    /// </summary>
    [Fact]
    public void 重击判定框比轻击伸得远_否则画面上打得更远却同框()
    {
        Assert.True(CombatFeel.HeavyHitboxWidthWorldPx > CombatFeel.LightHitboxWidthWorldPx);
    }

    [Fact]
    public void 判定框尺寸为正且中心落在角色本体高度内()
    {
        Assert.True(CombatFeel.LightHitboxWidthWorldPx > 0);
        Assert.True(CombatFeel.LightHitboxHeightWorldPx > 0);
        Assert.True(CombatFeel.HeavyHitboxWidthWorldPx > 0);
        Assert.True(CombatFeel.HeavyHitboxHeightWorldPx > 0);
        // 本体 ≤32px 高（正典「像素基准」），框心在脚底之上、不许高过头顶。
        Assert.InRange(CombatFeel.HitboxCenterYWorldPx, 1, 32);
    }

    /// <summary>
    /// `GP-16` 命中纵深容差的关系守卫。**不测 8 这个数手感对不对** —— 归 `GP-6`。这里钉正典点名的
    /// 两个坏法：容差 ≥ 一排间距的话隔一排也能打中，纵深上就没有「站错排」这回事；容差窗口盖满整条
    /// 带的话带内不存在打不到的位置，纵深挪步失去意义、48px 的排位是白做的。两头都不报错。
    /// </summary>
    [Fact]
    public void 命中纵深容差让隔一排打不到且带内存在打不到的位置()
    {
        Assert.InRange(CombatFeel.HitDepthToleranceWorldPx, 1, DepthBand.RowSpacingWorldPx - 1);
        Assert.True(2 * CombatFeel.HitDepthToleranceWorldPx < DepthBand.WidthWorldPx);
    }

    [Fact]
    public void 判定框上下沿都落在角色本体高度内()
    {
        foreach (var height in new[] { CombatFeel.LightHitboxHeightWorldPx, CombatFeel.HeavyHitboxHeightWorldPx })
        {
            Assert.True(CombatFeel.HitboxCenterYWorldPx - height / 2.0 >= 0);
            Assert.True(CombatFeel.HitboxCenterYWorldPx + height / 2.0 <= 32);
        }
    }
}
