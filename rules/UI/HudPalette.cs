namespace Tinderhearth.Rules.UI;

/// <summary>
/// 一个完全不透明的颜色。规则层不引用 Godot，所以自己定一个，翻译成引擎颜色的那一步在引擎层。
/// </summary>
/// <remarks>
/// 刻意没有 alpha 通道。半透明的色块画在像素图上会留下插值出来的中间色，而这个类型里根本没有
/// 那个字段，所以没人能顺手加一个半透明的遮罩 —— 编译期就拦住了，不靠谁去跑检查。
///
/// 于是冷却遮罩这类「压暗」只能用不透明色遮住一部分（从上往下抹），不是整块调透明度。像素图上
/// 的层次本来也该靠形状与色阶，不靠 alpha，见设计仓 production/像素绘制原则.md 的
/// 「硬边、抗锯齿与点绘」一节。
/// </remarks>
public readonly record struct PixelColor(byte R, byte G, byte B)
{
    /// <summary>从 <c>0xRRGGBB</c> 造一个。</summary>
    public static PixelColor FromHex(uint rgb) =>
        new((byte)(rgb >> 16), (byte)((rgb >> 8) & 0xFF), (byte)(rgb & 0xFF));

    /// <summary>写成 <c>#RRGGBB</c>，给日志与测试比对用。</summary>
    public override string ToString() => $"#{R:X2}{G:X2}{B:X2}";
}

/// <summary>
/// HUD 的占位色板。正式色板还没定，这一套是临时的。
/// </summary>
/// <remarks>
/// 先给一套占位，是因为不给颜色就画不出 HUD，而定色板反过来要先看到界面长什么样 —— 互相等着
/// 等于都不动。色板定稿时改这一个文件就行，不必翻界面代码。
///
/// 取值不是现编的：暖炭底、面板、边、正文、次要、强调这一组沿用当初判定 12px 中文可读性时那个
/// 字体对比工程（设计仓 tools/font-preview/main.gd）里的配色。换一套等于让那次判断作废。
///
/// 资源条的颜色按明度挑，各条明度都不相同，所以色觉有差异也分得出来；而且每条旁边都有文字标签，
/// 颜色不是唯一的区分手段。依据见设计仓 production/像素绘制原则.md 的「明度先于色相」一节。
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

    /// <summary>资源条的底槽。比暖炭底再暗一档，好让空槽与背景分得开。</summary>
    public static PixelColor Track => PixelColor.FromHex(0x14100E);

    /// <summary>
    /// 冷却遮罩。不透明，从上往下抹掉图标的一部分，抹完即可用。
    /// </summary>
    /// <remarks>
    /// 用遮住而不是压暗，理由见 <see cref="PixelColor"/>：压暗要 alpha，而 alpha 会在屏幕上留下
    /// 插值像素。遮住也更好认 —— 玩家看的是「还剩多少没退下去」那一块。
    /// </remarks>
    public static PixelColor Cooldown => PixelColor.FromHex(0x1A1512);

    /// <summary>倒地队友的头像框色。明度压到与次要信息同档，但色相偏冷，与「还活着」分得开。</summary>
    public static PixelColor Down => PixelColor.FromHex(0x4A4A52);

    /// <summary>HP。暗红，四条资源里明度最低。</summary>
    public static PixelColor Health => PixelColor.FromHex(0x9E3A32);

    /// <summary>精力。灰绿，明度排第二。它是经营侧的当日预算，不该抢注意力。</summary>
    public static PixelColor DailyVigor => PixelColor.FromHex(0x6E8158);

    /// <summary>MP。青蓝，明度排第三。</summary>
    public static PixelColor Mana => PixelColor.FromHex(0x5AA6CE);

    /// <summary>体力（SP）。土黄，四条资源里最亮 —— 它变化最频繁，最需要余光看得见。</summary>
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
    /// 一个颜色的相对明度，用来核「不靠色相也分得出来」。
    /// </summary>
    /// <remarks>
    /// 取 ITU-R BT.601 的亮度系数（0.299／0.587／0.114）。要算它是因为色觉差异里最常见的是红绿
    /// 难分，而 HP 是红、精力是绿 —— 它俩明度也一样的话，这两条条对一部分玩家来说就是同一根。
    /// 有测试盯着各条资源色明度两两之间的最小差。
    /// </remarks>
    public static double LuminanceOf(PixelColor color) =>
        ((color.R * 0.299) + (color.G * 0.587) + (color.B * 0.114)) / 255.0;
}
