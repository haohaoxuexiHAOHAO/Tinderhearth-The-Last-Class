namespace Tinderhearth.Rules.Combat;

/// <summary>战斗关卡的可行走纵深带：角色能在前后方向上走多远，世界像素。</summary>
/// <remarks>
/// 它不放在 <see cref="CombatFeel"/> 里，因为那份持有的是只能实机逐帧调的手感量。带宽不是那一类
/// 东西，它是从逻辑分辨率、角色本体尺寸、同深度相邻角色的间距与相邻两排的纵深差一路算出来的，
/// 算法见设计仓 canon/gameplay/战斗与关卡.md 的「可行走纵深是怎么算出来的」一节。
///
/// 那一节现在算出来的带宽与排距跟下面这两个常量对不上，要重新对一遍。而算式本身只说明「放得下、
/// 看得清」，不说明「打起来爽」—— 密度成不成立要满编实机看，结论可能回改这里的值。所以它是一处
/// 常量而不是散落的字面量：回改时只有这里要改，加上这里的说明要重写。
///
/// 坐标口径：0 是最靠后、离镜头最远，<see cref="WidthWorldPx"/> 是最靠前、离镜头最近。屏幕坐标
/// 向下为正，靠前的角色画在下面，于是纵深值与「绘制时要往下偏移多少世界像素」是同一个数、同一个
/// 方向，不用取反。反过来定也能用，但每一处消费点都要写一次减法，而符号写反不报错 —— 画面上的
/// 前后关系与命中判定用的前后关系会静默相反。
/// </remarks>
public static class DepthBand
{
    /// <summary>可行走纵深，世界像素。</summary>
    public const int WidthWorldPx = 48;

    /// <summary>带的最靠后沿，离镜头最远，世界像素。</summary>
    public const double BackWorldPx = 0.0;

    /// <summary>带的最靠前沿，离镜头最近，世界像素。</summary>
    public const double FrontWorldPx = WidthWorldPx;

    /// <summary>带的中线，世界像素。角色没被摆位时的默认纵深，前后各留一半。</summary>
    public const double CenterWorldPx = WidthWorldPx / 2.0;

    /// <summary>相邻两排可读纵深的间距，世界像素。</summary>
    /// <remarks>
    /// 纵深是连续的，这个数不是网格 —— 角色能停在任意纵深上。分道会把「往里挪半步躲开」变成
    /// 「换道」，那是两种手感。留着它是因为「这条带排得下几排」要有个能查的依据。
    ///
    /// 命中的纵深容差与实体阻挡的纵深阈值都取它的一半（见
    /// <see cref="CombatFeel.HitDepthToleranceWorldPx"/>）。那是推导关系，不是等式：那两个是手感量、
    /// 能各自单独改，改到隔一排也能打中就是改坏了，单测钉着那条关系。
    /// </remarks>
    public const int RowSpacingWorldPx = 16;

    /// <summary>把纵深钳进带内。带外的纵深没有意义 —— 那里没有地面。</summary>
    public static double Clamp(double depthWorldPx) =>
        Math.Clamp(depthWorldPx, BackWorldPx, FrontWorldPx);

    /// <summary>这个纵深在带内吗，含两端。</summary>
    public static bool Contains(double depthWorldPx) =>
        depthWorldPx >= BackWorldPx && depthWorldPx <= FrontWorldPx;
}
