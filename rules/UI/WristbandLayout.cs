namespace Tinderhearth.Rules.UI;

/// <summary>
/// 手环那一套的外壳怎么排：多大、顶层页签怎么压在外框顶边上、子页签多宽。
/// </summary>
/// <remarks>
/// 照作者画的模板（工作区根 <c>art-source/self-material/ui/interface-person1.png</c>）与设计仓
/// production/界面绘制约定.md「个人界面那一套」。外壳尺寸含顶上那排页签；页签最下面一行就是外框顶边那条线，
/// 所以页签那一排与外框叠一行。各页里面怎么排跟各自的编号走，本类只管外壳与页签。
/// </remarks>
public static class WristbandLayout
{
    /// <summary>外壳左右各离屏幕边多远。界面绘制约定定的推荐值，它让地图页那张城区底图放得下。</summary>
    public const int ScreenMarginX = 40;

    /// <summary>外壳上下各离屏幕边多远。</summary>
    public const int ScreenMarginY = 16;

    /// <summary>外壳宽，含顶上那排页签。改它要回去重算界面绘制约定里那个推荐尺寸的理由。</summary>
    public const int ShellWidth = UIMetrics.BaseWidth - (2 * ScreenMarginX);

    /// <summary>外壳高，含顶上那排页签。</summary>
    public const int ShellHeight = UIMetrics.BaseHeight - (2 * ScreenMarginY);

    /// <summary>顶层页签的图标放在页签底里的 (2,2)：底的一圈边框加一像素留白。</summary>
    public const int TabIconInset = 2;

    /// <summary>顶层页签底的边长：小图标两侧各加一个图标内边距。作者画的页签底照这个数。</summary>
    public const int TabSize = UIMetrics.IconSmall + (2 * TabIconInset);

    /// <summary>页签之间空几像素，顶层与子页签一样（照模板量的）。</summary>
    public const int TabGap = 2;

    /// <summary>页签那一排与外框叠几行：页签最下面一行就是外框顶边那条线，选中的那一个看着接进面板。</summary>
    public const int TabOverlap = 1;

    /// <summary>第一个顶层页签离外框左边多远：外框左上那个角（九宫格左边距）之后空一像素，照模板量的。</summary>
    public const int TabRowInsetX = 19;

    /// <summary>子页签里字的左右留白，一个两字标签的子页签因此是 40 宽（界面绘制约定）。</summary>
    public const int SubTabPaddingX = UIMetrics.Grid;

    /// <summary>外框到里面的东西多远：排版那一节「两组之间、面板外框到里面的东西」那一档。</summary>
    public const int FramePadding = 2 * UIMetrics.Grid;
}
