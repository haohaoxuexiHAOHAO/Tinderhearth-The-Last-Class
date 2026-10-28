using Godot;
using Tinderhearth.Rules.UI;

namespace Tinderhearth.UI;

/// <summary>
/// 像素字体与主题的接线：装好全局字体与一份主题，并把字体那几项导入属性读回来核一遍。
/// </summary>
/// <remarks>
/// 这里是核对、不是设置：真正生效的设置在
/// <c>assets/fonts/fusion-pixel-12px-proportional-zh_hans.ttf.import</c> 的 <c>[params]</c> 段，
/// 期望值在 <see cref="PixelFont"/>。两边分开放才能互相对照，核完逐项打进启动日志。
///
/// 这里面每一项错了都不报错：抗锯齿一开，12px 中文多出一圈半透明脏边；
/// <c>allow_system_fallback</c> 一开，缺字悄悄换成系统中文字体，而且每台机器表现不同 ——
/// 那种毛病在自己机器上永远看不到。
///
/// 字号与行高不在这里定，在 <see cref="UIMetrics"/>；配色也不在这里，在
/// <see cref="HudPalette"/>。本类只负责把它们装进 <see cref="Theme"/>。
/// </remarks>
public static class PixelTheme
{
    /// <summary>一项属性的核对结果。</summary>
    /// <param name="Name">属性名，与导入设置里那一项同名。</param>
    /// <param name="Expected">期望值（<see cref="PixelFont"/>）。</param>
    /// <param name="Actual">引擎自报的实际值。</param>
    public sealed record Check(string Name, string Expected, string Actual)
    {
        /// <summary>对上了没有。</summary>
        public bool Ok => Expected == Actual;
    }

    /// <summary>手环顶层页签那一种按钮（只有图标）。</summary>
    public const string WristbandTab = "WristbandTab";

    /// <summary>手环子页签那一种按钮（只有字）。</summary>
    public const string WristbandSubTab = "WristbandSubTab";

    /// <summary>手环外壳那一圈厚框。</summary>
    public const string WristbandFrame = "WristbandFrame";

    /// <summary>面板里的内容区：子页签下面那一大块浅底。</summary>
    public const string ContentInset = "ContentInset";

    /// <summary>浅底上的字。</summary>
    public const string PaperLabel = "PaperLabel";

    /// <summary>
    /// 装好主题：把像素字体设成全局回退字体，并用素材资源里的图组装按钮、页签与面板的样子。
    /// </summary>
    /// <remarks>
    /// 用 <see cref="ThemeDB.FallbackFont"/> 而不是只给每个界面挂 <see cref="Theme"/>：回退字体
    /// 是全局的，于是忘了挂主题的 Control 也拿到像素字体，而不是悄悄退回引擎默认字体。忘挂主题
    /// 不报错，这一条就是为了让它不必靠记性。
    ///
    /// 主题项由代码按素材资源组装，不在主题编辑器里手敲项名：项名敲错不报错，表现只是那一处退回引擎默认的样子。
    /// 九宫格边距读那一格自己的（作者重画边框时在检查器里改），字离框多远是内容边距、从排版单位取，两样分开。
    /// 几种页签、外框与内容区是基于按钮、面板的类型变体，名字在本类的常量里，界面代码按名字取。
    /// </remarks>
    public static Theme Install(UIArt art, out IReadOnlyList<Check> checks)
    {
        ArgumentNullException.ThrowIfNull(art);
        var font = LoadFont();
        checks = Verify(font);

        ThemeDB.FallbackFont = font;
        ThemeDB.FallbackFontSize = UIMetrics.FontSize;

        var theme = new Theme
        {
            DefaultFont = font,
            DefaultFontSize = UIMetrics.FontSize,
        };

        // 只设当前真用到的几项，不预设一整套：没有界面在用的主题项是猜出来的，
        // 而猜错的默认值会在将来某个界面上表现成「这颜色不知道从哪来的」。
        theme.SetColor("font_color", "Label", Ink);

        // 带字的按钮：焦点与鼠标悬停同一张（界面绘制约定）。字取 outline 1，按下那一态也读得清。
        // 焦点那一张是盖在当前那一态上面画的，所以带焦点时按下那一态看不出来；手柄按下那一下很短，先这样。
        var pad = UIMetrics.PanelPadding;
        var focused = Box(art.KitButtonFocus, pad, pad);
        SetButton(theme, "Button", normal: Box(art.KitButtonNormal, pad, pad), hover: focused, focus: focused,
            pressed: Box(art.KitButtonPressed, pad, pad), disabled: Box(art.KitButtonDisabled, pad, pad));
        SetFontColors(theme, "Button", ToColor(HudPalette.ButtonInk), ToColor(HudPalette.ButtonInk));

        // 顶层页签：只有图标，图标放在页签底的 (2,2)。选中那一态用按下（页签是切换按钮）。页签没有画悬停与
        // 焦点那两态：悬停照旧是没选中的样子，焦点先用一圈强调色的细框顶着 —— 不顶的话手柄停在哪个页签上看不出来。
        theme.SetTypeVariation(WristbandTab, "Button");
        var inset = WristbandLayout.TabIconInset;
        var tab = Plain(art.KitTabNormal, inset);
        SetButton(theme, WristbandTab, normal: tab, hover: tab, focus: FocusRing(),
            pressed: Plain(art.KitTabSelected, inset), disabled: Plain(art.KitTabDisabled, inset));
        // 不可用那一态换的是置灰版图标，不靠引擎默认那层半透明 —— 乘出来的颜色不在色板里，还带半透明。
        theme.SetColor("icon_disabled_color", WristbandTab, Colors.White);

        // 子页签：只有字。没选中的底是 soil 3、字取 soil 8；选中的底是 soil 8、字取 rust 1。
        theme.SetTypeVariation(WristbandSubTab, "Button");
        var subPad = WristbandLayout.SubTabPaddingX;
        var subTab = Box(art.KitTabtextNormal, subPad, 0);
        SetButton(theme, WristbandSubTab, normal: subTab, hover: subTab, focus: FocusRing(),
            pressed: Box(art.KitTabtextSelected, subPad, 0), disabled: Box(art.KitTabtextDisabled, subPad, 0));
        SetFontColors(theme, WristbandSubTab, ToColor(HudPalette.SubTabInk), ToColor(HudPalette.PaperInk));

        // 手环外壳那一圈厚框，与里面的浅底内容区。
        theme.SetTypeVariation(WristbandFrame, "PanelContainer");
        theme.SetStylebox("panel", WristbandFrame, Box(art.KitFrameThickDark, WristbandLayout.FramePadding,
            WristbandLayout.FramePadding));
        theme.SetTypeVariation(ContentInset, "PanelContainer");
        theme.SetStylebox("panel", ContentInset, Box(art.KitInset, UIMetrics.Grid, UIMetrics.Grid));
        theme.SetTypeVariation(PaperLabel, "Label");
        theme.SetColor("font_color", PaperLabel, ToColor(HudPalette.PaperInk));
        return theme;
    }

    /// <summary>一件九宫格件做成一个样式盒：贴图边距读那一格，内容边距由调用方按排版单位给。</summary>
    private static StyleBoxTexture Box(NinePiece? piece, int contentX, int contentY)
    {
        var nine = piece ?? throw new InvalidOperationException("素材资源里少了一件九宫格件 —— 应当已在启动时核过");
        return new StyleBoxTexture
        {
            Texture = nine.Texture,
            TextureMarginLeft = nine.Left,
            TextureMarginTop = nine.Top,
            TextureMarginRight = nine.Right,
            TextureMarginBottom = nine.Bottom,
            ContentMarginLeft = contentX,
            ContentMarginRight = contentX,
            ContentMarginTop = contentY,
            ContentMarginBottom = contentY,
        };
    }

    /// <summary>一张不拉伸的图做成样式盒（页签底）。按钮做成和图一样大，所以它不会被拉开。</summary>
    private static StyleBoxTexture Plain(Texture2D? texture, int content) => new()
    {
        Texture = texture ?? throw new InvalidOperationException("素材资源里少了一张页签底 —— 应当已在启动时核过"),
        ContentMarginLeft = content,
        ContentMarginTop = content,
        ContentMarginRight = content,
        ContentMarginBottom = content,
    };

    /// <summary>焦点那一圈：一像素强调色，不填底。页签那几种没画焦点那一态之前用它顶着。</summary>
    private static StyleBoxFlat FocusRing()
    {
        var ring = new StyleBoxFlat
        {
            DrawCenter = false,
            BorderColor = ToColor(HudPalette.Hot),
            AntiAliasing = false,
        };
        ring.SetBorderWidthAll(1);
        return ring;
    }

    private static void SetButton(Theme theme, string type, StyleBox normal, StyleBox hover, StyleBox focus,
        StyleBox pressed, StyleBox disabled)
    {
        theme.SetStylebox("normal", type, normal);
        theme.SetStylebox("hover", type, hover);
        theme.SetStylebox("pressed", type, pressed);
        theme.SetStylebox("hover_pressed", type, pressed);
        theme.SetStylebox("disabled", type, disabled);
        theme.SetStylebox("focus", type, focus);
    }

    private static void SetFontColors(Theme theme, string type, Color idle, Color pressed)
    {
        theme.SetColor("font_color", type, idle);
        theme.SetColor("font_hover_color", type, idle);
        theme.SetColor("font_focus_color", type, idle);
        theme.SetColor("font_pressed_color", type, pressed);
        theme.SetColor("font_hover_pressed_color", type, pressed);
        theme.SetColor("font_disabled_color", type, ToColor(HudPalette.Dim));
    }

    /// <summary>正文色。界面代码取它，不各自写一遍色值。</summary>
    public static Color Ink => ToColor(HudPalette.Ink);

    /// <summary>把规则层的不透明色翻成引擎的 <see cref="Color"/>。alpha 恒为满。</summary>
    public static Color ToColor(PixelColor color) => Color.Color8(color.R, color.G, color.B);

    /// <summary>载字体。缺了就抛 —— 悄悄退回引擎默认字体会让整套像素排版看起来「差一点」。</summary>
    public static FontFile LoadFont() =>
        ResourceLoader.Exists(PixelFont.ResourcePath)
            ? GD.Load<FontFile>(PixelFont.ResourcePath)
            : throw new FileNotFoundException(
                $"缺字体：{PixelFont.ResourcePath}（这个文件从哪来见 README 的「像素字体怎么进来的」一节）");

    /// <summary>把引擎自报的那几项属性与 <see cref="PixelFont"/> 逐项比对。</summary>
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

    // 下面三个枚举按名字一个一个翻译，不用强转。强转会把「引擎改了某个枚举值」这件事悄悄咽掉，
    // 而那正是最该被报出来的一类变化。
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
