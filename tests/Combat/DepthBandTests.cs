using Tinderhearth.Rules.Combat;
using Xunit;

namespace Tinderhearth.Rules.Tests.Combat;

/// <summary>
/// `GP-15` 纵深带的守卫。**不测 48 这个数对不对** —— 它是正典的几何账，密度成不成立归 `ENG-16`
/// 实机验，那里可能回改它。这里钉的是「改了之后哪些关系必须仍然成立」：钳制真的把带外的值拉
/// 回来、中线在带内、带宽能装下正典要的排数。这些关系错了，角色会站到没有地面的纵深上，而画面
/// 上只是「他站得有点靠里」，不报错。
/// </summary>
public class DepthBandTests
{
    [Fact]
    public void 带宽为正且前后沿与中线自洽()
    {
        Assert.True(DepthBand.WidthWorldPx > 0);
        Assert.True(DepthBand.BackWorldPx < DepthBand.FrontWorldPx);
        Assert.Equal(DepthBand.WidthWorldPx, DepthBand.FrontWorldPx - DepthBand.BackWorldPx, 8);
        Assert.True(DepthBand.Contains(DepthBand.CenterWorldPx));
        Assert.Equal(DepthBand.WidthWorldPx / 2.0, DepthBand.CenterWorldPx, 8);
    }

    /// <summary>
    /// 正典那笔账要求「3 排以上」，且每排间距 16px。整除是承重项：除不尽的话最里侧那一排站不满，
    /// 而「一排装得下约 11 人、需要 3 排以上」这笔账就对不上了（见正典「48px 是怎么算出来的」）。
    /// </summary>
    [Fact]
    public void 带宽装得下正典要求的排数()
    {
        Assert.True(DepthBand.RowSpacingWorldPx > 0);
        Assert.Equal(0, DepthBand.WidthWorldPx % DepthBand.RowSpacingWorldPx);
        Assert.True(DepthBand.WidthWorldPx / DepthBand.RowSpacingWorldPx + 1 >= 3);
    }

    [Theory]
    [InlineData(-1000.0)]
    [InlineData(-0.001)]
    public void 带后沿之外的纵深被钳回后沿(double depth)
    {
        Assert.Equal(DepthBand.BackWorldPx, DepthBand.Clamp(depth), 8);
        Assert.False(DepthBand.Contains(depth));
    }

    [Theory]
    [InlineData(1000.0)]
    [InlineData(48.001)]
    public void 带前沿之外的纵深被钳回前沿(double depth)
    {
        Assert.Equal(DepthBand.FrontWorldPx, DepthBand.Clamp(depth), 8);
        Assert.False(DepthBand.Contains(depth));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.5)]
    [InlineData(23.75)]
    [InlineData(48.0)]
    public void 带内的纵深原样返回且含两端(double depth)
    {
        Assert.Equal(depth, DepthBand.Clamp(depth), 8);
        Assert.True(DepthBand.Contains(depth));
    }
}
