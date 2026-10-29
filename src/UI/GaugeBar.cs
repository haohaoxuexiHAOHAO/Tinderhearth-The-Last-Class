using Godot;
using Tinderhearth.Rules.UI;

namespace Tinderhearth.UI;

/// <summary>
/// 一根资源条：作者画的槽，加一条按数值改长短的填充，扣减时填充后面露出浅色那一截。HUD 的三条资源与队友那一根都是它。
/// </summary>
/// <remarks>
/// 照设计仓 production/界面绘制约定.md 那张「代码管什么」表：填充放在槽里 (2,2)（<see cref="HudLayout.GaugeFillInset"/>），
/// 按长度裁、中段平铺，再按槽里那一片（<see cref="HudPalette.GaugeChannel"/>）裁掉出界的像素；右端三列是斜切的头，跟着末端走；
/// 不染色。回复时填充逐帧追上数值、追的那段时间里中段往右流，扣减时那一截留成浅色、停一会儿再缩（<see cref="GaugeChase"/>、<see cref="GaugeFlow"/>）。
///
/// 槽里那一片做遮罩，只让里面的东西落在那一片上（<see cref="CanvasItem.ClipChildren"/>）。那一截与填充各有一层按长度裁，
/// 那一截垫在下面，两张的头一样斜，所以露出来的只有它比填充长的那一段。值很低、长度不够放下两头那几列时，照两头那么宽画、
/// 再按长度裁短 —— 九宫格窄过两头时会把两头压扁，而那不报错。
/// </remarks>
public sealed partial class GaugeBar : Control
{
    // 同一张槽只认一次那一片：认出来的遮罩按槽那张图记下来。
    private static readonly Dictionary<Texture2D, Texture2D> ChannelMasks = [];

    private readonly int _width;
    private readonly GaugeChaseSettings _chaseSettings;
    private readonly Clipped _lag;
    private readonly Clipped _fill;
    private readonly IReadOnlyList<Texture2D> _flowFrames;
    private readonly GaugeFlow _flow;
    private GaugeChase? _chase;

    /// <summary>装一根资源条。槽、填充与那一截从素材资源取，缺了当场报错。</summary>
    /// <param name="art">素材资源。</param>
    /// <param name="fill">这一条用哪张填充。</param>
    /// <param name="fillName">那张填充在素材资源里叫什么，报错时用。</param>
    /// <param name="width">整根条多宽，界面像素。高是槽那张图的高（<see cref="HudLayout.GaugeBarHeight"/>）。</param>
    /// <param name="motion">追多快、那一截停多久、中段流多快。</param>
    public GaugeBar(UIArt art, NinePiece? fill, string fillName, int width, GaugeMotionSettings motion)
    {
        Name = "Bar";
        CustomMinimumSize = new Vector2(width, HudLayout.GaugeBarHeight);
        MouseFilter = MouseFilterEnum.Ignore;
        _width = width;
        _chaseSettings = motion.Chase;

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

        // 先加的画在下面。两张的中段都只许平铺：填充带亮点花纹，那一截跟着同一个画法。
        _lag = Clipped.Into(channel, HudPlace.Stretch(art.HudBarFillLag, "hud/bar_fill_lag",
            NinePatchRect.AxisStretchMode.Tile), "Lag");
        var fillPiece = HudPlace.Stretch(fill, fillName, NinePatchRect.AxisStretchMode.Tile);
        _flowFrames = FlowFrames.Rightward(fillPiece);
        _flow = new GaugeFlow(motion.FlowPixelsPerSecond, _flowFrames.Count);
        _fill = Clipped.Into(channel, fillPiece, "Fill");
    }

    /// <summary>填充现在画了多长，界面像素。</summary>
    public int Length => _chase?.Fill ?? 0;

    /// <summary>浅色那一截现在画到哪，界面像素。与 <see cref="Length"/> 一样长就是没有那一截。</summary>
    public int LagLength => _chase?.Lag ?? 0;

    /// <summary>填充中段现在往右挪了几像素；0 是原图那样。</summary>
    public int FlowOffset => _flow.Offset;

    /// <summary>
    /// 按比例改长度。第一次直接画到那么长；之后回复时追上去，扣减时留一截。比例先钳，取整在 <see cref="HudLayout.GaugeFillLength"/> 里。
    /// </summary>
    public void Set(double ratio)
    {
        var length = HudLayout.GaugeFillLength(ratio, _width);
        if (_chase is null)
        {
            _chase = new GaugeChase(_chaseSettings, length);
        }
        else
        {
            _chase.Retarget(length);
        }

        ShowLengths(_chase);
    }

    public override void _Process(double delta)
    {
        // 停在数值上就不再每帧走：多数时候条是不动的。
        if (_chase is null || _chase.Settled)
        {
            SetProcess(false);
            return;
        }

        // 按这一帧开头的样子判：追到数值的那一帧也算在追，流满这一帧再停。
        if (_chase.Rising)
        {
            Flow(delta);
        }

        _chase.Advance(delta);
        ShowLengths(_chase);
    }

    private void ShowLengths(GaugeChase chase)
    {
        _lag.Show(chase.Lag);
        _fill.Show(chase.Fill);
        SetProcess(!chase.Settled);
    }

    // 不追的时候不调它，所以中段停在当时那一格、不跳回原图那样。
    private void Flow(double delta)
    {
        var before = _flow.Offset;
        _flow.Advance(delta);
        if (_flow.Offset != before)
        {
            _fill.Use(_flowFrames[_flow.Offset]);
        }
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

    /// <summary>一层按长度裁的框，里面一张中段平铺的九宫格件：框宽就是长度，件最窄照两头那么宽。</summary>
    private sealed class Clipped
    {
        private readonly Control _clip;
        private readonly NinePatchRect _piece;
        private readonly int _minWidth;

        private Clipped(Control clip, NinePatchRect piece)
        {
            _clip = clip;
            _piece = piece;
            _minWidth = piece.PatchMarginLeft + piece.PatchMarginRight;
        }

        /// <summary>在 <paramref name="parent"/> 里贴着放填充那一点加一层框，把 <paramref name="piece"/> 装进去。</summary>
        public static Clipped Into(Control parent, NinePatchRect piece, string name)
        {
            var clip = new Control
            {
                Name = $"{name}Length",
                ClipContents = true,
                MouseFilter = MouseFilterEnum.Ignore,
                OffsetLeft = HudLayout.GaugeFillInset,
                OffsetTop = HudLayout.GaugeFillInset,
                OffsetRight = HudLayout.GaugeFillInset,
                OffsetBottom = HudLayout.GaugeFillInset + HudLayout.GaugeFillHeight,
            };
            parent.AddChild(clip);

            piece.Name = name;
            piece.OffsetBottom = HudLayout.GaugeFillHeight;
            clip.AddChild(piece);
            return new Clipped(clip, piece);
        }

        /// <summary>画到这么长，界面像素。</summary>
        public void Show(int length)
        {
            _clip.OffsetRight = HudLayout.GaugeFillInset + length;
            _piece.OffsetRight = Math.Max(length, _minWidth);
        }

        /// <summary>换一张贴图：中段挪开的那几张之一，边距不变。</summary>
        public void Use(Texture2D texture) => _piece.Texture = texture;
    }
}
