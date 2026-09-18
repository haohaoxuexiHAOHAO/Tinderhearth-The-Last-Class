using Tinderhearth.Rules.Foundation.Actors;

namespace Tinderhearth.Rules.Combat;

/// <summary>
/// 带纵深的实体阻挡（`GP-17`）：谁该挡谁，以及**只有纵深接近时才挡**。
/// </summary>
/// <remarks>
/// 起因是实机发现主角能直接穿过木桩。**但这不是「碰撞层漏填」，那才是本条存在的
/// 真正理由。** 纵深不参与引擎碰撞（`ARCHITECTURE.md` 的「战斗：三个轴」），所以两个角色即使纵深
/// 差满一条带、画面上明显一前一后，它们的 <c>Position</c> 仍可能完全重合 —— 在 Godot 眼里就是重叠
/// 的。于是「顺手给主角补一条碰撞掩码」的后果是**站在不同排的两个角色互相挡住**：现在的毛病是
/// 「该挡的没挡」，那么改完的毛病会是「不该挡的挡了」，而后者玩家读不出原因（他看到的是两个明显
/// 不在一排的东西卡在一起）。
///
/// 所以阻挡是**两个条件**：阵营关系说该挡（<see cref="Blocks"/>），且双方纵深差在阈值内
/// （<see cref="DepthOverlap"/>，与 `GP-16` 的命中判定同一套重叠判定、不写第二份）。横向那一半仍
/// 归引擎 —— 碰撞形状与 <c>MoveAndSlide</c> 已经在做，规则层不重算，重算就是第二份事实。
///
/// **阈值不共用命中容差**，见 <see cref="CombatFeel.BlockDepthThresholdWorldPx"/>。
/// </remarks>
public static class DepthBlocking
{
    /// <summary>
    /// 按阵营关系判该不该挡：**同阵营不挡、敌对双方挡、中立物件挡**。
    /// </summary>
    /// <remarks>
    /// 三条各有理由，不是对称凑出来的：
    ///
    /// **同阵营不挡** —— 否则队友会卡住玩家，敌群会互相卡死、挤不到玩家面前。
    ///
    /// **敌对双方挡** —— belt-scroll 的经典做法：敌群形成需要绕开或打退的「墙」，「被围住」成为
    /// 真实威胁，而**纵深挪步因此才有意义**（因为你需要绕）。不挡的话走位更流畅，但位置压力消失，
    /// 纵深挪步的动机会跟着弱掉。
    ///
    /// **中立物件挡** —— 与正典「场景物件在纵深上作阻挡与掩体，玩家与敌人**绕行而不是跳上去**」
    /// 一致（`canon/gameplay/战斗与关卡.md` 的空间模型）。绕行成立的前提就是物件真的挡人。
    ///
    /// 中立那一条**先判**，所以两个中立物件之间也算挡。它们不会动，判哪边都不影响画面；先判是因为
    /// 「中立的东西挡所有人」比「同类不挡」更贴近这条规则想说的话。
    /// </remarks>
    public static bool Blocks(CombatSide a, CombatSide b) =>
        a == CombatSide.Neutral || b == CombatSide.Neutral || a != b;

    /// <summary>
    /// 完整的阻挡判定：阵营关系说该挡，**且**双方纵深差在阈值内。
    /// </summary>
    /// <param name="a">一方的立场。</param>
    /// <param name="b">另一方的立场。</param>
    /// <param name="aDepthWorldPx">一方的纵深，口径见 <see cref="DepthBand"/>（0 最靠后）。</param>
    /// <param name="bDepthWorldPx">另一方的纵深，同一口径。</param>
    /// <param name="thresholdWorldPx">阻挡阈值，世界像素，非负。由调用方给，本类不持有那个数。</param>
    public static bool BlocksAtDepth(CombatSide a, CombatSide b, double aDepthWorldPx,
        double bDepthWorldPx, double thresholdWorldPx)
    {
        // 先算纵深那一半再取合，口径同 `DepthOverlap.WithHorizontal`：写成 `Blocks(...) && Within(...)`
        // 会让非法阈值在同阵营那一对上被短路掉，于是「阈值配错了」要等到场上真出现敌对双方才暴露 ——
        // 而那时它看起来是玩法问题。
        var depthOverlap = DepthOverlap.Within(aDepthWorldPx, bDepthWorldPx, thresholdWorldPx);
        return Blocks(a, b) && depthOverlap;
    }

    /// <summary>
    /// 把移动方的纵深挡在阻挡方的阈值之外：**只拦「往里挤」，往外挪永远允许**。
    /// </summary>
    /// <remarks>
    /// **为什么需要这一条，而不是让引擎碰撞去挡**：阻挡的另一半（横向）由引擎的 <c>MoveAndSlide</c> 挡，
    /// 但那只覆盖 Godot 的两个轴。**纵深没有碰撞体**（`ARCHITECTURE.md` 的「战斗：三个轴」），所以引擎
    /// 物理上拦不住纵深移动。实机撞到的正是这个洞：左右走过去被挡住，改用 W／S 在纵深
    /// 上走进去却能直接穿过 —— 于是「绕行」形同虚设，因为你可以换一排走到物件跟前、再横着挪进它体内。
    ///
    /// **只拦往里、不拦往外**，否则会把已经在里面的角色永久关住。「已经在里面」在正常操作下到不了
    /// （本条就是拦它的），但摆位、出场与将来的击退都可能造出那个状态，而把玩家关死比让他走出来更糟。
    ///
    /// **越界穿透也被这条挡住**：夹逼取的是「边界」而不是「上一帧的位置加位移」，所以即使某一帧的纵深
    /// 位移大到直接跨过阻挡方（当前不会 —— 纵深每帧最多 1px），也会被停在靠近侧的边界上，不会瞬移到另一侧。
    /// </remarks>
    /// <param name="moverDepthWorldPx">移动方**这一帧算完之后**的纵深。</param>
    /// <param name="blockerDepthWorldPx">阻挡方的纵深。</param>
    /// <param name="previousMoverDepthWorldPx">移动方**上一帧**的纵深，用来判断它从哪一侧靠近。</param>
    /// <param name="thresholdWorldPx">阻挡阈值，世界像素，非负。</param>
    /// <returns>允许停留的纵深。没有约束时原样返回 <paramref name="moverDepthWorldPx"/>。</returns>
    public static double ClampDepthOutOf(double moverDepthWorldPx, double blockerDepthWorldPx,
        double previousMoverDepthWorldPx, double thresholdWorldPx)
    {
        if (double.IsNaN(thresholdWorldPx) || thresholdWorldPx < 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(thresholdWorldPx), thresholdWorldPx,
                "阻挡阈值不能为负或 NaN —— 那会让每一次阻挡都静默失败");
        }

        // 从哪一侧来。上一帧正好同深时无从判断方向，退一步用这一帧的方向；两者都同深就放过 ——
        // 那是「已经完全叠在一起」，交给引擎层那条迟滞处理，不在这里硬推一个方向出来。
        var side = Math.Sign(previousMoverDepthWorldPx - blockerDepthWorldPx);
        if (side == 0)
        {
            side = Math.Sign(moverDepthWorldPx - blockerDepthWorldPx);
        }
        if (side == 0)
        {
            return moverDepthWorldPx;
        }

        // 允许靠到多近：本来在阈值外就是阈值；本来已经在里面，就以「不许更深」为界。
        var previousGap = Math.Abs(previousMoverDepthWorldPx - blockerDepthWorldPx);
        var limit = Math.Min(thresholdWorldPx, previousGap);
        var boundary = blockerDepthWorldPx + (side * limit);
        return side > 0 ? Math.Max(moverDepthWorldPx, boundary) : Math.Min(moverDepthWorldPx, boundary);
    }
}
