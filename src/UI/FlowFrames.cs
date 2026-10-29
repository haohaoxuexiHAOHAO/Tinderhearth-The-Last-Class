using Godot;
using Tinderhearth.Rules.UI;

namespace Tinderhearth.UI;

/// <summary>
/// 一张九宫格件中段挪开的那几张图：第 k 张是中段沿流动方向挪了 k 像素的样子，两头不动、像素全取自原图。
/// </summary>
/// <remarks>
/// 挪的是贴图里平铺的那一段，所以换一张贴图，整根条上铺开的花纹就一起挪了一格，接缝照旧接得上；第 0 张就是原图。
/// 每一格取原图哪一格在 <see cref="GaugeFlow.SourceIndex"/>。同一张原图只切一次，按原图与方向记下来。
/// </remarks>
internal static class FlowFrames
{
    private static readonly Dictionary<(Texture2D Source, bool Across), IReadOnlyList<Texture2D>> Cache = [];

    /// <summary>横着铺的件：中段往右挪。</summary>
    public static IReadOnlyList<Texture2D> Rightward(NinePatchRect piece) => Of(piece, across: true);

    /// <summary>竖着铺的件：中段往上挪。</summary>
    public static IReadOnlyList<Texture2D> Upward(NinePatchRect piece) => Of(piece, across: false);

    private static IReadOnlyList<Texture2D> Of(NinePatchRect piece, bool across)
    {
        var source = piece.Texture;
        if (Cache.TryGetValue((source, across), out var cached))
        {
            return cached;
        }

        var image = source.GetImage() ?? throw new InvalidOperationException(
            $"{source.ResourcePath} 读不出像素，中段挪不了 —— 那张图要按无损导入（项目里界面件的默认导入就是）");
        if (image.IsCompressed())
        {
            image.Decompress();
        }

        image.Convert(Image.Format.Rgba8);
        var width = image.GetWidth();
        var height = image.GetHeight();
        var start = across ? piece.PatchMarginLeft : piece.PatchMarginTop;
        var period = across
            ? width - piece.PatchMarginLeft - piece.PatchMarginRight
            : height - piece.PatchMarginTop - piece.PatchMarginBottom;
        if (period < 1)
        {
            throw new InvalidOperationException(
                $"{source.ResourcePath} 的九宫格边距把中段占满了，没有可挪的那一段 —— 在素材资源里核那两格边距");
        }

        var frames = new List<Texture2D>(period) { source };
        for (var offset = 1; offset < period; offset++)
        {
            var frame = (Image)image.Duplicate();
            for (var index = 0; index < period; index++)
            {
                var from = GaugeFlow.SourceIndex(index, offset, period);
                if (across)
                {
                    for (var y = 0; y < height; y++)
                    {
                        frame.SetPixel(start + index, y, image.GetPixel(start + from, y));
                    }
                }
                else
                {
                    // 往上挪：沿流动方向是从中段最下面那一行往上数。
                    var to = start + period - 1 - index;
                    var row = start + period - 1 - from;
                    for (var x = 0; x < width; x++)
                    {
                        frame.SetPixel(x, to, image.GetPixel(x, row));
                    }
                }
            }

            frames.Add(ImageTexture.CreateFromImage(frame));
        }

        Cache[(source, across)] = frames;
        return frames;
    }
}
