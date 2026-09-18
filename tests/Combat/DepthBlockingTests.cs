using Tinderhearth.Rules.Combat;
using Tinderhearth.Rules.Foundation.Actors;
using Xunit;

namespace Tinderhearth.Rules.Tests.Combat;

/// <summary>
/// `GP-17` 带纵深的实体阻挡守卫。**不测阈值那个数手感对不对** —— 它是未校准初值，归 `GP-6`。
/// 这里钉的是改值也不该动的四件事：三条阵营规则、两个条件必须都成立、阈值边界，以及
/// 「凡是挡住你的你都打得着」那条不变量。
/// </summary>
public class DepthBlockingTests
{
    /// <summary>本文件里当「够近／不够近」用的阈值。与手感初值无关，改初值不该让这些关系失败。</summary>
    private const double Threshold = 8.0;

    /// <summary>纵深上贴在一起，于是结论只由阵营决定 —— 用来单独测阵营那一半。</summary>
    private const double SameSpot = DepthBand.CenterWorldPx;

    /// <summary>
    /// 同阵营不挡（作者裁定）。否则队友会卡住玩家、敌群会互相卡死挤不到玩家面前。
    /// **两个方向都测** —— 阻挡是对称关系，写成单向的表现是「A 挡 B 而 B 不挡 A」，那在画面上
    /// 是一方能推着另一方走，不报错。
    /// </summary>
    [Theory]
    [InlineData(CombatSide.Ally)]
    [InlineData(CombatSide.Enemy)]
    public void 同阵营互不阻挡(CombatSide side)
    {
        Assert.False(DepthBlocking.Blocks(side, side));
        Assert.False(DepthBlocking.BlocksAtDepth(side, side, SameSpot, SameSpot, Threshold));
    }

    /// <summary>
    /// 敌对双方互相阻挡：敌群要能形成需要绕开或打退的「墙」，
    /// **纵深挪步因此才有意义**。
    /// </summary>
    [Fact]
    public void 敌对双方互相阻挡()
    {
        Assert.True(DepthBlocking.Blocks(CombatSide.Ally, CombatSide.Enemy));
        Assert.True(DepthBlocking.Blocks(CombatSide.Enemy, CombatSide.Ally));
        Assert.True(DepthBlocking.BlocksAtDepth(CombatSide.Ally, CombatSide.Enemy,
            SameSpot, SameSpot, Threshold));
    }

    /// <summary>
    /// 中立物件挡所有人，正典的「物件在纵深上作阻挡与掩体、**绕行而不是跳上去**」靠这条成立。
    /// 中立对中立也挡（先判中立）—— 它们不会动，判哪边都不影响画面。
    /// </summary>
    [Theory]
    [InlineData(CombatSide.Ally)]
    [InlineData(CombatSide.Enemy)]
    [InlineData(CombatSide.Neutral)]
    public void 中立物件阻挡任何一方(CombatSide other)
    {
        Assert.True(DepthBlocking.Blocks(CombatSide.Neutral, other));
        Assert.True(DepthBlocking.Blocks(other, CombatSide.Neutral));
    }

    /// <summary>
    /// 本条的口径：**两个条件都成立才挡**。阵营说该挡但纵深错开时不挡 —— 这正是「不能顺手补碰撞
    /// 层」那条理由的可测形状：画面上明显不在一排的两个实体不许互相卡住。
    /// 单独测阵营那一半测不出「合起来」这件事，忘了取合的代码在阵营单测下照样全绿。
    /// </summary>
    [Theory]
    [InlineData(CombatSide.Ally, CombatSide.Enemy, Threshold / 2, true)]      // 该挡 + 够近 → 挡
    [InlineData(CombatSide.Ally, CombatSide.Enemy, Threshold * 2, false)]     // 该挡 + 隔开 → 不挡
    [InlineData(CombatSide.Neutral, CombatSide.Ally, Threshold / 2, true)]    // 中立 + 够近 → 挡
    [InlineData(CombatSide.Neutral, CombatSide.Ally, Threshold * 2, false)]   // 中立 + 隔开 → 不挡
    [InlineData(CombatSide.Ally, CombatSide.Ally, Threshold / 2, false)]      // 同阵营 + 够近 → 不挡
    [InlineData(CombatSide.Ally, CombatSide.Ally, Threshold * 2, false)]      // 两个都不成立 → 不挡
    public void 阵营与纵深两个条件都成立才阻挡(CombatSide a, CombatSide b, double depthGap, bool expected)
    {
        Assert.Equal(expected, DepthBlocking.BlocksAtDepth(a, b,
            SameSpot, SameSpot + depthGap, Threshold));
        // 纵深差取绝对值，谁在前不影响结论。
        Assert.Equal(expected, DepthBlocking.BlocksAtDepth(a, b,
            SameSpot, SameSpot - depthGap, Threshold));
    }

    /// <summary>
    /// 阈值边界含在内（口径同 <see cref="DepthOverlap"/>）：差**正好**等于阈值仍然挡，超出一丝就不挡。
    /// 开区间会让有效窗口宽度取决于浮点末位，那种差别既调不出来也测不稳。
    /// </summary>
    [Fact]
    public void 阈值边界含在内()
    {
        var here = DepthBand.CenterWorldPx;
        Assert.True(DepthBlocking.BlocksAtDepth(CombatSide.Ally, CombatSide.Enemy,
            here, here + Threshold, Threshold));
        Assert.False(DepthBlocking.BlocksAtDepth(CombatSide.Ally, CombatSide.Enemy,
            here, here + Threshold + 1e-9, Threshold));
    }

    /// <summary>
    /// 非法阈值必须抛，**同阵营那一对也要抛**。写成 <c>Blocks(...) &amp;&amp; Within(...)</c> 会把它
    /// 短路掉，于是「阈值配错了」要等到场上真出现敌对双方才暴露 —— 而那时它看起来是玩法问题。
    /// </summary>
    [Theory]
    [InlineData(-0.001)]
    [InlineData(double.NaN)]
    public void 非法阈值在任何阵营组合下都要抛出(double threshold)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => DepthBlocking.BlocksAtDepth(
            CombatSide.Ally, CombatSide.Enemy, SameSpot, SameSpot, threshold));
        Assert.Throws<ArgumentOutOfRangeException>(() => DepthBlocking.BlocksAtDepth(
            CombatSide.Ally, CombatSide.Ally, SameSpot, SameSpot, threshold));
    }

    /// <summary>
    /// 阻挡阈值的选值理由，写成算式（`GP-17`）：**同一排挡得住、隔一排不挡**。
    /// 阈值是 `GP-6` 的未校准初值，可以改；改到让这条关系不成立就是改坏了，所以这里钉关系不钉数。
    /// </summary>
    [Fact]
    public void 阻挡阈值让同排挡住而隔一排不挡()
    {
        var here = DepthBand.CenterWorldPx;
        double threshold = CombatFeel.BlockDepthThresholdWorldPx;
        Assert.True(DepthBlocking.BlocksAtDepth(CombatSide.Ally, CombatSide.Enemy, here, here, threshold));
        // 纵深连续、不是轨道，所以「同一排」上两人各偏半排是常态，最坏情况差一排的一半。
        Assert.True(DepthBlocking.BlocksAtDepth(CombatSide.Ally, CombatSide.Enemy,
            here, here + (DepthBand.RowSpacingWorldPx / 2.0), threshold));
        // 隔一排，两个方向都不挡 —— 画面上不在一排的实体不许互相卡住。
        Assert.False(DepthBlocking.BlocksAtDepth(CombatSide.Ally, CombatSide.Enemy,
            here, here + DepthBand.RowSpacingWorldPx, threshold));
        Assert.False(DepthBlocking.BlocksAtDepth(CombatSide.Ally, CombatSide.Enemy,
            here, here - DepthBand.RowSpacingWorldPx, threshold));
    }

    /// <summary>
    /// **不变量：阻挡阈值 ≤ 命中容差。** 反过来会出现「被挡住却打不着」—— 玩家贴着一个够不到的
    /// 东西，他读不出原因。
    /// </summary>
    /// <remarks>
    /// 这条是那个决策的执行体。两个数都归 `GP-6` 各自单独调（一个是「打得着」的宽容量、一个是
    /// 「占同一格」的物理量，刻意不共用常量），正因为能各自动，才需要一条判据盯住它们之间必须保持的
    /// 关系 —— 把阻挡阈值调大过命中容差不会有任何报错，只会让玩家在某个距离上推不动也打不着。
    /// </remarks>
    /// <summary>
    /// 纵深方向靠近会被停在阈值边界上（实机发现的洞）：左右走被引擎挡住，而纵深上
    /// 走进去原先没有任何东西拦 —— 引擎碰撞只覆盖 Godot 的两个轴，纵深没有碰撞体。
    /// **两侧各测一遍**：只测一侧的话，符号写反那一半不会被发现。
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(-1)]
    public void 纵深方向靠近被停在阈值边界上(int side)
    {
        var blocker = DepthBand.CenterWorldPx;
        var start = blocker + (side * DepthBand.RowSpacingWorldPx);      // 隔一排，在阈值外
        // 往里挪一格：还在阈值外，放行。
        var stepIn = blocker + (side * (Threshold + 2));
        Assert.Equal(stepIn, DepthBlocking.ClampDepthOutOf(stepIn, blocker, start, Threshold), 8);
        // 继续挤到阈值以内：停在边界上，不许再进。
        var tooDeep = blocker + (side * (Threshold / 2));
        var clamped = DepthBlocking.ClampDepthOutOf(tooDeep, blocker, stepIn, Threshold);
        Assert.Equal(blocker + (side * Threshold), clamped, 8);
    }

    /// <summary>
    /// **一帧跨过对方也被挡住**，不许穿到另一侧。夹逼取的是边界而不是「上一帧位置加位移」，所以位移
    /// 再大也停在靠近侧。当前纵深每帧最多走 1px，撞不到这种情形，但符号写错时它就是穿模。
    /// </summary>
    [Fact]
    public void 一帧跨过对方也停在靠近侧的边界上()
    {
        var blocker = DepthBand.CenterWorldPx;
        var from = blocker + DepthBand.RowSpacingWorldPx;
        var acrossToFarSide = blocker - DepthBand.RowSpacingWorldPx;   // 一步跨到另一侧
        Assert.Equal(blocker + Threshold,
            DepthBlocking.ClampDepthOutOf(acrossToFarSide, blocker, from, Threshold), 8);
    }

    /// <summary>
    /// **已经在里面的不许被关死。** 正常操作到不了这个状态（上面那条就是拦它的），但摆位、出场与将来
    /// 的击退都可能造出来，而把玩家关在原地比让他走出来更糟。往外挪放行，往里挤才拦。
    /// </summary>
    [Fact]
    public void 已经重叠时往外挪放行往里挤才拦()
    {
        var blocker = DepthBand.CenterWorldPx;
        var inside = blocker + 3.0;                 // 已经在阈值（8）以内
        // 往外挪：放行，一点都不拉回。
        var outward = blocker + 5.0;
        Assert.Equal(outward, DepthBlocking.ClampDepthOutOf(outward, blocker, inside, Threshold), 8);
        // 一直往外挪到阈值之外也放行。
        var farOut = blocker + Threshold + 4.0;
        Assert.Equal(farOut, DepthBlocking.ClampDepthOutOf(farOut, blocker, inside, Threshold), 8);
        // 往里挤：停在原处，不许更深。
        var deeper = blocker + 1.0;
        Assert.Equal(inside, DepthBlocking.ClampDepthOutOf(deeper, blocker, inside, Threshold), 8);
    }

    /// <summary>
    /// 完全同深时放过：无从判断该往哪一侧推。这种状态交给引擎层那条迟滞（已重叠就先不挡），
    /// 在这里硬推一个方向出来会变成「凭符号决定把玩家弹向哪边」。
    /// </summary>
    [Fact]
    public void 完全同深时不硬推方向()
    {
        var here = DepthBand.CenterWorldPx;
        Assert.Equal(here, DepthBlocking.ClampDepthOutOf(here, here, here, Threshold), 8);
    }

    /// <summary>非法阈值必须抛，理由同 <see cref="DepthOverlap"/>：配错了要响着坏，不静默放过每一次。</summary>
    [Theory]
    [InlineData(-0.001)]
    [InlineData(double.NaN)]
    public void 纵深夹逼的非法阈值也要抛出(double threshold)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => DepthBlocking.ClampDepthOutOf(
            DepthBand.CenterWorldPx, DepthBand.CenterWorldPx, DepthBand.FrontWorldPx, threshold));
    }

    [Fact]
    public void 凡是挡住你的都打得着()
    {
        Assert.True(CombatFeel.BlockDepthThresholdWorldPx <= CombatFeel.HitDepthToleranceWorldPx);
        // 取阻挡成立的最坏距离（正好贴在阻挡阈值上），命中那一半必须仍然成立。
        var here = DepthBand.CenterWorldPx;
        var farthestBlocking = here + CombatFeel.BlockDepthThresholdWorldPx;
        Assert.True(DepthBlocking.BlocksAtDepth(CombatSide.Ally, CombatSide.Enemy,
            here, farthestBlocking, CombatFeel.BlockDepthThresholdWorldPx));
        Assert.True(DepthOverlap.Within(here, farthestBlocking, CombatFeel.HitDepthToleranceWorldPx));
    }
}
