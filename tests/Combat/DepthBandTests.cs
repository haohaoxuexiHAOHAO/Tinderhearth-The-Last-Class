using Tinderhearth.Rules.Combat;
using Xunit;

namespace Tinderhearth.Rules.Tests.Combat;

/// <summary>
/// 纵深带：钳制真的把带外的值拉回来、中线落在带内、带宽装得下要求的排数。
/// </summary>
/// <remarks>
/// 带宽那个数对不对这里不测 —— 它是一笔几何账，密度成不成立要实机验，可能回改。
/// 测的是改了带宽之后哪些关系还必须成立。这些关系错了，角色会站到没有地面的纵深上，
/// 而画面上只是「他站得有点靠里」，不报错。
/// </remarks>
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

    /// <summary>带宽必须被排间距整除，而且排数不少于三排。</summary>
    /// <remarks>
    /// 整除这条不能松：除不尽的话最里侧那一排站不满，同屏能摆下多少人的那笔账就对不上了。
    /// 账怎么算的见设计仓 canon/gameplay/战斗与关卡.md 的「可行走纵深是怎么算出来的」一节。
    /// </remarks>
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
