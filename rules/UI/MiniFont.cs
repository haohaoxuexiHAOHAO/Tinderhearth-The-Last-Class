namespace Tinderhearth.Rules.UI;

/// <summary>
/// 迷你字：格子角上的件数与键名用的那一排字形（<c>kit/font_mini</c>）。一个字取一格，挨着的两个字叠一列。
/// </summary>
/// <remarks>
/// 有哪几个字、什么顺序、一格多大，都照素材清单里那一件（<see cref="UIArtManifest"/>），家在生成器，这里不另写一份。
///
/// 正文那款字不进格子角，理由在设计仓 production/界面绘制约定.md 的「12 像素字落在哪几行」。
/// </remarks>
public static class MiniFont
{
    /// <summary>素材清单里那一件：一排等宽的字形格。</summary>
    public static UIArtPiece Sheet { get; } = UIArtManifest.Find("kit", "font_mini");

    /// <summary>字形表从左到右是哪几个字。</summary>
    public static string Glyphs { get; } = Sheet.Glyphs ?? throw new InvalidOperationException(
        $"素材清单里 {Sheet.FilePath} 没写字形顺序：生成器（tools/ui_art.py）那一件要带上 glyphs");

    /// <summary>一格多宽，像素：整排的宽按字数等分。</summary>
    public static int CellWidth => Sheet.Size.Width / Glyphs.Length;

    /// <summary>一格多高，像素：整排的高。</summary>
    public static int CellHeight => Sheet.Size.Height;

    /// <summary>一格四周那圈描边多宽，像素：生成器在每个字形外面画一圈这么宽的描边。</summary>
    /// <remarks>
    /// 字形本身不碰格子的边，所以挨着的两个字叠这么多列只叠到描边：一个字的右边与下一个字的左边并成一条，
    /// 两个字之间只隔一列描边色。
    /// </remarks>
    public const int Outline = 1;

    /// <summary>一个字比前一个字往右挪多少，像素：一格的宽减去叠掉的那一列描边。</summary>
    public static int Advance => CellWidth - Outline;

    /// <summary>这一串里有没有字形表外的字。空串写得出来，画出来什么都没有。</summary>
    public static bool CanWrite(string text) => Missing(text).Length == 0;

    /// <summary>
    /// 写不出来就抛，并点名是哪几个字。
    /// </summary>
    /// <remarks>
    /// 抛而不是跳过那个字：跳过会让「忘了加字形」与「这里本来就不显示」长得一模一样，与按键记号那一处同一个理由
    /// （<see cref="InputHints.LabelOf"/>）。
    /// </remarks>
    public static void Require(string text)
    {
        var missing = Missing(text);
        if (missing.Length > 0)
        {
            throw new ArgumentException(
                $"迷你字写不出「{text}」：字形表里没有「{missing}」。字形表是 {Glyphs}，"
                    + $"要加字就改生成器（tools/ui_art.py）里 {Sheet.Group}/{Sheet.Name} 那一件",
                nameof(text));
        }
    }

    /// <summary>这个字在字形表那张图上从哪一列起，像素。</summary>
    public static int SheetXOf(char glyph)
    {
        Require(glyph.ToString());
        return Glyphs.IndexOf(glyph, StringComparison.Ordinal) * CellWidth;
    }

    /// <summary>一行字里第 <paramref name="index"/> 个字的那一格离这一行左边多远，像素。</summary>
    public static int XOf(int index) => index * Advance;

    /// <summary>一行字多宽，像素：每个字一格、挨着的叠一列。空串是 0。</summary>
    public static int WidthOf(string text) => text.Length == 0 ? 0 : XOf(text.Length - 1) + CellWidth;

    private static string Missing(string text) =>
        string.Concat(text.Where(c => Glyphs.IndexOf(c, StringComparison.Ordinal) < 0).Distinct());
}
