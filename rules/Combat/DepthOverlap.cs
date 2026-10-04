namespace Tinderhearth.Rules.Combat;

/// <summary>带纵深的重叠判定：横向与纵深两个条件都成立才算重叠。</summary>
/// <remarks>
/// 「两个条件都要」不是可调项：攻击必须落在横向范围内，且双方的纵深差在容差内，规则见设计仓
/// canon/gameplay/战斗与关卡.md 的「打击反馈」一节。可调的只有容差那个数，还没实机调过。
///
/// 横向那一半是个 <c>bool</c> 参数而不是在这里算：横向重叠由引擎的形状查询答（<c>src/World/Hitbox.cs</c>），
/// 把矩形与旋转在规则层再实现一遍就是第二份事实。纵深反过来只能在这里判 —— Godot 2D 的两个轴已经
/// 被横向与跳跃高度占满，纵深不参与引擎碰撞，所以形状查询压根看不见它。
///
/// 于是本类的意义是给「两个条件都要」一个具名的去处：问「这一下算不算重叠」只有
/// <see cref="WithHorizontal"/> 一条路，「只判了一个轴」就写不出来。少了它，漏判纵深的代码与单
/// 平面时代的代码长得一模一样，而它不报错 —— 只表现为隔着一排也能打中。
/// </remarks>
public static class DepthOverlap
{
    /// <summary>两个纵深之间的距离，世界像素。恒非负，与谁在前无关。</summary>
    public static double SeparationWorldPx(double aDepthWorldPx, double bDepthWorldPx) =>
        Math.Abs(aDepthWorldPx - bDepthWorldPx);

    /// <summary>纵深那一半：双方纵深差在容差内。边界含在内，差正好等于容差仍算重叠。</summary>
    /// <remarks>
    /// 边界取「含」而不是「不含」：正好贴在容差上时判「打得着」更贴合玩家预期，而且开区间会让
    /// 有效窗口的宽度取决于浮点末位 —— 那种差别既调不出来也测不稳。
    ///
    /// 非法容差抛异常，不静默当成 0。负容差或 NaN 会让「差不超过容差」恒为假，于是每一次攻击都
    /// 打空，而一行报错都没有；玩家读到的是「判定不准」，查起来会先去翻判定框和动画。这类配错了
    /// 就全体失效的量必须响着坏。
    /// </remarks>
    /// <param name="aDepthWorldPx">一方的纵深，口径见 <see cref="DepthBand"/>，0 最靠后。</param>
    /// <param name="bDepthWorldPx">另一方的纵深，同一口径。</param>
    /// <param name="toleranceWorldPx">容差，世界像素，非负。由调用方按用途给，命中与阻挡各一个数。</param>
    public static bool Within(double aDepthWorldPx, double bDepthWorldPx, double toleranceWorldPx)
    {
        if (double.IsNaN(toleranceWorldPx) || toleranceWorldPx < 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(toleranceWorldPx), toleranceWorldPx,
                "纵深容差不能为负、也不能是 NaN —— 那会让每一次判定都静默失败");
        }

        return SeparationWorldPx(aDepthWorldPx, bDepthWorldPx) <= toleranceWorldPx;
    }

    /// <summary>两个条件一起判：横向重叠，而且纵深差在容差内。</summary>
    /// <param name="horizontalOverlap">横向那一半，由引擎的形状查询给出。</param>
    /// <param name="aDepthWorldPx">一方的纵深。</param>
    /// <param name="bDepthWorldPx">另一方的纵深。</param>
    /// <param name="toleranceWorldPx">纵深容差，世界像素，非负。</param>
    public static bool WithHorizontal(bool horizontalOverlap, double aDepthWorldPx,
        double bDepthWorldPx, double toleranceWorldPx)
    {
        // 先算纵深那一半再取合。写成 horizontalOverlap && Within(...) 会让非法容差在横向不重叠时
        // 被短路掉，于是「容差配错了」要等到有人真站到面前才暴露，而那时它看起来像是玩法问题。
        var depthOverlap = Within(aDepthWorldPx, bDepthWorldPx, toleranceWorldPx);
        return horizontalOverlap && depthOverlap;
    }
}
