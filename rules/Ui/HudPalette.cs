namespace Tinderhearth.Rules.Ui;

/// <summary>
/// 一个**完全不透明**的颜色。规则层不引用 Godot，所以自己定，翻译在引擎层。
/// </summary>
/// <remarks>
/// **刻意没有 alpha 通道。** [像素绘制原则 §9] 把「透明度只使用完全透明或完全不透明」定为绝对
/// 规则，`tools/check_assets.py` 已经在逐像素守素材那一半；界面颜色是另一半 —— 一个 50%
/// 不透明的遮罩画上去，屏幕上出现的就是插值像素，与半透明素材同一后果。类型里没有那个字段，
/// 就没有人能顺手加。
///
/// 于是冷却遮罩这类「压暗」效果只能用**不透明色遮住一部分**（从上往下抹）而不是整块调透明度。
/// 那也是像素风该有的做法：像素图上的层次靠形状与色阶，不靠 alpha。
/// </remarks>
public readonly record struct PixelColor(byte R, byte G, byte B)
{
    /// <summary>从 <c>0xRRGGBB</c> 造一个。</summary>
    public static PixelColor FromHex(uint rgb) =>
        new((byte)(rgb >> 16), (byte)((rgb >> 8) & 0xFF), (byte)(rgb & 0xFF));

    /// <summary>写成 <c>#RRGGBB</c>，给日志与守卫比对用。</summary>
    public override string ToString() => $"#{R:X2}{G:X2}{B:X2}";
}

/// <summary>
/// HUD 的**占位色板**（`UI-8`）。**正式色板归 `DOC-2`，本文件不是它的结论。**
/// </summary>
/// <remarks>
/// 为什么先给一套占位而不是等 `DOC-2`：不给色就没法把 HUD 画出来，而 `DOC-2` 反过来依赖
/// `UI-1` 的界面结构 —— 互相等着等于都不动。所以这里给一套明确标为占位的值，`DOC-2` 定稿时
/// 改这一个文件即可，不必翻界面代码。
///
/// **取值不是新发明的**：暖炭底、面板、边、正文、次要、强调六色直接沿用 [ADR-0008] 那套字体
/// 对比工程（设计仓 `tools/font-preview/main.gd`）里用过的占位配色。理由是连续性 —— 作者
/// 2026-08-30 判定「12px 可读」时看的就是这个背景上的这个字色，换一套等于让那次判断作废。
/// 脚手架 `CameraHarness` 里那几条边界线也是同一批色。
///
/// 四条资源色是本轮新加的，按 [像素绘制原则 §4]「明度先于色相」挑：四条的明度各不相同，
/// 于是**即使色觉有差异也分得出来**；而且每条旁边都有文字标签，颜色不是唯一的区分手段
/// （§4 的「相反语义不能只靠颜色」在这里的形态）。
/// </remarks>
public static class HudPalette
{
    /// <summary>暖炭底。整屏最暗的一档，条底与遮罩都从它派生。</summary>
    public static PixelColor Charcoal => PixelColor.FromHex(0x211A17);

    /// <summary>面板底。九宫格面板与技能位空框的填充。</summary>
    public static PixelColor Panel => PixelColor.FromHex(0x332822);

    /// <summary>面板边。1px 描边。</summary>
    public static PixelColor Edge => PixelColor.FromHex(0x6B523C);

    /// <summary>正文。</summary>
    public static PixelColor Ink => PixelColor.FromHex(0xEADFC8);

    /// <summary>次要信息：按键记号、未解锁的技能位标签。</summary>
    public static PixelColor Dim => PixelColor.FromHex(0x9A8B74);

    /// <summary>强调：当前生效的技能组、目标达成那句话。</summary>
    public static PixelColor Hot => PixelColor.FromHex(0xE09A4E);

    /// <summary>资源条的底槽。比暖炭底更暗一档，好让空槽与背景分得开。</summary>
    public static PixelColor Track => PixelColor.FromHex(0x14100E);

    /// <summary>
    /// 冷却遮罩。**不透明**，从上往下抹掉图标的一部分，抹完即可用。
    /// </summary>
    /// <remarks>
    /// 用遮住而不是压暗，理由见 <see cref="PixelColor"/>：压暗要 alpha，而 alpha 会在屏幕上
    /// 造出插值像素。遮住的读感也更直接 —— 玩家看的是「还剩多少没退下去」这个形状。
    /// </remarks>
    public static PixelColor Cooldown => PixelColor.FromHex(0x1A1512);

    /// <summary>倒地队友的头像框色。明度压到与次要信息同档，但色相偏冷，与「还活着」分得开。</summary>
    public static PixelColor Down => PixelColor.FromHex(0x4A4A52);

    /// <summary>HP。暗红，四条里明度最低（实测 0.34）。</summary>
    public static PixelColor Health => PixelColor.FromHex(0x9E3A32);

    /// <summary>日体力。灰绿，明度第二档（0.47）。它是经营侧的量，不该抢注意力。</summary>
    public static PixelColor DailyVigor => PixelColor.FromHex(0x6E8158);

    /// <summary>MP。青蓝，明度第三档（0.58）。</summary>
    public static PixelColor Mana => PixelColor.FromHex(0x5AA6CE);

    /// <summary>SP。土黄，四条里最亮（0.78）—— 它变化最频繁，最需要余光看得见。</summary>
    public static PixelColor Stamina => PixelColor.FromHex(0xE5C866);

    /// <summary>某条资源用哪个色。</summary>
    public static PixelColor ColorOf(HudGaugeKind kind) => kind switch
    {
        HudGaugeKind.Health => Health,
        HudGaugeKind.Stamina => Stamina,
        HudGaugeKind.Mana => Mana,
        HudGaugeKind.DailyVigor => DailyVigor,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), $"没有这条资源：{kind}"),
    };

    /// <summary>
    /// 四条资源色的相对明度，用来核「不靠色相也分得出来」。
    /// </summary>
    /// <remarks>
    /// 取 ITU-R BT.601 的亮度系数（0.299／0.587／0.114）。为什么要算它：色觉差异最常见的是
    /// 红绿难分，而 HP 是红、日体力是绿 —— 如果它俩明度也一样，那对一部分玩家来说这两条条
    /// 就是同一根。有测试盯着四条明度两两之间的最小差。
    /// </remarks>
    public static double LuminanceOf(PixelColor color) =>
        ((color.R * 0.299) + (color.G * 0.587) + (color.B * 0.114)) / 255.0;
}
