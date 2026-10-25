namespace Tinderhearth.Rules.Combat;

/// <summary>参与纵深排序的一个对象：它的纵深，与它脚底所在的地面高度。</summary>
/// <param name="DepthWorldPx">纵深位置，口径见 <see cref="DepthBand"/>，0 最靠后。</param>
/// <param name="GroundYWorldPx">脚底所在那层地面的世界 Y，向下为正。不含跳跃高度 —— 跳起来不改变前后关系。</param>
public readonly record struct DepthSubject(double DepthWorldPx, double GroundYWorldPx);

/// <summary>纵深绘制的规则：谁压谁、往哪偏、影子用哪一张。量在这里，节点在引擎层。</summary>
/// <remarks>
/// 排序算在规则层而不是交给 Godot 的 <c>y_sort_enabled</c>，是因为要的是两个键：纵深为主、同纵深
/// 时脚底为次。<c>y_sort</c> 只按节点的屏幕 Y 排一个键，而屏幕 Y 里混着跳跃高度与纵深偏移，于是
/// 「跳起来」会被排成「往里走」，地形起伏也会把纵深这个主键压过去。
///
/// 排错了，玩家看到的前后关系会与命中判定用的前后关系相反，而那件事不报错。算在这里的另外两个
/// 好处是它能脱引擎单测，且引擎层只剩「把序号写进 <c>z_index</c>」这一步。
/// </remarks>
public static class DepthRendering
{
    /// <summary>谁先画。返回负数表示 <paramref name="a"/> 先画，也就是压在下面。</summary>
    public static int Compare(in DepthSubject a, in DepthSubject b)
    {
        // 主键：纵深靠后（值小）的先画，靠前的压在上面。
        var byDepth = a.DepthWorldPx.CompareTo(b.DepthWorldPx);
        if (byDepth != 0)
        {
            return byDepth;
        }

        // 次键：同纵深时按脚底位置 —— 屏幕上更靠上（Y 小）的先画。
        return a.GroundYWorldPx.CompareTo(b.GroundYWorldPx);
    }

    /// <summary>按绘制顺序返回下标：第 0 项最先画、压在最下面，最后一项压在最上面。</summary>
    /// <remarks>
    /// 两个键都相等时按输入下标兜底，于是结果完全确定。同纵深同高度的两个角色若每帧顺序不定，
    /// 画面上会看到它们互相闪进闪出，而那种抖动很难归因到排序上。
    /// </remarks>
    public static int[] DrawOrder(IReadOnlyList<DepthSubject> subjects)
    {
        var order = new int[subjects.Count];
        for (var i = 0; i < order.Length; i++)
        {
            order[i] = i;
        }

        Array.Sort(order, (x, y) =>
        {
            var byKeys = Compare(subjects[x], subjects[y]);
            return byKeys != 0 ? byKeys : x.CompareTo(y);
        });
        return order;
    }

    /// <summary>纵深带来的绘制偏移，世界像素，向下为正。只改画面，不改碰撞。</summary>
    /// <remarks>
    /// 碰撞地面对应带中线，所以偏移是「纵深减带中线」：往前走画得更低、往后走画得更高，而站在默认
    /// 纵深（带中线）的角色画在它物理位置上、偏移为零。
    ///
    /// 选中线而不是让碰撞地面对应带后沿（那样偏移就是纵深本身、恒为正），是为了让物理位置与绘制
    /// 位置在默认纵深上重合。既有那几条按屏幕位置取像素的核对（闪白与恢复取色）量的是颜色不是
    /// 位置，整体下移半条带会让它们量错地方，并报出与颜色无关的失败。
    ///
    /// 代价是地形也得画成一条跟带一样厚的带子，否则角色在带内前后走时脚会离开那条画出来的地面线。
    /// 训练房的地面因此按带宽画，见 <c>TrainingRoom</c> 的 <c>BuildTerrain</c>。
    /// </remarks>
    public static double DrawOffsetWorldPx(double depthWorldPx) =>
        DepthBand.Clamp(depthWorldPx) - DepthBand.CenterWorldPx;

    /// <summary>
    /// 离地 <paramref name="heightAboveGroundWorldPx"/> 时用那套影子图里的第几张：0 是贴地那张，越往后越小。
    /// </summary>
    /// <param name="heightAboveGroundWorldPx">离地多高，世界像素。</param>
    /// <param name="steps">那套影子图一共几张，至少一张。</param>
    /// <remarks>
    /// 从贴地到 <see cref="CombatFeel.ShadowShrinkHeightWorldPx"/> 等分成 <paramref name="steps"/> 段，落在
    /// 第几段就用第几张；再往上一直是最后一张，负高度（不该出现）按贴地算。只有一张时恒为 0：影子大小
    /// 不随高度变，只靠留在地上指出离地。
    ///
    /// 用换小的一张表示高度，不变淡、也不把图缩放。变淡要用 alpha，而设计仓 production/像素绘制原则.md
    /// 的「9. 硬边、抗锯齿与点绘」一节把「透明度只用完全透明或完全不透明」定成了绝对规则；缩放是非整数
    /// 倍，缩出来的像素不在作者画的格子上。每一张多大由作者画出来。
    /// </remarks>
    public static int ShadowStepAt(double heightAboveGroundWorldPx, int steps)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(steps, 1);
        var band = Math.Floor(heightAboveGroundWorldPx * steps / CombatFeel.ShadowShrinkHeightWorldPx);
        return (int)Math.Clamp(band, 0.0, steps - 1);
    }
}
