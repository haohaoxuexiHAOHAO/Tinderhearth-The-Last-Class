using Godot;
using Tinderhearth.Rules.UI;

namespace Tinderhearth.UI;

/// <summary>
/// 一根资源条：作者画的槽，加一条按数值改长短的填充。HUD 的三条资源与队友那一根都是它。
/// </summary>
/// <remarks>
/// 照设计仓 production/界面绘制约定.md 那张「代码管什么」表：填充放在槽里 (2,2)（<see cref="HudLayout.GaugeFillInset"/>），
/// 按数值改长度、中段平铺，再按槽里那一片（<see cref="HudPalette.GaugeChannel"/>）裁掉出界的像素；右端三列是斜切的头，
/// 跟着末端走；不染色。
///
/// 三层各管一件事：槽里那一片做遮罩，只让填充落在那一片上（<see cref="CanvasItem.ClipChildren"/>）；它下面一层按长度裁；
/// 最里面才是填充本身。值很低、长度不够放下填充两头那几列时，填充照两头那么宽画、再按长度裁短 —— 九宫格窄过两头时
/// 会把两头压扁，而那不报错。
///
/// 回复时追、扣减时留一截是下一步（设计仓台账 <c>UI-45</c>），这一版长度直接跟着数值走。
/// </remarks>
public sealed partial class GaugeBar : Control
{
    // 同一张槽只认一次那一片：认出来的遮罩按槽那张图记下来。
    private static readonly Dictionary<Texture2D, Texture2D> ChannelMasks = [];

    private readonly int _width;
    private readonly int _fillMinWidth;
    private readonly Control _length;
    private readonly NinePatchRect _fill;

    /// <summary>装一根资源条。槽与填充从素材资源取，缺了当场报错。</summary>
    /// <param name="art">素材资源。</param>
    /// <param name="fill">这一条用哪张填充。</param>
    /// <param name="fillName">那张填充在素材资源里叫什么，报错时用。</param>
    /// <param name="width">整根条多宽，界面像素。高是槽那张图的高（<see cref="HudLayout.GaugeBarHeight"/>）。</param>
    public GaugeBar(UIArt art, NinePiece? fill, string fillName, int width)
    {
        Name = "Bar";
        CustomMinimumSize = new Vector2(width, HudLayout.GaugeBarHeight);
        MouseFilter = MouseFilterEnum.Ignore;
        _width = width;

        // 槽中段每一列都一样，拉伸与平铺画出来一样。
        var track = HudPlace.Stretch(art.HudBarTrack, "hud/bar_track", NinePatchRect.AxisStretchMode.Stretch);
        track.Name = "Track";
        track.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(track);

        // 槽里那一片：和槽一样拉开，自己不画，只拿来裁它里面的东西。
        var channel = HudPlace.Stretch(art.HudBarTrack, "hud/bar_track", NinePatchRect.AxisStretchMode.Stretch);
        channel.Name = "Channel";
        channel.Texture = ChannelMaskOf(channel.Texture);
        channel.ClipChildren = ClipChildrenMode.Only;
        channel.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(channel);

        // 按长度裁的那一层：贴在放填充那一点上，宽就是当前长度。
        _length = new Control
        {
            Name = "Length",
            ClipContents = true,
            MouseFilter = MouseFilterEnum.Ignore,
            OffsetLeft = HudLayout.GaugeFillInset,
            OffsetTop = HudLayout.GaugeFillInset,
            OffsetRight = HudLayout.GaugeFillInset,
            OffsetBottom = HudLayout.GaugeFillInset + HudLayout.GaugeFillHeight,
        };
        channel.AddChild(_length);

        // 填充中段带亮点花纹，只许平铺。
        _fill = HudPlace.Stretch(fill, fillName, NinePatchRect.AxisStretchMode.Tile);
        _fill.Name = "Fill";
        _fill.OffsetBottom = HudLayout.GaugeFillHeight;
        _length.AddChild(_fill);
        _fillMinWidth = _fill.PatchMarginLeft + _fill.PatchMarginRight;
    }

    /// <summary>填充现在画了多长，界面像素。</summary>
    public int Length { get; private set; }

    /// <summary>按比例改长度。比例先钳，取整在 <see cref="HudLayout.GaugeFillLength"/> 里。</summary>
    public void Set(double ratio)
    {
        Length = HudLayout.GaugeFillLength(ratio, _width);
        _length.OffsetRight = HudLayout.GaugeFillInset + Length;
        _fill.OffsetRight = Math.Max(Length, _fillMinWidth);
    }

    /// <summary>
    /// 从槽那张图认出放填充的那一片，做成一张遮罩：那一片的像素不透明，其余全透明。
    /// </summary>
    /// <remarks>
    /// 认的是色板里那一档（<see cref="HudPalette.GaugeChannel"/>）。作者重画那张槽时那一片换了档，认出来就是空的，
    /// 填充一个像素都不显示 —— 所以认出来是空的当场报错，不画一根看不见的条。
    /// </remarks>
    private static Texture2D ChannelMaskOf(Texture2D track)
    {
        if (ChannelMasks.TryGetValue(track, out var cached))
        {
            return cached;
        }

        var image = track.GetImage() ?? throw new InvalidOperationException(
            $"资源条的槽 {track.ResourcePath} 读不出像素 —— 那张图要按无损导入（项目里界面件的默认导入就是）");
        if (image.IsCompressed())
        {
            image.Decompress();
        }

        image.Convert(Image.Format.Rgba8);
        var channel = PixelTheme.ToColor(HudPalette.GaugeChannel);
        var found = 0;
        for (var y = 0; y < image.GetHeight(); y++)
        {
            for (var x = 0; x < image.GetWidth(); x++)
            {
                var pixel = image.GetPixel(x, y);
                var inside = pixel.A8 == channel.A8 && pixel.R8 == channel.R8
                             && pixel.G8 == channel.G8 && pixel.B8 == channel.B8;
                image.SetPixel(x, y, inside ? Colors.White : Colors.Transparent);
                found += inside ? 1 : 0;
            }
        }

        if (found == 0)
        {
            throw new InvalidOperationException(
                $"资源条的槽 {track.ResourcePath} 里认不出放填充的那一片（色板 {HudPalette.GaugeChannel.Name}）"
                    + " —— 重画那张槽时，放填充的那一片要照旧画成这一档");
        }

        var mask = ImageTexture.CreateFromImage(image);
        ChannelMasks[track] = mask;
        return mask;
    }
}
