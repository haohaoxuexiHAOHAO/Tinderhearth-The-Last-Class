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
/// 色板里的一档：代码仓 <c>assets/self-drawn/palette.gpl</c> 里那一项的名字，加它的色值。
/// </summary>
/// <remarks>
/// 值抄在代码里、不在运行时读那份色板：<c>.gpl</c> 不是引擎资源，导出包里没有它（实测：
/// <c>tools/verify.py</c> 写出的导出清单里查不到）。抄一份就多一个家，所以由测试拿名字去色板里逐项对
/// （<c>tests/UI/PaletteTests.cs</c>）—— 色板改了值而这里没跟，或者名字拼错，那条测试点名报出是哪一档。
///
/// 能直接当 <see cref="PixelColor"/> 用：名字只给测试核对，用色的地方不必管它。
/// </remarks>
/// <param name="Name">色板里那一项的名字，例如 <c>sky 5</c>。</param>
/// <param name="Color">它的色值，必须与色板里那一项一致。</param>
public readonly record struct PaletteEntry(string Name, PixelColor Color)
{
    /// <summary>取色值。</summary>
    public static implicit operator PixelColor(PaletteEntry entry) => entry.Color;
}

/// <summary>
/// 代码里设的颜色，只在这一处。
/// </summary>
/// <remarks>
/// 两种。<see cref="PaletteEntry"/> 那几档按名字取色板里的一项；取哪一档、为什么，记在色板被借那一条的
/// 「借给界面」里。其余仍是 <see cref="PixelColor"/> 占位色，随画的界面件一起删，所以不给它们找色板里的
/// 一档：面板底与边随作者画的框（设计仓台账 <c>UI-44</c>），条底与资源条的染色随画的填充（<c>UI-45</c>、
/// <c>UI-48</c>），倒地头像色随那张倒地记号（<c>UI-46</c>）。
///
/// 正文、次要与强调取的是离当初判 12px 中文可读性那组配色（设计仓 tools/font-preview/main.gd）最近的一档，
/// 那次判断大体还成立；换色之后照样要实机重看一遍（台账 <c>UI-48</c>）。
///
/// 资源条的颜色按明度挑，各条明度都不相同，所以色觉有差异也分得出来；而且每条旁边都有文字标签，
/// 颜色不是唯一的区分手段。依据见设计仓 production/像素绘制原则.md 的「明度先于色相」一节。
/// </remarks>
public static class HudPalette
{
    /// <summary>面板底。九宫格面板与技能位空框的填充。占位色。</summary>
    public static PixelColor Panel => PixelColor.FromHex(0x332822);

    /// <summary>面板边。1px 描边。占位色。</summary>
    public static PixelColor Edge => PixelColor.FromHex(0x6B523C);

    /// <summary>正文。色板 <c>sky 5</c>：深底上的字色。</summary>
    public static PaletteEntry Ink { get; } = new("sky 5", PixelColor.FromHex(0xE3DBCC));

    /// <summary>次要信息：按键记号、未解锁的技能位标签、不可用按钮的字。色板 <c>stone 4</c>。</summary>
    public static PaletteEntry Dim { get; } = new("stone 4", PixelColor.FromHex(0x92897B));

    /// <summary>
    /// 强调：当前生效的技能组、目标达成那句话、随身栏选中格的件数，伤害数字也用它。色板 <c>ember 4</c>。
    /// </summary>
    public static PaletteEntry Hot { get; } = new("ember 4", PixelColor.FromHex(0xD99240));

    /// <summary>浅底上的正文：内容区、左栏的盒子、选中的子页签。色板 <c>rust 1</c>。</summary>
    public static PaletteEntry PaperInk { get; } = new("rust 1", PixelColor.FromHex(0x3D1714));

    /// <summary>带字按钮上的字，按下那一态也够亮。色板 <c>outline 1</c>。</summary>
    public static PaletteEntry ButtonInk { get; } = new("outline 1", PixelColor.FromHex(0x0A0815));

    /// <summary>没选中的子页签上的字（底是 <c>soil 3</c>）。色板 <c>soil 8</c>。</summary>
    public static PaletteEntry SubTabInk { get; } = new("soil 8", PixelColor.FromHex(0xDCC68F));

    /// <summary>资源条的底槽。占位色。</summary>
    public static PixelColor Track => PixelColor.FromHex(0x14100E);

    /// <summary>
    /// 冷却遮罩。色板 <c>outline 2</c>。不透明，从上往下抹掉图标的一部分，抹完即可用。
    /// </summary>
    /// <remarks>
    /// 用遮住而不是压暗，理由见 <see cref="PixelColor"/>：压暗要 alpha，而 alpha 会在屏幕上留下
    /// 插值像素。遮住也更好认 —— 玩家看的是「还剩多少没退下去」那一块。
    /// </remarks>
    public static PaletteEntry Cooldown { get; } = new("outline 2", PixelColor.FromHex(0x110F1C));

    /// <summary>倒地队友的头像框色。明度压到与次要信息同档，但色相偏冷，与「还活着」分得开。占位色。</summary>
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
