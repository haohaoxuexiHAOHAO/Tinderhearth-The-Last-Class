namespace Tinderhearth.Rules.Combat;

/// <summary>参与纵深排序的一个对象：它的纵深，与它脚底所在的地面高度。</summary>
/// <param name="DepthWorldPx">纵深位置，口径见 <see cref="DepthBand"/>（0 最靠后）。</param>
/// <param name="GroundYWorldPx">脚底所在**地面**的世界 Y，向下为正。**不含跳跃高度** —— 跳起来不改变前后关系。</param>
public readonly record struct DepthSubject(double DepthWorldPx, double GroundYWorldPx);

/// <summary>
/// 纵深绘制的规则（`ENG-15`）：谁压谁、往哪偏、影子多大。**量在这里，节点在引擎层。**
/// </summary>
/// <remarks>
/// 为什么排序算在规则层而不是交给 Godot 的 <c>y_sort_enabled</c>：正典要的是**两个键**
/// —— 纵深为主、同纵深时脚底为次。<c>y_sort</c> 只按节点的屏幕 Y 排一个键，而屏幕 Y 里混着
/// 跳跃高度与纵深偏移，于是「跳起来」会被排成「往里走」，地形起伏也会把纵深主键压过去。
/// 正典点名了这条是承重项：排错了，玩家看到的前后关系会与命中判定（`GP-16`）用的前后关系
/// 相反 —— 而那件事不报错。算在这里的好处是它能脱引擎单测，且引擎层只剩「把序号写进
/// <c>z_index</c>」这一步。
/// </remarks>
public static class DepthRendering
{
    /// <summary>
    /// 谁先画。返回负数表示 <paramref name="a"/> 先画（在下面）。
    /// </summary>
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

    /// <summary>
    /// 按绘制顺序返回下标：结果的第 0 项最先画（压在最下面），最后一项压在最上面。
    /// </summary>
    /// <remarks>
    /// 两个键都相等时按输入下标兜底，于是结果**完全确定**。这不是洁癖：同纵深同高度的两个角色
    /// 若每帧顺序不定，画面上会看到它们互相闪进闪出，而那种抖动很难归因到排序上。
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

    /// <summary>
    /// 纵深带来的**绘制**偏移，世界像素，向下为正。只改画面，不改碰撞。
    /// </summary>
    /// <remarks>
    /// **碰撞地面对应带中线**，所以偏移是 <c>纵深 − 带中线</c>：往前走画得更低、往后走画得更高，
    /// 而站在默认纵深（带中线）的角色画在它物理位置上、偏移为零。
    ///
    /// 为什么选中线而不是让碰撞地面对应带后沿（那样偏移就是纵深本身、恒为正）：物理位置与绘制
    /// 位置在默认纵深上重合，既有的按屏幕位置取像素的判据（`GP-13` 的闪白与恢复取色）才不会
    /// 因为「所有角色整体下移了半条带」而全部失准。**这不是为了让旧判据好过** —— 那些判据量的
    /// 是颜色不是位置，位移会让它们量错地方并报出与颜色无关的失败。
    ///
    /// 代价写明：地形也得画成一条 48px 厚的带子，否则角色在带内前后走时脚会离开那条画出来的
    /// 地面线。开发场景里的地面因此按带宽画，见 `PlayerDev`。
    /// </remarks>
    public static double DrawOffsetWorldPx(double depthWorldPx) =>
        DepthBand.Clamp(depthWorldPx) - DepthBand.CenterWorldPx;

    /// <summary>
    /// 本体宽 <paramref name="bodyWidthWorldPx"/> 的角色，贴地时影子多宽（世界像素）。
    /// </summary>
    /// <remarks>
    /// 影子跟着当前姿态的本体宽度走，不是一个固定值 —— 理由与「按动作取值而不是逐帧取」见
    /// <see cref="CombatFeel.ShadowWidthPercentOfBody"/>。负宽度（不该出现）钳成 0，不返回负数
    /// 让下游画出翻转的多边形。
    /// </remarks>
    public static double ShadowWidthAt(double bodyWidthWorldPx) =>
        Math.Max(0.0, bodyWidthWorldPx) * CombatFeel.ShadowWidthPercentOfBody / 100.0;

    /// <summary>
    /// 影子在离地 <paramref name="heightAboveGroundWorldPx"/> 时缩到多少（1.0 是贴地原尺寸）。
    /// </summary>
    /// <remarks>
    /// **用缩小表示高度，不用变淡。** 变淡要 alpha，而[像素绘制原则 §9]把「透明度只使用完全
    /// 透明或完全不透明」定为绝对规则（`rules/Ui/HudPalette.cs` 的 <c>PixelColor</c> 连字段都
    /// 不给）—— 一个半透明影子画上去就是屏幕上的插值像素，与半透明素材同一后果。缩小是像素风
    /// 该有的做法：层次靠形状与色阶。
    ///
    /// 线性插值，钳在两端：贴地是原尺寸，到 <see cref="CombatFeel.ShadowShrinkHeightWorldPx"/>
    /// 及更高都是最小尺寸。**不是曲线** —— 曲线的形状要实机看才定得出来，归 `GP-6`。
    /// </remarks>
    public static double ShadowScaleAt(double heightAboveGroundWorldPx)
    {
        var progress = Math.Clamp(
            heightAboveGroundWorldPx / CombatFeel.ShadowShrinkHeightWorldPx, 0.0, 1.0);
        var smallest = CombatFeel.ShadowMinScalePercent / 100.0;
        return 1.0 - ((1.0 - smallest) * progress);
    }
}
