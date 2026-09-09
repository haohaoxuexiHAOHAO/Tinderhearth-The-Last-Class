using Tinderhearth.Rules.Combat;
using Xunit;

namespace Tinderhearth.Rules.Tests.Combat;

/// <summary>
/// `ENG-15` 纵深绘制规则的守卫。**这里钉的是正典点名的那条承重项**：纵深是排序主键。排错了，
/// 玩家看到的前后关系会与命中判定（`GP-16`）用的前后关系相反 —— 而那件事不报错，画面上只是
/// 「这一下明明打中了却没反应」或者反过来。
/// </summary>
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

    /// <summary>
    /// **纵深压过脚底**：靠前的角色即使站在更高的地形上，也必须压在靠后的角色上面。这条与
    /// 「按屏幕 Y 单键排序」的做法结果相反，正是不能直接用 <c>y_sort</c> 的原因。
    /// </summary>
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

    [Fact]
    public void 影子贴地是原尺寸_到峰高缩到最小()
    {
        Assert.Equal(1.0, DepthRendering.ShadowScaleAt(0), 8);
        Assert.Equal(CombatFeel.ShadowMinScalePercent / 100.0,
            DepthRendering.ShadowScaleAt(CombatFeel.ShadowShrinkHeightWorldPx), 8);
    }

    [Fact]
    public void 影子缩放单调不回头且两端都钳住()
    {
        var previous = double.MaxValue;
        for (var height = 0; height <= CombatFeel.ShadowShrinkHeightWorldPx * 2; height++)
        {
            var scale = DepthRendering.ShadowScaleAt(height);
            Assert.True(scale <= previous);
            Assert.InRange(scale, CombatFeel.ShadowMinScalePercent / 100.0, 1.0);
            previous = scale;
        }

        // 超过缩到底的高度不再继续缩；负高度（不该出现）也不放大。
        Assert.Equal(CombatFeel.ShadowMinScalePercent / 100.0, DepthRendering.ShadowScaleAt(1000), 8);
        Assert.Equal(1.0, DepthRendering.ShadowScaleAt(-1000), 8);
    }

    /// <summary>影子不许比一排纵深还厚，否则相邻两排的影子会连成一片、读不出排位。</summary>
    [Fact]
    public void 影子尺寸与角色和排间距的关系()
    {
        Assert.True(CombatFeel.ShadowHeightWorldPx > 0);
        Assert.True(CombatFeel.ShadowHeightWorldPx < DepthBand.RowSpacingWorldPx);
        Assert.InRange(CombatFeel.ShadowMinScalePercent, 1, 99);
        Assert.True(CombatFeel.ShadowShrinkHeightWorldPx > 0);
        // 影子比本体略窄：站着时不从脚边露出来，但也不能窄到看不出是这个角色的影子。
        Assert.InRange(CombatFeel.ShadowWidthPercentOfBody, 50, 100);
    }

    [Theory]
    [InlineData(19)]    // 待机
    [InlineData(36)]    // 重击伸展到最远
    public void 影子按本体范围取_比本体略窄且同中点(int bodyWidth)
    {
        var body = GroundSpan.Centered(bodyWidth);
        var shadow = DepthRendering.ShadowSpanAt(body);

        Assert.True(shadow.Width < body.Width);
        Assert.True(shadow.Width > 0);
        Assert.Equal(bodyWidth * CombatFeel.ShadowWidthPercentOfBody / 100.0, shadow.Width, 8);
        Assert.Equal(body.Center, shadow.Center, 8);
    }

    /// <summary>
    /// **本体越宽影子越宽**，这是「攻击与闪避时影子跟着变」那条反馈的可测形状。
    /// </summary>
    [Fact]
    public void 本体变宽影子跟着变宽()
    {
        var wide = DepthRendering.ShadowSpanAt(GroundSpan.Centered(36)).Width;
        var narrow = DepthRendering.ShadowSpanAt(GroundSpan.Centered(19)).Width;

        Assert.True(wide > narrow);
        // 空中缩小与本体宽度是两件独立的事，乘在一起不互相抵消。
        Assert.True(wide * DepthRendering.ShadowScaleAt(32)
            > narrow * DepthRendering.ShadowScaleAt(32));
    }

    /// <summary>
    /// **影子跟着本体的中点偏**，不强行画在脚底锚点上。这是作者 2026-09-09 报的「攻击时左侧影子
    /// 没有了」的可测形状：出拳那一帧本体从锚点向右伸出去，对称的椭圆会在后腿那侧盖过头、在拳
    /// 那侧不够长。
    /// </summary>
    [Fact]
    public void 本体偏向一侧时影子跟着偏过去()
    {
        // 出拳：从锚点左侧 8px 伸到右侧 20px。
        var punching = new GroundSpan(-8, 20);
        var shadow = DepthRendering.ShadowSpanAt(punching);

        Assert.True(shadow.Center > 0);
        Assert.Equal(punching.Center, shadow.Center, 8);
        // 影子仍整体落在本体范围内（比本体窄且同中点，所以两端都不越界）。
        Assert.True(shadow.Left > punching.Left);
        Assert.True(shadow.Right < punching.Right);
        // 对称的画法会把影子摆在 0 附近 —— 那正是被否掉的做法。
        Assert.NotEqual(0.0, shadow.Center, 3);
    }

    [Fact]
    public void 范围的并集与镜像()
    {
        var floor = GroundSpan.Centered(18);
        var punching = new GroundSpan(-8, 20);

        // 并集：实体范围与姿态范围都要被盖住 —— 走动时手臂收回也不让影子缩到实体宽以内。
        var union = punching.Union(floor);
        Assert.Equal(-9, union.Left, 8);
        Assert.Equal(20, union.Right, 8);

        // 镜像：朝左时姿态跟着翻，边界是在未翻转的图上量的。
        var mirrored = punching.Mirrored();
        Assert.Equal(-20, mirrored.Left, 8);
        Assert.Equal(8, mirrored.Right, 8);
        Assert.Equal(punching.Width, mirrored.Width, 8);
        Assert.Equal(-punching.Center, mirrored.Center, 8);
        Assert.Equal(punching, mirrored.Mirrored());
    }

    [Fact]
    public void 左右反了的范围当空的_不返回负宽度()
    {
        var reversed = new GroundSpan(10, -10);

        Assert.Equal(0.0, reversed.Width, 8);
        Assert.Equal(0.0, DepthRendering.ShadowSpanAt(reversed).Width, 8);
        Assert.Equal(0.0, DepthRendering.ShadowSpanAt(GroundSpan.Centered(0)).Width, 8);
    }
}
