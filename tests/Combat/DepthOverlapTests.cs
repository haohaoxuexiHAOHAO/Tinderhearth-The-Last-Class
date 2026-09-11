using Tinderhearth.Rules.Combat;
using Xunit;

namespace Tinderhearth.Rules.Tests.Combat;

/// <summary>
/// `GP-16` 带纵深的重叠判定守卫。**不测容差那个数手感对不对** —— 它是未校准初值，归 `GP-6`。
/// 这里钉的是改值也不该动的三件事：两个条件必须都成立、边界含在内、非法容差要响着坏。
/// </summary>
public class DepthOverlapTests
{
    /// <summary>本文件里当「够近／不够近」用的容差。与手感初值无关，改初值不该让这些关系失败。</summary>
    private const double Tolerance = 8.0;

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.001)]
    [InlineData(1000.0)]
    public void 同一纵深在任何非负容差下都重叠(double tolerance)
    {
        Assert.Equal(0.0, DepthOverlap.SeparationWorldPx(DepthBand.CenterWorldPx, DepthBand.CenterWorldPx), 8);
        Assert.True(DepthOverlap.Within(DepthBand.CenterWorldPx, DepthBand.CenterWorldPx, tolerance));
    }

    /// <summary>
    /// 边界值：差**正好**等于容差仍算重叠，超出一丝就不算。开区间会让有效窗口宽度取决于浮点末位，
    /// 那种差别既调不出来也测不稳。顺带钉住「与谁在前无关」——两个纵深互换结论必须一样。
    /// </summary>
    [Theory]
    [InlineData(0.0, Tolerance)]
    [InlineData(DepthBand.CenterWorldPx, DepthBand.CenterWorldPx + Tolerance)]
    [InlineData(DepthBand.FrontWorldPx - Tolerance, DepthBand.FrontWorldPx)]
    public void 容差边界含在内且与前后顺序无关(double back, double front)
    {
        var gap = DepthOverlap.SeparationWorldPx(back, front);
        Assert.Equal(Tolerance, gap, 8);
        Assert.True(DepthOverlap.Within(back, front, gap));
        Assert.True(DepthOverlap.Within(front, back, gap));
        Assert.False(DepthOverlap.Within(back, front, gap - 1e-9));
        Assert.False(DepthOverlap.Within(front, back, gap - 1e-9));
    }

    /// <summary>
    /// 正典要的那条口径，四种组合逐个走一遍：**任一条件不成立即不重叠**（`FR-25`）。
    /// 单独测纵深那一半测不出「合起来」这件事 —— 忘了取合的代码在纵深单测下照样全绿。
    /// </summary>
    [Theory]
    [InlineData(true, Tolerance / 2, true)]     // 横向够 + 纵深够 → 重叠
    [InlineData(true, Tolerance * 2, false)]    // 横向够 + 纵深超差 → 不重叠
    [InlineData(false, Tolerance / 2, false)]   // 横向不够 + 纵深够 → 不重叠
    [InlineData(false, Tolerance * 2, false)]   // 两个都不够 → 不重叠
    public void 两个条件都成立才算重叠(bool horizontalOverlap, double depthGap, bool expected)
    {
        Assert.Equal(expected, DepthOverlap.WithHorizontal(horizontalOverlap,
            DepthBand.CenterWorldPx, DepthBand.CenterWorldPx + depthGap, Tolerance));
    }

    /// <summary>
    /// 非法容差必须抛，不许静默当成 0：负数与 NaN 都让「差 ≤ 容差」恒为假，于是每一次攻击都打空
    /// 而一行报错都没有。**横向不重叠时也要抛** —— 短路掉的话「容差配错了」要等到有人真站到面前
    /// 才暴露，而那时它看起来是玩法问题。
    /// </summary>
    [Theory]
    [InlineData(-0.001)]
    [InlineData(-1000.0)]
    [InlineData(double.NaN)]
    public void 非法容差必须抛出而不是让每次判定静默失败(double tolerance)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            DepthOverlap.Within(DepthBand.CenterWorldPx, DepthBand.CenterWorldPx, tolerance));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            DepthOverlap.WithHorizontal(false, DepthBand.CenterWorldPx, DepthBand.CenterWorldPx, tolerance));
    }

    /// <summary>
    /// 容差**由调用方传入**（`GP-17` 复用的形状）：同一对纵深，宽的容差说算、严的说不算。
    /// 命中容差与实体阻挡阈值因此可以是两个数，本类不持有任何一个。
    /// </summary>
    [Fact]
    public void 同一对纵深在不同容差下给出不同结论()
    {
        var here = DepthBand.CenterWorldPx;
        var there = here + (Tolerance * 3 / 4);
        Assert.True(DepthOverlap.Within(here, there, Tolerance));
        Assert.False(DepthOverlap.Within(here, there, Tolerance / 2));
    }

    /// <summary>
    /// 命中容差的选值理由，写成算式（`GP-16`）：**同一排打得到、隔一排打不到**。
    /// 容差是 `GP-6` 的未校准初值，可以改；改到让这条关系不成立就是改坏了，所以这里钉关系不钉数。
    /// </summary>
    [Fact]
    public void 命中容差让同排打得到而隔一排打不到()
    {
        var here = DepthBand.CenterWorldPx;
        double tolerance = CombatFeel.HitDepthToleranceWorldPx;
        Assert.True(DepthOverlap.Within(here, here, tolerance));
        // 纵深连续、不是轨道，所以「同一排」上两人各偏半排是常态，最坏情况差一排的一半。
        Assert.True(DepthOverlap.Within(here, here + (DepthBand.RowSpacingWorldPx / 2.0), tolerance));
        Assert.True(DepthOverlap.Within(here, here - (DepthBand.RowSpacingWorldPx / 2.0), tolerance));
        // 隔一排，两个方向都打不到。
        Assert.False(DepthOverlap.Within(here, here + DepthBand.RowSpacingWorldPx, tolerance));
        Assert.False(DepthOverlap.Within(here, here - DepthBand.RowSpacingWorldPx, tolerance));
    }
}
