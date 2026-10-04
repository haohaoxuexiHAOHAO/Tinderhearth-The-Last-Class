using Tinderhearth.Rules.Foundation.Actors;

namespace Tinderhearth.Rules.Combat;

/// <summary>带纵深的实体阻挡：谁该挡谁，以及只有纵深接近时才挡。</summary>
/// <remarks>
/// 纵深不参与引擎碰撞（见 <c>ARCHITECTURE.md</c> 的「战斗：三个轴，但位置的所有权在纵深轴上是
/// 反的」一节），所以两个角色即使纵深差满一条带、画面上明显一前一后，它们的 <c>Position</c> 仍
/// 可能完全重合 —— 在 Godot 眼里就是重叠的。
///
/// 于是「给角色补一条碰撞掩码」解决不了问题，只会把毛病从「该挡的没挡」换成「不该挡的挡了」，
/// 而后者玩家读不出原因：他看到的是两个明显不在一排的东西卡在一起。阻挡因此是两个条件：阵营关系
/// 说该挡（<see cref="Blocks"/>），且双方纵深差在阈值内（<see cref="DepthOverlap"/>）。
///
/// 纵深那一半与命中判定用同一套重叠判定，不写第二份。横向那一半仍归引擎 —— 碰撞形状与
/// <c>MoveAndSlide</c> 已经在做，规则层重算就是第二份事实。阈值不共用命中容差，见
/// <see cref="CombatFeel.BlockDepthThresholdWorldPx"/>。
/// </remarks>
public static class DepthBlocking
{
    /// <summary>按阵营关系判该不该挡：同阵营不挡、敌对双方挡、中立物件挡。</summary>
    /// <remarks>
    /// 三条各有理由，不是对称凑出来的。同阵营不挡，否则队友会卡住玩家，敌群会互相卡死、挤不到
    /// 玩家面前。敌对双方挡，敌群因此形成需要绕开或打退的墙，「被围住」成为真实威胁，而纵深挪步
    /// 也因此才有意义 —— 因为你需要绕。
    ///
    /// 中立物件挡，是因为设计仓 canon/gameplay/战斗与关卡.md 的「战斗关卡的空间模型：带纵深的
    /// 横版」一节要求玩家与敌人绕行而不是跳上去，而绕行成立的前提就是物件真的挡人。
    ///
    /// 中立那一条先判，所以两个中立物件之间也算挡。它们不会动，判哪边都不影响画面；先判是因为
    /// 「中立的东西挡所有人」比「同类不挡」更贴近这条规则想说的话。
    /// </remarks>
    public static bool Blocks(CombatSide a, CombatSide b) =>
        a == CombatSide.Neutral || b == CombatSide.Neutral || a != b;

    /// <summary>完整的阻挡判定：阵营关系说该挡，而且双方纵深差在阈值内。</summary>
    /// <param name="a">一方的立场。</param>
    /// <param name="b">另一方的立场。</param>
    /// <param name="aDepthWorldPx">一方的纵深，口径见 <see cref="DepthBand"/>，0 最靠后。</param>
    /// <param name="bDepthWorldPx">另一方的纵深，同一口径。</param>
    /// <param name="thresholdWorldPx">阻挡阈值，世界像素，非负。由调用方给，本类不持有那个数。</param>
    public static bool BlocksAtDepth(CombatSide a, CombatSide b, double aDepthWorldPx,
        double bDepthWorldPx, double thresholdWorldPx)
    {
        // 先算纵深那一半再取合，口径同 DepthOverlap.WithHorizontal：写成 Blocks(...) && Within(...)
        // 会让非法阈值在同阵营那一对上被短路掉，于是「阈值配错了」要等到场上真出现敌对双方才
        // 暴露，而那时它看起来像是玩法问题。
        var depthOverlap = DepthOverlap.Within(aDepthWorldPx, bDepthWorldPx, thresholdWorldPx);
        return Blocks(a, b) && depthOverlap;
    }

    /// <summary>把移动方的纵深挡在阻挡方的阈值之外：只拦往里挤，往外挪永远允许。</summary>
    /// <remarks>
    /// 横向那一半由引擎的 <c>MoveAndSlide</c> 挡，但那只覆盖 Godot 的两个轴；纵深上没有碰撞体，
    /// 引擎物理上拦不住纵深移动。实机撞到的正是这个洞：左右走过去被挡住，改用前后走却能直接穿
    /// 过去 —— 于是绕行形同虚设，换一排走到物件跟前再横着挪进它体内就行了。
    ///
    /// 只拦往里、不拦往外，否则会把已经在里面的角色永久关住。「已经在里面」正常操作下到不了（本
    /// 方法就是拦它的），但摆位、出场与将来的击退都可能造出那个状态，而把玩家关死比让他走出来更糟。
    ///
    /// 越界穿透也被这条挡住：夹逼取的是边界而不是「上一帧的位置加位移」，所以即使某一帧的纵深位移
    /// 大到直接跨过阻挡方，也会停在靠近侧的边界上，不会瞬移到另一侧。
    /// </remarks>
    /// <param name="moverDepthWorldPx">移动方这一帧算完之后的纵深。</param>
    /// <param name="blockerDepthWorldPx">阻挡方的纵深。</param>
    /// <param name="previousMoverDepthWorldPx">移动方上一帧的纵深，用来判断它从哪一侧靠近。</param>
    /// <param name="thresholdWorldPx">阻挡阈值，世界像素，非负。</param>
    /// <returns>允许停留的纵深。没有约束时原样返回 <paramref name="moverDepthWorldPx"/>。</returns>
    public static double ClampDepthOutOf(double moverDepthWorldPx, double blockerDepthWorldPx,
        double previousMoverDepthWorldPx, double thresholdWorldPx)
    {
        if (double.IsNaN(thresholdWorldPx) || thresholdWorldPx < 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(thresholdWorldPx), thresholdWorldPx,
                "阻挡阈值不能为负、也不能是 NaN —— 那会让每一次阻挡都静默失败");
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
