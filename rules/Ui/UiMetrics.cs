namespace Tinderhearth.Rules.Ui;

/// <summary>
/// 界面排版单位（`UI-6`）。**所有界面都从这里取数，不各自发明边距。**
/// </summary>
/// <remarks>
/// 为什么这几个数放在规则层而不是引擎层：它们之间有**可以判死的关系**（栅格整除基础单位、
/// 逻辑分辨率整除栅格、边距是栅格的整数倍）。放这里就能用单元测试盯住那些关系 —— 有人为了
/// 「看着好一点」把 8 改成 6，测试当场失败，而不是等到某个界面对不齐才被发现。
///
/// 这几个数是**排版单位**，不是玩法数值，所以与 `GP-2` 无关，也不该外置成配置：外置意味着
/// mod 能改排版，那会让所有界面在未知边距下重排。
///
/// 依据链：基础单位 16px 与精灵格 32×32 来自[玩法定位 · 像素基准]；字号 12px、行高 16px
/// 来自 [ADR-0008]；逻辑分辨率 640×360 由作者 2026-08-30 定。交付 `DOC-2` 的就是这一组。
/// </remarks>
public static class UiMetrics
{
    /// <summary>基础单位。图块、背包图标与 1 格建筑都是它，人物占两个。</summary>
    public const int BaseUnit = 16;

    /// <summary>逻辑分辨率。`aspect="expand"` 下这是**下限**：宽度会随窗口宽高比撑开。</summary>
    public const int BaseWidth = 640;
    public const int BaseHeight = 360;

    /// <summary>正文字号与行高，取自 ADR-0008 的实测（汉字宽 12、行高 16、ascent 13）。</summary>
    public const int FontSize = 12;
    public const int LineHeight = 16;

    /// <summary>
    /// 排版栅格 8px。取基础单位的一半：16px 太粗，做不出「12px 文字加一圈内边距」这种
    /// 常见形状；4px 太细，等于没有栅格。8 同时整除 640 与 360，所以边缘不会出现半格。
    /// </summary>
    public const int Grid = 8;

    /// <summary>
    /// 面板内边距 4px。加上 1px 边框正好让 12px 文字在 24px 高的面板里上下各留 1px：
    /// 1 + 4 + 12 + 4 + 1 = 22 ≤ 24。这就是占位面板取 24×24 的原因。
    /// </summary>
    public const int PanelPadding = 4;

    /// <summary>同一组内元素间距 4px；跨组用一个栅格（8px）。</summary>
    public const int ItemGap = 4;

    /// <summary>
    /// 屏幕安全边距一个栅格。**不是为了电视过扫描**，而是因为 `expand` 下逻辑宽度会变：
    /// 贴边的元素在宽窗口上会离屏幕边缘忽远忽近，留一格让它看起来是有意的。
    /// </summary>
    public const int SafeMargin = Grid;

    /// <summary>图标两档：界面符号 16px，角色与大件 32px。中间档不设 —— 见 32px 铁律。</summary>
    public const int IconSmall = 16;
    public const int IconLarge = 32;

    /// <summary>侧视关卡的相机缩放，来自玩法正典。有效世界视野因此是逻辑分辨率的一半。</summary>
    public const int SideViewZoom = 2;

    /// <summary>侧视关卡里实际能看到的世界宽度（逻辑像素）。</summary>
    public static int SideViewWorldWidth => BaseWidth / SideViewZoom;

    /// <summary>侧视关卡里实际能看到的世界高度（逻辑像素）。</summary>
    public static int SideViewWorldHeight => BaseHeight / SideViewZoom;

    /// <summary>
    /// 一行最多排多少个全宽汉字。**是下限不是定值** —— `expand` 会让逻辑宽度变大。
    /// </summary>
    public static int MaxFullWidthChars => (BaseWidth - SafeMargin * 2) / FontSize;
}
