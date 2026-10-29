using Godot;
using Tinderhearth.Rules.UI;

namespace Tinderhearth.UI;

/// <summary>
/// HUD 几块共用的那几样：照规则层的放置（<see cref="HudLayout.PlacementOf"/>）贴角、造画好的框、摆画好的件。
/// 关卡、基地与随身栏共用这一处。
/// </summary>
internal static class HudPlace
{
    /// <summary>
    /// 先贴角，再按放置往里挪那两段距离。锚点跟着视口走，逻辑宽度撑开时贴右的那几块自动跟着走。
    /// </summary>
    /// <remarks>
    /// 每次都从锚点预设重算一遍偏移再挪，所以尺寸一变就重摆也是幂等的。必须用
    /// <c>SetAnchorsAndOffsetsPreset</c>：实测对已经在树里的节点调 <c>SetAnchorsPreset</c>，引擎会改写偏移去
    /// 保住当前那个 0×0 的矩形，锚点对了尺寸还是 0×0，而且不报错。尺寸取的是这一块的最小尺寸，而块的
    /// 最小尺寸就是 <see cref="HudLayout.SizeOf"/>（块自己的 <c>CustomMinimumSize</c>）。
    /// </remarks>
    public static void Apply(Control block, HudPlacement placement)
    {
        var preset = placement.Anchor switch
        {
            HudAnchor.TopLeft => Control.LayoutPreset.TopLeft,
            HudAnchor.TopRight => Control.LayoutPreset.TopRight,
            HudAnchor.BottomLeft => Control.LayoutPreset.BottomLeft,
            HudAnchor.BottomRight => Control.LayoutPreset.BottomRight,
            _ => throw new ArgumentOutOfRangeException(nameof(placement), $"没有这个角：{placement.Anchor}"),
        };
        block.SetAnchorsAndOffsetsPreset(preset, Control.LayoutPresetMode.Minsize);

        var dx = placement.Anchor is HudAnchor.TopLeft or HudAnchor.BottomLeft ? placement.InsetX : -placement.InsetX;
        var dy = placement.Anchor is HudAnchor.TopLeft or HudAnchor.TopRight ? placement.InsetY : -placement.InsetY;
        block.OffsetLeft += dx;
        block.OffsetRight += dx;
        block.OffsetTop += dy;
        block.OffsetBottom += dy;
    }

    /// <summary>
    /// 造一块的底：作者画的框，外框尺寸取 <see cref="HudLayout.SizeOf"/>。
    /// </summary>
    /// <remarks>
    /// 那条横排格子坐在画好的那条栏上（<c>hud/actionbar_frame</c>，两头不拉伸、中间那几列横着拉开），其余几块坐在
    /// HUD 的细框上（<c>kit/frame_thin_dark</c>）。框的贴图边距读素材资源那一格，作者重画边框时在检查器里改；
    /// 字与格子离框多远是框边，按 <see cref="HudLayout.InsetOf"/> 给 —— 两样分开，改边框粗细不会顺带挪动里面的东西。
    /// </remarks>
    public static PanelContainer Frame(HudBlock block, UIContext context, UIArt art)
    {
        var size = HudLayout.SizeOf(block, context);
        var (insetX, insetY) = HudLayout.InsetOf(block);
        var piece = block == HudBlock.ActionBar ? art.HudActionbarFrame : art.KitFrameThinDark;

        var frame = new PanelContainer
        {
            Name = block.ToString(),
            CustomMinimumSize = new Vector2(size.Width, size.Height),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        frame.AddThemeStyleboxOverride("panel", PixelTheme.Box(piece, insetX, insetY));
        return frame;
    }

    /// <summary>
    /// 一张作者画的件，原尺寸摆、不吃鼠标。素材资源启动时核过，这里取不到就是没核。
    /// </summary>
    /// <remarks>不要碰 <c>TextureFilter</c>，项目级的最近邻过滤是像素清晰的唯一依靠。</remarks>
    public static TextureRect Picture(Texture2D? texture, string piece) => new()
    {
        Name = "Art",
        Texture = texture ?? throw new InvalidOperationException($"素材资源里少了「{piece}」—— 应当已在启动时核过"),
        StretchMode = TextureRect.StretchModeEnum.Keep,
        MouseFilter = Control.MouseFilterEnum.Ignore,
    };

    /// <summary>取一张作者画的件。素材资源启动时核过，这里取不到就是没核。</summary>
    public static Texture2D Piece(Texture2D? texture, string piece) =>
        texture ?? throw new InvalidOperationException($"素材资源里少了「{piece}」—— 应当已在启动时核过");
}
