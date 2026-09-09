namespace Tinderhearth.Rules.Combat;

/// <summary>
/// 战斗关卡的可行走纵深带（`GP-15`）。**这是正典的几何账，不是手感初值。**
/// </summary>
/// <remarks>
/// 为什么它不在 <see cref="CombatFeel"/> 里：那份持有的是「只能实机逐帧调」的手感量，全部标着
/// 未校准、归 `GP-6`。48px 不是那类东西 —— 它是从侧视有效视野 320×180、角色本体 20×28–30px、
/// 同深度相邻角色间距 28px、相邻两排纵深差 16px 一路算出来的（设计仓
/// `canon/gameplay/战斗与关卡.md` 的「48px 是怎么算出来的」）。把它混进手感常量里，下一个人
/// 就会以为它可以凭手感改，而它一改，「一排装得下几个、几排装得下 20 个」那笔账要重算。
///
/// **它也不是不可改的。** 正典自己写明那笔账只证明「放得下、看得清」，没证明「打起来爽」；
/// 密度成不成立要满编实机看，验法与判据归 `ENG-16`，结论可能回改这个 48。所以它是**一处**
/// 常量而不是散落的字面量 —— 回改时只有这里要改，加上这里的一句话要重写。
///
/// **坐标口径：0 是最靠后（离镜头最远），<see cref="WidthWorldPx"/> 是最靠前（离镜头最近）。**
/// 选这个方向不是随手定的：屏幕坐标向下为正，靠前的角色画在下面，于是「纵深值」与「绘制时
/// 要往下偏移多少世界像素」是同一个数、同一个方向，1:1、不用取反。绘制排序（`ENG-15`）也因此
/// 直白 —— 纵深大的后画、压在上面。反过来定（0 在最前）能用，但每一处消费点都要写一次减法，
/// 而符号写反是**不报错**的：画面前后关系与命中判定的前后关系会静默相反，正典点名了这条。
/// </remarks>
public static class DepthBand
{
    /// <summary>可行走纵深，世界像素。正典几何账的结论，改它要连 `ENG-16` 那笔账一起改。</summary>
    public const int WidthWorldPx = 48;

    /// <summary>带的最靠后沿（离镜头最远），世界像素。</summary>
    public const double BackWorldPx = 0.0;

    /// <summary>带的最靠前沿（离镜头最近），世界像素。</summary>
    public const double FrontWorldPx = WidthWorldPx;

    /// <summary>带的中线，世界像素。角色没被摆位时的默认纵深，前后各留 24px（1.5 排）。</summary>
    public const double CenterWorldPx = WidthWorldPx / 2.0;

    /// <summary>相邻两排可读纵深的间距，世界像素。正典的可读性依据，此处只作参考量。</summary>
    /// <remarks>
    /// 纵深是**连续**的，这个数不是网格 —— 角色能停在任意纵深上（正典：分道会把「往里挪半步
    /// 躲开」变成「换道」，那是两种手感）。留它是因为「48px 能排下 4 排」这句话要有个能查的
    /// 依据，而不是让读代码的人回去翻正典重算。密度判定归 `ENG-16`。
    /// </remarks>
    public const int RowSpacingWorldPx = 16;

    /// <summary>把纵深钳进带内。带外的纵深没有意义 —— 那里没有地面。</summary>
    public static double Clamp(double depthWorldPx) =>
        Math.Clamp(depthWorldPx, BackWorldPx, FrontWorldPx);

    /// <summary>这个纵深在带内吗（含两端）。</summary>
    public static bool Contains(double depthWorldPx) =>
        depthWorldPx >= BackWorldPx && depthWorldPx <= FrontWorldPx;
}
