using Tinderhearth.Rules.Combat;
using Xunit;

namespace Tinderhearth.Rules.Tests.Combat;

/// <summary>纵深绘制规则，重点是「纵深是排序主键」这一条。</summary>
/// <remarks>
/// 排错了，玩家看到的前后关系会与命中判定用的前后关系相反，而那件事不报错 ——
/// 画面上只是「这一下明明打中了却没反应」，或者反过来。
/// </remarks>
public class DepthRenderingTests
{
    private static DepthSubject At(double depth, double groundY = 140) => new(depth, groundY);

    [Fact]
    public void 纵深靠后的先画_靠前的压在上面()
    {
        var back = At(DepthBand.BackWorldPx);
        var front = At(DepthBand.FrontWorldPx);

        Assert.True(DepthRendering.Compare(back, front) < 0);
        Assert.True(DepthRendering.Compare(front, back) > 0);
        Assert.Equal([0, 1], DepthRendering.DrawOrder([back, front]));
        Assert.Equal([1, 0], DepthRendering.DrawOrder([front, back]));
    }

    [Fact]
    public void 两者纵深交换_绘制顺序随之交换()
    {
        var a = At(10);
        var b = At(30);
        var before = DepthRendering.DrawOrder([a, b]);

        // 同一组对象，只把纵深对调。
        var after = DepthRendering.DrawOrder([b, a]);

        Assert.Equal([0, 1], before);
        Assert.Equal([1, 0], after);
    }

    [Fact]
    public void 同纵深时按脚底位置排_屏幕上更靠上的先画()
    {
        var higher = At(24, groundY: 120);
        var lower = At(24, groundY: 168);

        Assert.True(DepthRendering.Compare(higher, lower) < 0);
        Assert.Equal([1, 0], DepthRendering.DrawOrder([lower, higher]));
    }

    /// <summary>纵深压过脚底：靠前的角色即使站在更高的地形上，也必须压在靠后的角色上面。</summary>
    /// <remarks>这条与「按屏幕 Y 单键排序」的结果相反，正是不能直接用 <c>y_sort</c> 的原因。</remarks>
    [Fact]
    public void 纵深是主键_脚底不能翻盘()
    {
        var frontOnHighGround = At(40, groundY: 100);
        var backOnLowGround = At(8, groundY: 170);

        Assert.True(DepthRendering.Compare(backOnLowGround, frontOnHighGround) < 0);
        Assert.Equal([1, 0], DepthRendering.DrawOrder([frontOnHighGround, backOnLowGround]));
    }

    [Fact]
    public void 两个键都相等时顺序确定_不逐帧抖动()
    {
        var same = At(24, 140);
        var order = DepthRendering.DrawOrder([same, same, same]);

        Assert.Equal([0, 1, 2], order);
        // 同一份输入连算两次必须给同一个答案。
        Assert.Equal(order, DepthRendering.DrawOrder([same, same, same]));
        Assert.Equal(0, DepthRendering.Compare(same, same));
    }

    [Fact]
    public void 空集合与单个对象不报错()
    {
        Assert.Empty(DepthRendering.DrawOrder([]));
        Assert.Equal([0], DepthRendering.DrawOrder([At(24)]));
    }

    [Fact]
    public void 三个不同纵深排成一条链()
    {
        // 输入顺序刻意打乱，结果必须只由纵深决定。
        var order = DepthRendering.DrawOrder([At(48), At(0), At(24)]);

        Assert.Equal([1, 2, 0], order);
    }

    [Fact]
    public void 绘制偏移以带中线为零_往前为正往后为负()
    {
        Assert.Equal(0.0, DepthRendering.DrawOffsetWorldPx(DepthBand.CenterWorldPx), 8);
        Assert.Equal(DepthBand.CenterWorldPx, DepthRendering.DrawOffsetWorldPx(DepthBand.FrontWorldPx), 8);
        Assert.Equal(-DepthBand.CenterWorldPx, DepthRendering.DrawOffsetWorldPx(DepthBand.BackWorldPx), 8);
        // 偏移的绝对值不超过半条带 —— 否则角色会画到地形带之外。
        Assert.Equal(DepthBand.WidthWorldPx,
            DepthRendering.DrawOffsetWorldPx(DepthBand.FrontWorldPx)
            - DepthRendering.DrawOffsetWorldPx(DepthBand.BackWorldPx), 8);
    }

    [Fact]
    public void 带外的纵深先被钳进带内再算偏移()
    {
        Assert.Equal(DepthBand.CenterWorldPx, DepthRendering.DrawOffsetWorldPx(1000), 8);
        Assert.Equal(-DepthBand.CenterWorldPx, DepthRendering.DrawOffsetWorldPx(-1000), 8);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void 影子贴地用第一张_到参考高度换到最后一张(int steps)
    {
        Assert.Equal(0, DepthRendering.ShadowStepAt(0, steps));
        Assert.Equal(steps - 1, DepthRendering.ShadowStepAt(CombatFeel.ShadowShrinkHeightWorldPx, steps));
    }

    /// <summary>越高只往小的那张换、不回头，两端钳住：张号永远是那套图里真有的一张。</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void 影子换张单调不回头且两端都钳住(int steps)
    {
        var previous = 0;
        for (var height = 0.0; height <= CombatFeel.ShadowShrinkHeightWorldPx * 2; height += 0.25)
        {
            var step = DepthRendering.ShadowStepAt(height, steps);
            Assert.True(step >= previous);
            Assert.InRange(step, 0, steps - 1);
            previous = step;
        }

        // 超过参考高度一直是最后一张；负高度（不该出现）按贴地算。
        Assert.Equal(steps - 1, DepthRendering.ShadowStepAt(1000, steps));
        Assert.Equal(0, DepthRendering.ShadowStepAt(-1000, steps));
    }

    /// <summary>每一张都轮得到，而且各占同样高的一段 —— 作者多画的那一张不会被跳过。</summary>
    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void 参考高度以内等分给每一张(int steps)
    {
        // 参考高度不为正时下面那段一次都不进，这条会空跑通过。
        Assert.True(CombatFeel.ShadowShrinkHeightWorldPx > 0);
        const double sample = 0.25;
        var samplesPerStep = new int[steps];
        for (var height = 0.0; height < CombatFeel.ShadowShrinkHeightWorldPx; height += sample)
        {
            samplesPerStep[DepthRendering.ShadowStepAt(height, steps)]++;
        }

        var expected = CombatFeel.ShadowShrinkHeightWorldPx / sample / steps;
        Assert.All(samplesPerStep, count => Assert.InRange(count, expected - 1, expected + 1));
    }

    [Fact]
    public void 一张都没有的那套当场报错()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => DepthRendering.ShadowStepAt(0, 0));
    }
}
