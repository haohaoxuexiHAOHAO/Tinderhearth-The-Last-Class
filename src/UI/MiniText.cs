using Godot;
using Tinderhearth.Rules.UI;

namespace Tinderhearth.UI;

/// <summary>
/// 一行迷你字：逐字从 <c>kit/font_mini</c> 那张字形表上取一格画出来，贴在自己矩形的一个角上。
/// </summary>
/// <remarks>
/// 不走 Godot 的图片字体导入：那张图要导成字体就离开了素材资源那一格，启动时核不到它，字形顺序还得在导入设置里
/// 再写一份。取哪一格、字与字挪多少在 <see cref="MiniFont"/>，本类只画，也不染色。
/// </remarks>
public sealed partial class MiniText : Control
{
    /// <summary>字贴在矩形的哪个角上。</summary>
    public enum Corner
    {
        /// <summary>左上：格子上的键名。</summary>
        TopLeft,

        /// <summary>右下：格子上的件数。</summary>
        BottomRight,
    }

    private readonly Texture2D _sheet;
    private readonly Corner _corner;
    private string _text = string.Empty;

    /// <summary>造一行空的。字形表从素材资源取，素材资源启动时核过。</summary>
    public MiniText(UIArt art, Corner corner)
    {
        _sheet = HudPlace.Piece(art.KitFontMini, "kit/font_mini");
        _corner = corner;
        MouseFilter = MouseFilterEnum.Ignore;
    }

    /// <summary>显示的字。字形表里没有的字当场抛，并点名是哪几个。</summary>
    public string Text
    {
        get => _text;
        set
        {
            if (value == _text)
            {
                return;
            }

            MiniFont.Require(value);
            _text = value;
            QueueRedraw();
        }
    }

    public override void _Draw()
    {
        // 贴右下时从右边往左让出整行的宽：字多字少，最后一个字都压在同一个角上。
        var x = _corner == Corner.TopLeft ? 0 : (int)Size.X - MiniFont.WidthOf(_text);
        var y = _corner == Corner.TopLeft ? 0 : (int)Size.Y - MiniFont.CellHeight;
        for (var i = 0; i < _text.Length; i++)
        {
            DrawTextureRectRegion(_sheet,
                new Rect2(x + MiniFont.XOf(i), y, MiniFont.CellWidth, MiniFont.CellHeight),
                new Rect2(MiniFont.SheetXOf(_text[i]), 0, MiniFont.CellWidth, MiniFont.CellHeight));
        }
    }
}
