namespace Tinderhearth.Rules.UI;

/// <summary>
/// 界面排版单位，量纲都是界面像素。所有界面从这里取数，不各自发明边距。
/// </summary>
/// <remarks>
/// 放在规则层而不是引擎层，是因为这几个数之间的关系能用单元测试盯住：栅格整除基础单位、逻辑
/// 分辨率整除栅格、边距是栅格的整数倍。有人为了「看着好一点」把栅格从 8 改成 6，测试当场失败，
/// 而不是等到某个界面对不齐才被发现。
///
/// 它们是排版单位、不是玩法数值，所以不外置成配置文件。外置意味着 mod 能改排版，
/// 而那会让所有界面在未知边距下重排。
///
/// 基础单位与精灵格尺寸出自设计仓 canon/gameplay/玩法定位.md 的「像素基准」一节。
/// </remarks>
public static class UIMetrics
{
    /// <summary>基础单位。图块、背包图标与 1 格建筑都是它，人物占两个。</summary>
    public const int BaseUnit = 16;

    /// <summary>
    /// 逻辑分辨率。视口拉伸是 <c>expand</c>，所以这两个数是下限：宽度会随窗口宽高比撑开。
    /// </summary>
    public const int BaseWidth = 640;
    public const int BaseHeight = 360;

    /// <summary>正文字号与行高。实测选定的那款中文像素字体：汉字宽 12、行高 16、基线在 13。</summary>
    public const int FontSize = 12;
    public const int LineHeight = 16;

    /// <summary>
    /// 排版栅格。取基础单位的一半：16 太粗，做不出「12px 文字加一圈内边距」这种常见形状；
    /// 4 太细，等于没有栅格。8 同时整除 640 与 360，所以边缘不会出现半格。
    /// </summary>
    public const int Grid = 8;

    /// <summary>
    /// 面板内边距。加上 1px 边框，12px 文字在 24px 高的面板里上下各剩 1px：
    /// 1 + 4 + 12 + 4 + 1 = 22，不超过 24。占位面板取 24×24 就是这么来的。
    /// </summary>
    public const int PanelPadding = 4;

    /// <summary>同一组内元素的间距；跨组用一个栅格。</summary>
    public const int ItemGap = 4;

    /// <summary>
    /// 屏幕安全边距，一个栅格。不是为了电视过扫描 —— <c>expand</c> 下逻辑宽度不定，元素紧贴
    /// 屏幕边缘时看起来像忘了留边，留一格让它看起来是有意的。
    /// </summary>
    public const int SafeMargin = Grid;

    /// <summary>
    /// 图标两档：小图标 16，大图标 32。
    /// </summary>
    /// <remarks>
    /// 16 是一个基础单位（图块、背包图标），32 是两个（俯视本体与界面图标的大档都是两单位，见设计仓
    /// canon/gameplay/玩法定位.md 的「像素基准」）。中间档不设：中间任何尺寸在世界里都没有对应物。
    /// 格子与详情框那一格落在两档之间，但它们是装一个小图标的框，不是一档图标（见下面的格子）。
    ///
    /// 改这两个数要回去重算设计仓 production/界面绘制约定.md「尺寸阶梯」那张表：那里的尺寸引的是这里，
    /// 这边一改它就静默过期，而能提醒改的人的只有这一段。
    /// </remarks>
    public const int IconSmall = 16;
    public const int IconLarge = 32;

    /// <summary>格子的描边，一个像素。</summary>
    public const int CellOutline = 1;

    /// <summary>格子里描边与图标之间的留白。</summary>
    /// <remarks>
    /// 取 2 而不是 1：选中框画在格子外面，焦点框与稀有度框画在里面那两圈，三样同时在也要都看得出来。
    /// 详情框那一格不可点，用不着给那两圈腾地方，所以它只空一像素（那一格不进本类，尺寸在绘制约定里）。
    /// </remarks>
    public const int CellPadding = 2;

    /// <summary>格内边距：描边加留白。图标就放在格子里 (CellInset, CellInset) 那一点上。</summary>
    public const int CellInset = CellOutline + CellPadding;

    /// <summary>
    /// 格子边长：一个小图标两侧各加一个格内边距。技能栏、随身栏、背包、仓库、宝物栏与两种孔共用这一档。
    /// </summary>
    /// <remarks>
    /// 作者画的格子照这个数画。改它（或改它由来的那几个数）要回去重算设计仓
    /// production/界面绘制约定.md「尺寸阶梯」那张表，画好的格子也得跟着重画。
    /// </remarks>
    public const int Cell = IconSmall + (CellInset * 2);

    /// <summary>迷你档：一个排版栅格。只给界面符号用（爱心、星、强化点、增减、锁、翻页箭头），不进世界。</summary>
    /// <remarks>
    /// 世界里的东西最小也占一个基础单位，所以世界那一层的提示取小图标那一档，不取这一档。改它要回去
    /// 重算设计仓 production/界面绘制约定.md「尺寸阶梯」那张表。
    /// </remarks>
    public const int Mini = Grid;

    /// <summary>侧视关卡的相机缩放倍数。有效世界视野因此是逻辑分辨率的一半。</summary>
    /// <remarks>
    /// 设计仓 canon/gameplay/玩法定位.md 的「像素基准」一节已经改成两种视角的相机都不缩放，
    /// 这个常量和下面两个派生量是那次改动之前留下的，还没清掉。改相机那边之前先对一下这里。
    /// </remarks>
    public const int SideViewZoom = 2;

    /// <summary>侧视关卡里实际能看到的世界宽度，单位是逻辑像素。</summary>
    public static int SideViewWorldWidth => BaseWidth / SideViewZoom;

    /// <summary>侧视关卡里实际能看到的世界高度，单位是逻辑像素。</summary>
    public static int SideViewWorldHeight => BaseHeight / SideViewZoom;

    /// <summary>
    /// 一行最多排多少个全宽汉字。是下限不是定值 —— <c>expand</c> 会让逻辑宽度变大。
    /// </summary>
    public static int MaxFullWidthChars => (BaseWidth - SafeMargin * 2) / FontSize;
}
