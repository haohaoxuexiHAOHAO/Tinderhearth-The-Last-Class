using Tinderhearth.Rules.Combat;
using Xunit;

namespace Tinderhearth.Rules.Tests.Combat;

/// <summary>
/// 手感那几个常量的量纲与区间。数值本身好不好这里不测，那只能实机调。
/// </summary>
/// <remarks>
/// 测的是常量之间必须成立的关系，比如取消窗不超过后摇、无敌窗落在闪避时长内。
/// 单纯把某个数调大调小不会让这些测试失败，把关系弄反了才会。
/// </remarks>
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
        Assert.True(CombatFeel.RunSpeedPixelsPerSecond > CombatFeel.MoveSpeedPixelsPerSecond);
    }

    [Fact]
    public void 纵深两个速度为正且闪避比行走快()
    {
        Assert.True(CombatFeel.DepthSpeedPixelsPerSecond > 0);
        Assert.True(CombatFeel.DodgeDepthSpeedPixelsPerSecond > CombatFeel.DepthSpeedPixelsPerSecond);
    }

    /// <summary>一次纵深闪避走的距离要比整条纵深带窄。</summary>
    /// <remarks>
    /// 这条关系就是纵深闪避另设一个速度、不照搬横向速度的理由：走满整条带的话每次纵深闪避
    /// 都撞在带沿上，落点由钳制决定而不是由输入决定，「往里挪半步」与「翻到最里侧」成了同一
    /// 个结果。横向那个速度本身不在这里断言，它还要实机调，调小了不该让这条失败。
    /// </remarks>
    [Fact]
    public void 一次纵深闪避走不完整条纵深带()
    {
        var distance = CombatFeel.DodgeDepthSpeedPixelsPerSecond
            * CombatFeel.DodgeDurationFrames / (double)CombatFeel.PhysicsTicksPerSecond;
        Assert.InRange(distance, DepthBand.RowSpacingWorldPx, DepthBand.WidthWorldPx - 1);
    }

    /// <summary>四段判定框的（宽, 高, 中心离脚底高）三元组：轻击三段加重击单招。</summary>
    /// <remarks>各段的数是从精灵表量出来的，这里只拿它们核对段与段之间的关系，不测某个数对不对。</remarks>
    private static (int Width, int Height, int CenterY)[] HitboxSpecs() =>
    [
        (CombatFeel.Light1HitboxWidthWorldPx, CombatFeel.Light1HitboxHeightWorldPx, CombatFeel.Light1HitboxCenterYWorldPx),
        (CombatFeel.Light2HitboxWidthWorldPx, CombatFeel.Light2HitboxHeightWorldPx, CombatFeel.Light2HitboxCenterYWorldPx),
        (CombatFeel.Light3HitboxWidthWorldPx, CombatFeel.Light3HitboxHeightWorldPx, CombatFeel.Light3HitboxCenterYWorldPx),
        (CombatFeel.HeavyHitboxWidthWorldPx, CombatFeel.HeavyHitboxHeightWorldPx, CombatFeel.HeavyHitboxCenterYWorldPx),
    ];

    /// <summary>重击比每一段轻击都伸得远，踢腿（第 3 段）比两段直拳都伸得远。</summary>
    /// <remarks>
    /// 各段那几个数对不对这里不测 —— 它们是从精灵表量出来的，框贴不贴那只拳脚只能看画面。
    /// 测的是这两条关系，它们正是判定框按段分开的理由：踢腿伸展本来就比拳远。
    /// 谁把它们改成相等或者倒过来，画面与判定就又对不上，而那一头不报错。
    /// </remarks>
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
            // 角色本体不超过 32px 高，见设计仓 canon/gameplay/玩法定位.md 的「像素基准」一节。
            // 框心要在脚底之上，也不许高过头顶。
            Assert.InRange(centerY, 1, 32);
        }
    }

    /// <summary>命中纵深容差要小于一排间距，而且它的窗口不许盖满整条纵深带。</summary>
    /// <remarks>
    /// 容差本身手感对不对这里不测，它还要实机调。测的是两头的坏法，两头都不报错：
    /// 容差大到一排间距以上，隔一排也能打中，纵深上就没有「站错排」这回事；
    /// 窗口盖满整条带，带内不存在打不到的位置，纵深挪步与排位都失去意义。
    /// </remarks>
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
