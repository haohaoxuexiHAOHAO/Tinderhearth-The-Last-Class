namespace Tinderhearth.Rules.Combat;

/// <summary>
/// 带纵深的重叠判定（`GP-16`）：**横向与纵深两个条件都成立**才算重叠。
/// </summary>
/// <remarks>
/// 正典把这条定为空间模型的直接推论、**不是可调项**：「攻击必须落在横向范围内，且双方的纵深差在
/// 容差内，两个条件都成立才算打中」（设计仓 `canon/gameplay/战斗与关卡.md` 的「打击反馈」）。可调
/// 的只有容差那个数，归 `GP-6` 实测收敛。
///
/// **为什么横向那一半是个 <c>bool</c> 参数而不是在这里算**：横向重叠由引擎的形状查询答
/// （`src/World/Hitbox.cs` 的 <c>IntersectShape</c>），把矩形与旋转再在规则层实现一遍就是第二份
/// 事实。而纵深**只能**在这里判 —— Godot 2D 的两个轴已被横向与跳跃高度占满，纵深不参与引擎碰撞
/// （代码仓 `ARCHITECTURE.md` 的「战斗：三个轴」），所以形状查询压根看不见它。
///
/// 那么本类存在的意义是**「两个条件都要」有一个具名落点**：问「这一下算不算重叠」只有
/// <see cref="WithHorizontal"/> 一条路，于是「只判了一个轴」写不出来。少了这个落点，漏判纵深的
/// 代码与单平面时代的代码长得一模一样，而它不报错 —— 只表现为隔着一排也能打中。
///
/// **容差由调用方传入，本类不持有任何数。** 命中传命中容差（<see cref="CombatFeel"/>），`GP-17`
/// 的实体阻挡传它自己的阈值：「打得着」的宽容量与「占同一格」的物理量很可能两边都不合适同一个数，
/// 所以两者都归 `GP-6` 校准但**不共用常量**（见设计仓 `issue-GP-17`）。
/// </remarks>
public static class DepthOverlap
{
    /// <summary>两个纵深之间的距离，世界像素。恒非负，与谁在前无关。</summary>
    public static double SeparationWorldPx(double aDepthWorldPx, double bDepthWorldPx) =>
        Math.Abs(aDepthWorldPx - bDepthWorldPx);

    /// <summary>
    /// 纵深那一半：双方纵深差在容差内。**边界含在内**（差正好等于容差仍算重叠）。
    /// </summary>
    /// <remarks>
    /// 边界取「含」而不是「不含」：正好贴在容差上时判「打得着」更贴合玩家预期，而且开区间会让
    /// 有效窗口宽度取决于浮点末位 —— 那种差别既调不出来也测不稳。
    ///
    /// **非法容差抛异常，不静默当成 0。** 负容差或 NaN 会让 <c>Math.Abs(...) &lt;= 容差</c> 恒为
    /// 假，于是每一次攻击都打空，而一行报错都没有；玩家读到的是「判定不准」，查起来会先去翻判定框
    /// 和动画。这类「配错了就全体失效」的量必须响着坏。
    /// </remarks>
    /// <param name="aDepthWorldPx">一方的纵深，口径见 <see cref="DepthBand"/>（0 最靠后）。</param>
    /// <param name="bDepthWorldPx">另一方的纵深，同一口径。</param>
    /// <param name="toleranceWorldPx">容差，世界像素，非负。由调用方按用途给（命中／阻挡各一个数）。</param>
    public static bool Within(double aDepthWorldPx, double bDepthWorldPx, double toleranceWorldPx)
    {
        if (double.IsNaN(toleranceWorldPx) || toleranceWorldPx < 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(toleranceWorldPx), toleranceWorldPx,
                "纵深容差不能为负或 NaN —— 那会让每一次判定都静默失败");
        }

        return SeparationWorldPx(aDepthWorldPx, bDepthWorldPx) <= toleranceWorldPx;
    }

    /// <summary>
    /// 两个条件一起判：横向重叠**且**纵深差在容差内。
    /// </summary>
    /// <param name="horizontalOverlap">横向那一半，由引擎的形状查询给出。</param>
    /// <param name="aDepthWorldPx">一方的纵深。</param>
    /// <param name="bDepthWorldPx">另一方的纵深。</param>
    /// <param name="toleranceWorldPx">纵深容差，世界像素，非负。</param>
    public static bool WithHorizontal(bool horizontalOverlap, double aDepthWorldPx,
        double bDepthWorldPx, double toleranceWorldPx)
    {
        // 先算纵深那一半再取合。写成 `horizontalOverlap && Within(...)` 会让非法容差在横向不重叠
        // 时被短路掉，于是「容差配错了」要等到有人真站到面前才暴露 —— 而那时它看起来是玩法问题。
        var depthOverlap = Within(aDepthWorldPx, bDepthWorldPx, toleranceWorldPx);
        return horizontalOverlap && depthOverlap;
    }
}
