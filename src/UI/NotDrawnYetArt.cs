using Godot;
using Tinderhearth.Rules.UI;

namespace Tinderhearth.UI;

/// <summary>图还是「暂无」时画的那一张统一占位：一个只描一圈边的方框，大小与正式的图相同。</summary>
/// <remarks>
/// 口径在设计仓 production/像素绘制原则.md 的「像素基准」一节：每一样都画同一个框，不画剪影 —— 剪影会漏出形状，
/// 作者照着画时被它带偏。只描边、中间透明：作物画在耕地上，实心的框会盖住那一格干没干、湿没湿，而浇水时看的正是它。
/// 边的颜色取色板里的一档（<see cref="HudPalette.Dim"/>），不在这里写一个色板以外的颜色。
///
/// 图标那一类不用它，用文字（<see cref="IconPlaceholder"/>）。一种尺寸只做一张，之后复用。
/// </remarks>
public static class NotDrawnYetArt
{
    private static readonly Dictionary<Vector2I, Texture2D> Boxes = [];

    /// <summary>这么大的一张占位框。</summary>
    /// <param name="size">正式的图有多大，像素；两边都至少 1。</param>
    public static Texture2D Box(Vector2I size)
    {
        if (size.X < 1 || size.Y < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(size), size, "占位框两边都至少 1 像素");
        }

        if (Boxes.TryGetValue(size, out var made))
        {
            return made;
        }

        var image = Image.CreateEmpty(size.X, size.Y, false, Image.Format.Rgba8);
        var edge = PixelTheme.ToColor(HudPalette.Dim.Color);
        for (var x = 0; x < size.X; x++)
        {
            image.SetPixel(x, 0, edge);
            image.SetPixel(x, size.Y - 1, edge);
        }

        for (var y = 0; y < size.Y; y++)
        {
            image.SetPixel(0, y, edge);
            image.SetPixel(size.X - 1, y, edge);
        }

        var texture = ImageTexture.CreateFromImage(image);
        Boxes[size] = texture;
        return texture;
    }
}
