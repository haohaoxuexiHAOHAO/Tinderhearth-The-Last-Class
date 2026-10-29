using Godot;
using Tinderhearth.Rules.UI;

namespace Tinderhearth.UI;

/// <summary>把一块 HUD 照规则层的放置（<see cref="HudLayout.PlacementOf"/>）贴到屏幕上。关卡、基地与随身栏共用这一处。</summary>
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
    /// 造一块的面板底：描边加框边，外框尺寸取 <see cref="HudLayout.SizeOf"/>。
    /// </summary>
    /// <remarks>
    /// 还是代码画的纯色框，换成作者画的细框与那条栏跟着设计仓台账 <c>UI-44</c> 的接线走。抗锯齿必须关掉
    /// —— 抗锯齿会造出半透明边，见设计仓 production/像素绘制原则.md 的「硬边、抗锯齿与点绘」一节。
    /// 框边按 <see cref="HudLayout.InsetOf"/> 给，那条横排格子的框边是画好的栏的两头与上下，不是面板内边距。
    /// </remarks>
    public static PanelContainer Frame(HudBlock block, UIContext context)
    {
        var size = HudLayout.SizeOf(block, context);
        var (insetX, insetY) = HudLayout.InsetOf(block);
        var style = new StyleBoxFlat
        {
            BgColor = PixelTheme.ToColor(HudPalette.Panel),
            BorderColor = PixelTheme.ToColor(HudPalette.Edge),
            AntiAliasing = false,
            ContentMarginLeft = insetX,
            ContentMarginRight = insetX,
            ContentMarginTop = insetY,
            ContentMarginBottom = insetY,
        };
        style.SetBorderWidthAll(1);

        var frame = new PanelContainer
        {
            Name = block.ToString(),
            CustomMinimumSize = new Vector2(size.Width, size.Height),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        frame.AddThemeStyleboxOverride("panel", style);
        return frame;
    }
}
