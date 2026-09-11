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

    /// <summary>四段判定框的（宽, 高, 中心离脚底高）三元组：轻击三段 + 重击单招。</summary>
    /// <remarks>各段的数由 <c>tools/import_role_sheets.py</c> 从精灵表量出、<c>tools/check_assets.py</c>
    /// 逐段比对，这里只拿它们钉**段与段之间的关系**，不测某个数对不对。</remarks>
    private static (int Width, int Height, int CenterY)[] HitboxSpecs() =>
    [
        (CombatFeel.Light1HitboxWidthWorldPx, CombatFeel.Light1HitboxHeightWorldPx, CombatFeel.Light1HitboxCenterYWorldPx),
        (CombatFeel.Light2HitboxWidthWorldPx, CombatFeel.Light2HitboxHeightWorldPx, CombatFeel.Light2HitboxCenterYWorldPx),
        (CombatFeel.Light3HitboxWidthWorldPx, CombatFeel.Light3HitboxHeightWorldPx, CombatFeel.Light3HitboxCenterYWorldPx),
        (CombatFeel.HeavyHitboxWidthWorldPx, CombatFeel.HeavyHitboxHeightWorldPx, CombatFeel.HeavyHitboxCenterYWorldPx),
    ];

    /// <summary>
    /// 判定框**按段**分开之后的关系守卫（`ART-6`，2026-09-11）。**不测各段那几个数对不对** ——
    /// 它们由 <c>tools/import_role_sheets.py</c> 从精灵表量出、由 <c>tools/check_assets.py</c>
    /// 逐段比对，那是守卫的活。这里只钉两条**关系**，它们是本轮改动的**理由**、改坏了不报错：
    /// 重击必须比每一段轻击都伸得远；踢腿（第 3 段）必须比两段直拳都伸得远（作者「踢腿伸展比拳远」，
    /// 正是三段分框的理由）。谁把它们改成相等或倒过来，画面与判定就又对不上了。
    /// </summary>
    [Fact]
    public void 重击比每段轻击都远且踢腿比直拳远_否则画面与判定对不上()
    {
        int[] lightWidths =
        [
            CombatFeel.Light1HitboxWidthWorldPx,
            CombatFeel.Light2HitboxWidthWorldPx,
            CombatFeel.Light3HitboxWidthWorldPx,
        ];
        foreach (var width in lightWidths)
        {
            Assert.True(CombatFeel.HeavyHitboxWidthWorldPx > width);
        }
        Assert.True(CombatFeel.Light3HitboxWidthWorldPx > CombatFeel.Light1HitboxWidthWorldPx);
        Assert.True(CombatFeel.Light3HitboxWidthWorldPx > CombatFeel.Light2HitboxWidthWorldPx);
    }

    [Fact]
    public void 每段判定框尺寸为正且中心落在角色本体高度内()
    {
        foreach (var (width, height, centerY) in HitboxSpecs())
        {
            Assert.True(width > 0);
            Assert.True(height > 0);
            // 本体 ≤32px 高（正典「像素基准」），框心在脚底之上、不许高过头顶。
            Assert.InRange(centerY, 1, 32);
        }
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
    public void 每段判定框上下沿都落在角色本体高度内()
    {
        foreach (var (_, height, centerY) in HitboxSpecs())
        {
            Assert.True(centerY - height / 2.0 >= 0);
            Assert.True(centerY + height / 2.0 <= 32);
        }
    }
}
