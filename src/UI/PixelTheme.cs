using Godot;
using Tinderhearth.Rules.Ui;

namespace Tinderhearth.UI;

/// <summary>
/// 像素字体与主题的接线（`UI-8` 的第一步）。**并且把字体的十项属性读回来核一遍。**
/// </summary>
/// <remarks>
/// 为什么核而不是设：真正生效的设置在 <c>assets/fonts/…zh_hans.ttf.import</c> 的
/// <c>[params]</c> 段（导入器消费它、烘出 <c>.fontdata</c>），期望值在
/// <see cref="PixelFont"/>。两者分在两处才互为量具 —— 同源的守卫等于没有守卫，改错设置时判据
/// 会跟着一起改掉。这里的职责就是把引擎自己报的实际值与期望值逐项比对并打进日志，
/// `tools/check_hud.py` 读回来判。
///
/// 十项里每一项的失效方式都是静默的：抗锯齿一开，12px 中文多出一圈半透明脏边；
/// <c>allow_system_fallback</c> 一开，缺字悄悄换成系统中文字体，**而且每台机器表现不同** ——
/// 那种缺陷在自己机器上永远看不到。
///
/// **字号与行高不在这里定**，在 <see cref="UiMetrics"/>；配色也不在这里，在
/// <see cref="HudPalette"/>（占位，归 `DOC-2`）。本类只负责把它们装进 <see cref="Theme"/>。
/// </remarks>
public static class PixelTheme
{
    /// <summary>一项属性的核对结果。</summary>
    /// <param name="Name">属性名，与 [ADR-0008] 那张表同名。</param>
    /// <param name="Expected">期望值（<see cref="PixelFont"/>）。</param>
    /// <param name="Actual">引擎自报的实际值。</param>
    public sealed record Check(string Name, string Expected, string Actual)
    {
        /// <summary>对上了没有。</summary>
        public bool Ok => Expected == Actual;
    }

    /// <summary>
    /// 装好主题：把像素字体设成全局回退字体，并造一份带占位配色的 <see cref="Theme"/>。
    /// </summary>
    /// <remarks>
    /// 用 <see cref="ThemeDB.FallbackFont"/> 而不是只给每个界面挂 <see cref="Theme"/>：回退字体是
    /// **全局**的，于是任何忘了挂主题的 Control 也拿到像素字体，而不是悄悄退回引擎默认字体。
    /// 忘挂主题不报错 —— 这条正是为了让它不必靠记性。
    /// </remarks>
    public static Theme Install(out IReadOnlyList<Check> checks)
    {
        var font = LoadFont();
        checks = Verify(font);

        ThemeDB.FallbackFont = font;
        ThemeDB.FallbackFontSize = UiMetrics.FontSize;

        var theme = new Theme
        {
            DefaultFont = font,
            DefaultFontSize = UiMetrics.FontSize,
        };

        // 只设本轮真用到的几项。**不预设一整套** —— 没有界面在用的主题项是猜出来的，
        // 而猜错的默认值会在将来某个界面上表现成「颜色不知道从哪来的」。
        theme.SetColor("font_color", "Label", Ink);
        theme.SetColor("font_color", "Button", Ink);
        theme.SetColor("font_disabled_color", "Button", ToColor(HudPalette.Dim));
        return theme;
    }

    /// <summary>正文色。界面代码取它，不各自写一遍色值。</summary>
    public static Color Ink => ToColor(HudPalette.Ink);

    /// <summary>把规则层的不透明色翻成引擎的 <see cref="Color"/>。**alpha 恒为满。**</summary>
    public static Color ToColor(PixelColor color) => Color.Color8(color.R, color.G, color.B);

    /// <summary>载字体。**缺了就抛** —— 静默用引擎默认字体会让整套像素排版看起来「差一点」。</summary>
    public static FontFile LoadFont() =>
        ResourceLoader.Exists(PixelFont.ResourcePath)
            ? GD.Load<FontFile>(PixelFont.ResourcePath)
            : throw new FileNotFoundException(
                $"缺字体：{PixelFont.ResourcePath}（取法见 README「像素字体怎么进来的」）");

    /// <summary>把引擎自报的十项属性与 <see cref="PixelFont"/> 逐项比对。</summary>
    private static IReadOnlyList<Check> Verify(FontFile font) =>
    [
        new("antialiasing", PixelFont.Antialiasing.ToString(),
            Read(font.Antialiasing).ToString()),
        new("hinting", PixelFont.Hinting.ToString(),
            Read(font.Hinting).ToString()),
        new("subpixel_positioning", PixelFont.SubpixelPositioning.ToString(),
            Read(font.SubpixelPositioning).ToString()),
        new("multichannel_signed_distance_field",
            PixelFont.MultichannelSignedDistanceField.ToString(),
            font.MultichannelSignedDistanceField.ToString()),
        new("generate_mipmaps", PixelFont.GenerateMipmaps.ToString(),
            font.GenerateMipmaps.ToString()),
        new("force_autohinter", PixelFont.ForceAutohinter.ToString(),
            font.ForceAutohinter.ToString()),
        new("disable_embedded_bitmaps", PixelFont.DisableEmbeddedBitmaps.ToString(),
            font.DisableEmbeddedBitmaps.ToString()),
        new("keep_rounding_remainders", PixelFont.KeepRoundingRemainders.ToString(),
            font.KeepRoundingRemainders.ToString()),
        new("oversampling", PixelFont.Oversampling.ToString("0.###"),
            font.Oversampling.ToString("0.###")),
        new("allow_system_fallback", PixelFont.AllowSystemFallback.ToString(),
            font.AllowSystemFallback.ToString()),
    ];

    // 三个枚举**按符号翻译，不按数值转换**。强转会把「引擎改了枚举值」这种事悄悄咽掉，
    // 而那正是最需要被报出来的一类变化。
    private static FontAntialiasing Read(TextServer.FontAntialiasing value) => value switch
    {
        TextServer.FontAntialiasing.None => FontAntialiasing.None,
        TextServer.FontAntialiasing.Gray => FontAntialiasing.Gray,
        TextServer.FontAntialiasing.Lcd => FontAntialiasing.LcdSubpixel,
        _ => throw new ArgumentOutOfRangeException(nameof(value),
            $"引擎报了一个规则层不认识的抗锯齿方式：{value}"),
    };

    private static FontHinting Read(TextServer.Hinting value) => value switch
    {
        TextServer.Hinting.None => FontHinting.None,
        TextServer.Hinting.Light => FontHinting.Light,
        TextServer.Hinting.Normal => FontHinting.Normal,
        _ => throw new ArgumentOutOfRangeException(nameof(value),
            $"引擎报了一个规则层不认识的微调方式：{value}"),
    };

    private static FontSubpixelPositioning Read(TextServer.SubpixelPositioning value) => value switch
    {
        TextServer.SubpixelPositioning.Disabled => FontSubpixelPositioning.Disabled,
        TextServer.SubpixelPositioning.Auto => FontSubpixelPositioning.Auto,
        TextServer.SubpixelPositioning.OneHalf => FontSubpixelPositioning.OneHalf,
        TextServer.SubpixelPositioning.OneQuarter => FontSubpixelPositioning.OneQuarter,
        _ => throw new ArgumentOutOfRangeException(nameof(value),
            $"引擎报了一个规则层不认识的次像素定位方式：{value}"),
    };
}
