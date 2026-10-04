namespace Tinderhearth.Rules.Combat;

/// <summary>一段贴地的水平范围，相对脚底锚点、向右为正，世界像素。</summary>
/// <remarks>
/// 影子要的是范围而不只是宽度。只给宽度、把椭圆恒画在脚底锚点上，出拳时就会错开：那一帧的本体
/// 从锚点向右伸出去很远、向左只有小半个身子，而对称的椭圆在拳这一侧不够长、在后腿这一侧又盖过
/// 头。实机一眼就看得出来 —— 攻击时左侧影子会消失。
/// </remarks>
/// <param name="Left">左边界，相对脚底锚点。</param>
/// <param name="Right">右边界，相对脚底锚点。</param>
public readonly record struct GroundSpan(double Left, double Right)
{
    /// <summary>宽度，世界像素。左右反了就当空的，不返回负数。</summary>
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
/// <param name="DepthWorldPx">纵深位置，口径见 <see cref="DepthBand"/>，0 最靠后。</param>
/// <param name="GroundYWorldPx">脚底所在那层地面的世界 Y，向下为正。不含跳跃高度 —— 跳起来不改变前后关系。</param>
public readonly record struct DepthSubject(double DepthWorldPx, double GroundYWorldPx);

/// <summary>纵深绘制的规则：谁压谁、往哪偏、影子多大。量在这里，节点在引擎层。</summary>
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
    /// 训练房的地面因此按带宽画，见 <c>TrainingRoom</c> 的 <c>AddGround</c>。
    /// </remarks>
    public static double DrawOffsetWorldPx(double depthWorldPx) =>
        DepthBand.Clamp(depthWorldPx) - DepthBand.CenterWorldPx;

    /// <summary>本体贴地范围 <paramref name="body"/> 对应的影子范围，贴地时、不缩小。</summary>
    /// <remarks>
    /// 保中点、按比例收窄：影子比本体略窄（<see cref="CombatFeel.ShadowWidthPercentOfBody"/>），但
    /// 跟着本体的中点走，不强行画在脚底锚点上。出拳那一帧本体的中点偏向拳的一侧，影子就跟着偏过
    /// 去 —— 顶光垂直投影本来就该这样。
    /// </remarks>
    public static GroundSpan ShadowSpanAt(GroundSpan body)
    {
        var half = body.Width / 2.0 * CombatFeel.ShadowWidthPercentOfBody / 100.0;
        return new(body.Center - half, body.Center + half);
    }

    /// <summary>影子在离地 <paramref name="heightAboveGroundWorldPx"/> 时缩到多少，1.0 是贴地原尺寸。</summary>
    /// <remarks>
    /// 用缩小表示高度，不用变淡。变淡要用 alpha，而设计仓 production/像素绘制原则.md 的
    /// 「9. 硬边、抗锯齿与点绘」一节把「透明度只用完全透明或完全不透明」定成了绝对规则 —— 半透明
    /// 影子画上去就是屏幕上的插值像素。缩小是像素风该有的做法：层次靠轮廓与色阶。
    ///
    /// 线性插值，钳在两端：贴地是原尺寸，到 <see cref="CombatFeel.ShadowShrinkHeightWorldPx"/> 及
    /// 更高都是最小尺寸。不走曲线，因为曲线的弯法要实机看才定得出来。
    /// </remarks>
    public static double ShadowScaleAt(double heightAboveGroundWorldPx)
    {
        var progress = Math.Clamp(
            heightAboveGroundWorldPx / CombatFeel.ShadowShrinkHeightWorldPx, 0.0, 1.0);
        var smallest = CombatFeel.ShadowMinScalePercent / 100.0;
        return 1.0 - ((1.0 - smallest) * progress);
    }
}
