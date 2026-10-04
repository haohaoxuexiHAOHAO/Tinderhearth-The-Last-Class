namespace Tinderhearth.Rules.UI;

/// <summary>字体抗锯齿方式。规则层不引用 Godot，所以自己定一套符号，翻译在引擎层。</summary>
public enum FontAntialiasing
{
    /// <summary>不抗锯齿。抗锯齿会造出半透明像素，而透明度只能全透或全不透。</summary>
    None,

    /// <summary>灰度抗锯齿（引擎默认）。</summary>
    Gray,

    /// <summary>次像素抗锯齿。</summary>
    LcdSubpixel,
}

/// <summary>字形微调方式。</summary>
public enum FontHinting
{
    /// <summary>不微调。字形本来就在整数网格上，微调只会把它挪开。</summary>
    None,

    /// <summary>轻微调。</summary>
    Light,

    /// <summary>常规微调。</summary>
    Normal,
}

/// <summary>次像素定位精度。</summary>
public enum FontSubpixelPositioning
{
    /// <summary>关闭。半像素定位就是糊边。</summary>
    Disabled,

    /// <summary>自动。</summary>
    Auto,

    /// <summary>半像素。</summary>
    OneHalf,

    /// <summary>四分之一像素。</summary>
    OneQuarter,
}

/// <summary>
/// 像素字体的期望取值。引擎层读回实际值，与本表逐项比对。
/// </summary>
/// <remarks>
/// 本表是期望值，不是设置值：真正生效的设置在 <c>assets/fonts/…zh_hans.ttf.import</c> 的
/// <c>[params]</c> 段。两处刻意分开好互相核对 —— 改了导入设置，引擎层读回的实际值就与本表对不上
/// 并打进启动日志；改了本表，单元测试失败。写成一处就没有这个性质了。
///
/// 这些项非钉不可，因为它们的失效方式全是静默的。抗锯齿一开，12px 中文就多出一圈半透明脏边；
/// 系统字体回退一开，缺字会悄悄换成系统中文字体，像素风当场破掉而且每台机器表现不同。画面上
/// 只是「有点糊」「有点不对」，不报错。
///
/// 取值来自中文像素字体那次选型的实测表，本文件不重新论证。字号与行高不在这里 ——
/// 它们是排版单位，在 <see cref="UIMetrics"/>。
/// </remarks>
public static class PixelFont
{
    /// <summary>字体文件在工程里的位置。</summary>
    public const string ResourcePath =
        "res://assets/fonts/fusion-pixel-12px-proportional-zh_hans.ttf";

    /// <summary>
    /// 随字体一起发行的许可证。OFL 第 2 条要求每份拷贝都带它，所以它必须进发行包。
    /// </summary>
    /// <remarks>
    /// <c>tools/verify.py</c> 的解包清单那一步会判：包里有字体却没有这份许可证就失败。
    /// 漏掉不会报错，只会变成上架之后的法律问题 —— 那正是需要机器来判的那类事。
    /// </remarks>
    public const string LicensePath = "res://assets/fonts/LICENSE-OFL.txt";

    /// <summary>
    /// 上游版本号。钉死；上游发了新版要先重跑字体审计（设计仓 tools/audit_fonts.py）再换。
    /// </summary>
    public const string UpstreamVersion = "2026.08.11";

    /// <summary>
    /// 字体文件的 SHA256。拿它核「仓里这份就是审计过的那份」。
    /// </summary>
    /// <remarks>
    /// 字体是二进制，换成另一个版本、或者换成另一个字形版本（<c>zh_hant</c>／<c>ja</c>），在
    /// git diff 里只是一行「二进制文件有差异」，而字形覆盖与度量会跟着变。钉住内容，
    /// 「审计过」这句话才指得到一份确定的文件。
    /// </remarks>
    public const string Sha256 =
        "5f43f090748d2ee00792d942a1257f44723b807b5cfefa133a856a3a0c5ff702";

    /// <summary>字体文件字节数。与 SHA256 一起看，两样都对得上才算同一份文件。</summary>
    public const int FileBytes = 6995400;

    /// <summary>
    /// 保留字体名。没做子集化也没改字形，所以沿用原名是合规的（OFL 第 3 条）。
    /// </summary>
    /// <remarks>
    /// 这条常量的用处是让 OFL 第 3 条有个能指的东西：将来真做了子集化或改字形，输出的字体名就不能
    /// 再叫这个 —— 那时改这里，同时改字体审计脚本与字体选型那份决定。
    /// </remarks>
    public const string ReservedFamilyName = "Fusion Pixel 12px Proportional";

    // ── 下面十项逐项都有理由，别按「看着差不多」改 ──

    /// <summary>
    /// 抗锯齿：关。半透明脏边破坏像素对齐，见设计仓 production/像素绘制原则.md 的
    /// 「硬边、抗锯齿与点绘」一节。
    /// </summary>
    public const FontAntialiasing Antialiasing = FontAntialiasing.None;

    /// <summary>微调：关。字形本来就在整数网格上。</summary>
    public const FontHinting Hinting = FontHinting.None;

    /// <summary>次像素定位：关。</summary>
    public const FontSubpixelPositioning SubpixelPositioning = FontSubpixelPositioning.Disabled;

    /// <summary>多通道有向距离场：关。MSDF 是为任意缩放做的，与像素风相反。</summary>
    public const bool MultichannelSignedDistanceField = false;

    /// <summary>字形 mipmap：关。缩小级别对像素风没有意义。</summary>
    public const bool GenerateMipmaps = false;

    /// <summary>强制自动微调：关。同微调那一项。</summary>
    public const bool ForceAutohinter = false;

    /// <summary>禁用内嵌位图：开。只用轮廓，行为可预测。</summary>
    public const bool DisableEmbeddedBitmaps = true;

    /// <summary>保留取整余量：关。字宽本来就是整数，不需要余量累计。</summary>
    public const bool KeepRoundingRemainders = false;

    /// <summary>过采样倍数：1。任何过采样都会引入非整数缩放。</summary>
    public const float Oversampling = 1.0f;

    /// <summary>
    /// 系统字体回退：关。有意让缺字显成豆腐块。
    /// </summary>
    /// <remarks>
    /// 开着的后果是缺字悄悄换成系统中文字体（微软雅黑之类），像素风当场破掉，而且每台机器表现
    /// 不同 —— 那种缺陷在自己机器上永远看不到。真缺字时的补法是给字体加 <c>fallbacks</c> 链，
    /// 不是打开系统回退。
    /// </remarks>
    public const bool AllowSystemFallback = false;

    /// <summary>上面那十项的条数。引擎层报「几项里几项一致」时用它，免得两边各数一遍。</summary>
    public const int PropertyCount = 10;
}
