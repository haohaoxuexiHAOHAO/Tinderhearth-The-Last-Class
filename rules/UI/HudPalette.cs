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
/// 每一个都是 <see cref="PaletteEntry"/>：按名字取色板里的一项，取哪一档、为什么，记在色板被借那一条的
/// 「借给界面」里；有测试判这里没有不带项名的颜色。资源条（HUD 那几根与精英血条）、倒地记号与格子角上的
/// 迷你字都是作者画的、不染色，所以这里没有它们的颜色。
///
/// 正文、次要与强调取的是离当初判 12px 中文可读性那组配色（设计仓 tools/font-preview/main.gd）最近的一档，
/// 那次判断大体还成立；换色之后照样要实机重看一遍（设计仓台账 <c>UI-48</c>）。
/// </remarks>
public static class HudPalette
{
    /// <summary>正文。色板 <c>sky 5</c>：深底上的字色。</summary>
    public static PaletteEntry Ink { get; } = new("sky 5", PixelColor.FromHex(0xE3DBCC));

    /// <summary>次要信息：手柄的按键记号、未解锁的技能位标签、不可用按钮的字。色板 <c>stone 4</c>。</summary>
    public static PaletteEntry Dim { get; } = new("stone 4", PixelColor.FromHex(0x92897B));

    /// <summary>
    /// 强调：当前生效的技能组、目标达成那句话，伤害数字也用它。色板 <c>ember 4</c>。
    /// </summary>
    public static PaletteEntry Hot { get; } = new("ember 4", PixelColor.FromHex(0xD99240));

    /// <summary>浅底上的正文：内容区、左栏的盒子、选中的子页签。色板 <c>rust 1</c>。</summary>
    public static PaletteEntry PaperInk { get; } = new("rust 1", PixelColor.FromHex(0x3D1714));

    /// <summary>带字按钮上的字，按下那一态也够亮。色板 <c>outline 1</c>。</summary>
    public static PaletteEntry ButtonInk { get; } = new("outline 1", PixelColor.FromHex(0x0A0815));

    /// <summary>没选中的子页签上的字（底是 <c>soil 3</c>）。色板 <c>soil 8</c>。</summary>
    public static PaletteEntry SubTabInk { get; } = new("soil 8", PixelColor.FromHex(0xDCC68F));

    /// <summary>
    /// 资源条槽里放填充的那一片的颜色。色板 <c>ember 3</c>。它不拿来画：代码按它认出那一片，把出界的填充裁掉。
    /// </summary>
    /// <remarks>
    /// 作者重画那张槽时，放填充的那一片要照旧是这一档；换了档，裁出来的形状就不对了，而那不报错。
    /// </remarks>
    public static PaletteEntry GaugeChannel { get; } = new("ember 3", PixelColor.FromHex(0xB46734));

    /// <summary>
    /// 冷却遮罩。色板 <c>outline 2</c>。不透明，从上往下抹掉图标的一部分，抹完即可用。
    /// </summary>
    /// <remarks>
    /// 用遮住而不是压暗，理由见 <see cref="PixelColor"/>：压暗要 alpha，而 alpha 会在屏幕上留下
    /// 插值像素。遮住也更好认 —— 玩家看的是「还剩多少没退下去」那一块。
    /// </remarks>
    public static PaletteEntry Cooldown { get; } = new("outline 2", PixelColor.FromHex(0x110F1C));
}
