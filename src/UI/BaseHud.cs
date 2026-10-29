using Godot;
using Tinderhearth.Rules.UI;

namespace Tinderhearth.UI;

/// <summary>
/// 基地 HUD 的屏幕空间部分：左下那一块，竖的一支精力加一行状态位。右下那条随身栏是另一个节点（<see cref="CarrySlotBarView"/>）。
/// </summary>
/// <remarks>
/// 与 <see cref="LevelHud"/> 同一条纪律：尺寸与放置从 <see cref="HudLayout"/> 取（场合是 <see cref="UIContext.Base"/>），
/// 显示的数值从 <see cref="BaseHudViewModel"/> 取，位置一律走锚点预设（<see cref="HudPlace"/>），除 0 与 1 之外不写数字字面量。
///
/// 那支精力条用的是作者画的两张：空管 <c>hud/vigor_tube_track</c>，液面 <c>hud/vigor_tube_fill</c> 叠在上面。液面外面套一个
/// 贴着管子底边的裁剪框，框高按 <see cref="HudLayout.VigorShownHeight"/> 给，于是液面从下往上露出，不拉伸、不染色。
///
/// 这一块怎么排（标签图标与管子在上、状态位在下，与关卡那一块同宽）没有参考图，先照这一种排，实机看了不合适再改。
/// </remarks>
public sealed partial class BaseHud : Control
{
    private const UIContext Here = UIContext.Base;

    private readonly UIArt _art;

    private BaseHudViewModel _model;
    private Control? _root;
    private Control _shown = null!;

    /// <summary>装一份基地 HUD。视图模型由调用方给，本类不挑数据；图从素材资源取。</summary>
    public BaseHud(BaseHudViewModel model, UIArt art)
    {
        _model = model;
        _art = art;
    }

    /// <summary>当前视图模型。换一份就刷新。</summary>
    public BaseHudViewModel Model
    {
        get => _model;
        set
        {
            _model = value;
            if (_root is not null)
            {
                Refresh();
            }
        }
    }

    public override void _Ready()
    {
        // 铺满所属层，块靠锚点贴角；不吃鼠标。两条坑与 LevelHud 那两段注释同一个：必须用 SetAnchorsAndOffsetsPreset，
        // 块要等根节点尺寸定下来再摆、尺寸每变一次重摆一遍。
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;

        BuildResources();

        Resized += AnchorBlock;
        AnchorBlock();
        Refresh();
    }

    public override void _ExitTree() => Resized -= AnchorBlock;

    /// <summary>精力那一条。视图模型构造时已经核过基地只有这一条，这里再取不到就是那一处坏了。</summary>
    private HudGauge Vigor => _model.Gauges.Single(g => g.Kind == HudGaugeKind.DailyVigor);

    /// <summary>上面一段是标签图标加那支管子，下面一行留给状态位。行距为零，与关卡那一块同一个排法。</summary>
    private void BuildResources()
    {
        var frame = HudPlace.Frame(HudBlock.Resources, Here);
        AddChild(frame);
        _root = frame;

        var column = new VBoxContainer { Name = "Content" };
        column.AddThemeConstantOverride("separation", 0);
        frame.AddChild(column);

        var top = new HBoxContainer
        {
            Name = HudGaugeKind.DailyVigor.ToString(),
            CustomMinimumSize = new Vector2(HudLayout.ResourcesContentWidth, HudLayout.VigorTubeHeight),
        };
        top.AddThemeConstantOverride("separation", UIMetrics.ItemGap);
        column.AddChild(top);

        // 标签比管子矮，靠 ShrinkCenter 在那一段里居中 —— 不用纵向偏移，就不会有半格。
        var label = Piece(_art.HudGaugeIconVigor, "hud/gauge_icon_vigor");
        label.Name = "Label";
        label.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        top.AddChild(label);

        var tube = new Control
        {
            Name = "Tube",
            CustomMinimumSize = new Vector2(HudLayout.VigorTubeWidth, HudLayout.VigorTubeHeight),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        top.AddChild(tube);

        var track = Piece(_art.HudVigorTubeTrack, "hud/vigor_tube_track");
        track.Name = "Track";
        track.SetAnchorsPreset(LayoutPreset.FullRect);
        tube.AddChild(track);

        // 裁剪框四个锚点钉在管子的宽与底边上，高度靠上偏移给；液面在框里也贴底、整张那么高，
        // 于是框每高一像素，液面就从下往上多露一行。锚点要在挂进树之前设：那时设不会改写偏移。
        _shown = new Control
        {
            Name = "Shown",
            ClipContents = true,
            MouseFilter = MouseFilterEnum.Ignore,
            AnchorRight = 1,
            AnchorTop = 1,
            AnchorBottom = 1,
        };
        tube.AddChild(_shown);

        var liquid = Piece(_art.HudVigorTubeFill, "hud/vigor_tube_fill");
        liquid.Name = "Liquid";
        liquid.AnchorRight = 1;
        liquid.AnchorTop = 1;
        liquid.AnchorBottom = 1;
        liquid.OffsetTop = -HudLayout.VigorTubeHeight;
        _shown.AddChild(liquid);

        // 状态位那一行只留位置：里面挂什么跟状态系统走，界面侧不新增读口。
        column.AddChild(new Control
        {
            Name = "Status",
            CustomMinimumSize = new Vector2(HudLayout.ResourcesContentWidth, HudLayout.StatusRowHeight),
            MouseFilter = MouseFilterEnum.Ignore,
        });
    }

    /// <summary>照基地那一套把那一块贴到左下角。放置从规则层取，这里没有一个坐标。</summary>
    private void AnchorBlock()
    {
        if (_root is { } root)
        {
            HudPlace.Apply(root, HudLayout.PlacementOf(HudBlock.Resources, Here));
        }
    }

    /// <summary>把视图模型画上去：裁剪框往上长到该露的高度。</summary>
    private void Refresh() => _shown.OffsetTop = -HudLayout.VigorShownHeight(Vigor.Ratio);

    /// <summary>一张作者画的件，原尺寸摆。不要碰 <c>TextureFilter</c>，项目级的最近邻过滤是像素清晰的唯一依靠。</summary>
    private static TextureRect Piece(Texture2D? texture, string piece) => new()
    {
        Texture = texture
            ?? throw new InvalidOperationException($"素材资源里少了「{piece}」—— 应当已在启动时核过"),
        StretchMode = TextureRect.StretchModeEnum.Keep,
        MouseFilter = MouseFilterEnum.Ignore,
    };
}
