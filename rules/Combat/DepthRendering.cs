namespace Tinderhearth.Rules.Combat;

/// <summary>
/// 一段贴地的水平范围，相对脚底锚点、向右为正（世界像素）。
/// </summary>
/// <remarks>
/// 影子要的是**范围**而不只是宽度。只给宽度、把椭圆恒画在脚底锚点上，出拳时就会错开：那一帧
/// 的本体从锚点向右伸出去很远、向左只有小半个身子，而对称的椭圆在拳这一侧不够长、在后腿这一侧
/// 又盖过头。作者 2026-09-09 实机一眼看出来了（「攻击的时候左侧影子没有了」）。
/// </remarks>
/// <param name="Left">左边界，相对脚底锚点。</param>
/// <param name="Right">右边界，相对脚底锚点。</param>
public readonly record struct GroundSpan(double Left, double Right)
{
    /// <summary>宽度；左右反了就当空的，不返回负数。</summary>
    public double Width => Math.Max(0.0, Right - Left);

    /// <summary>中点，相对脚底锚点。</summary>
    public double Center => (Left + Right) / 2.0;

    /// <summary>并集：两段都要被盖住。</summary>
    public GroundSpan Union(GroundSpan other) =>
        new(Math.Min(Left, other.Left), Math.Max(Right, other.Right));

    /// <summary>左右镜像。角色朝左时姿态跟着翻，而边界是在未翻转的图上量的。</summary>
    public GroundSpan Mirrored() => new(-Right, -Left);

    /// <summary>以脚底锚点为中心、宽 <paramref name="widthWorldPx"/> 的对称范围。</summary>
    public static GroundSpan Centered(double widthWorldPx) =>
        new(-widthWorldPx / 2.0, widthWorldPx / 2.0);
}

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
    /// 本体贴地范围 <paramref name="body"/> 对应的影子范围，贴地时（不缩小）。
    /// </summary>
    /// <remarks>
    /// **保中点、按比例收窄**：影子比本体略窄（<see cref="CombatFeel.ShadowWidthPercentOfBody"/>），
    /// 但**跟着本体的中点走**，不强行画在脚底锚点上。出拳那一帧本体的中点偏向拳的一侧，影子就跟着
    /// 偏过去 —— 顶光垂直投影本来就该这样。
    /// </remarks>
    public static GroundSpan ShadowSpanAt(GroundSpan body)
    {
        var half = body.Width / 2.0 * CombatFeel.ShadowWidthPercentOfBody / 100.0;
        return new(body.Center - half, body.Center + half);
    }

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
