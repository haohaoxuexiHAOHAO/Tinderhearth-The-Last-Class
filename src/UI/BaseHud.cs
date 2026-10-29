using Godot;
using Tinderhearth.Rules.UI;

namespace Tinderhearth.UI;

/// <summary>
/// 基地 HUD 的屏幕空间部分：左下那一块，竖的一支精力加两行两列状态位。右下那条随身栏是另一个节点（<see cref="CarrySlotBarView"/>）。
/// </summary>
/// <remarks>
/// 与 <see cref="LevelHud"/> 同一条纪律：尺寸与放置从 <see cref="HudLayout"/> 取（场合是 <see cref="UIContext.Base"/>），
/// 显示的数值从 <see cref="BaseHudViewModel"/> 取，位置一律走锚点预设（<see cref="HudPlace"/>），除 0 与 1 之外不写数字字面量。
///
/// 那支精力条用的是作者画的两张：空管 <c>hud/vigor_tube_track</c>，液面 <c>hud/vigor_tube_fill</c> 叠在上面，两张都按中段平铺
/// 拉到这一块那么高。液面外面套一个贴着管子底边的裁剪框，框高按 <see cref="HudLayout.VigorShownHeight"/> 给，于是液面
/// 从下往上露出，不染色。液面中段一直往上挪，气泡跟着往上浮（<see cref="GaugeFlow"/>），挪多快由调用方给。
///
/// 竖管拉到与右边两行两列状态位一样高：照画布原样摆，这一块右边与下面会空着一大片，精力每变一点在管子里也看不清。
/// </remarks>
public sealed partial class BaseHud : Control
{
    private const UIContext Here = UIContext.Base;

    private readonly UIArt _art;
    private readonly GaugeMotionSettings _motion;

    private BaseHudViewModel _model;
    private Control? _root;
    private Control _shown = null!;
    private NinePatchRect _liquid = null!;
    private IReadOnlyList<Texture2D> _bubbleFrames = [];
    private GaugeFlow _bubbles = null!;

    /// <summary>装一份基地 HUD。视图模型与那几个动的量由调用方给，本类不挑数据；图从素材资源取。</summary>
    public BaseHud(BaseHudViewModel model, UIArt art, GaugeMotionSettings motion)
    {
        _model = model;
        _art = art;
        _motion = motion;
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

    /// <summary>液面中段现在往上挪了几像素；0 是原图那样。</summary>
    public int BubbleOffset => _bubbles.Offset;

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

    public override void _Process(double delta)
    {
        // 气泡一直浮，与精力涨不涨无关；只在挪满一像素时换图。
        var before = _bubbles.Offset;
        _bubbles.Advance(delta);
        if (_bubbles.Offset != before)
        {
            _liquid.Texture = _bubbleFrames[_bubbles.Offset];
        }
    }

    /// <summary>精力那一条。视图模型构造时已经核过基地只有这一条，这里再取不到就是那一处坏了。</summary>
    private HudGauge Vigor => _model.Gauges.Single(g => g.Kind == HudGaugeKind.DailyVigor);

    /// <summary>左边是标签加那支竖管，隔一个栅格是两行两列的状态位；竖管与状态位一样高。</summary>
    private void BuildResources()
    {
        var frame = HudPlace.Frame(HudBlock.Resources, Here, _art);
        AddChild(frame);
        _root = frame;

        // 一行排开：标签与竖管是一组（组内间距），隔一个栅格是两行两列的状态位。
        var row = new HBoxContainer { Name = "Content" };
        row.AddThemeConstantOverride("separation", UIMetrics.Grid);
        frame.AddChild(row);

        var gauge = new HBoxContainer { Name = HudGaugeKind.DailyVigor.ToString() };
        gauge.AddThemeConstantOverride("separation", UIMetrics.ItemGap);
        row.AddChild(gauge);

        // 标签比管子矮，靠 ShrinkCenter 在那一段里居中 —— 不用纵向偏移，就不会有半格。
        var label = HudPlace.Picture(_art.HudGaugeIconVigor, "hud/gauge_icon_vigor");
        label.Name = "Label";
        label.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        gauge.AddChild(label);

        var tube = new Control
        {
            Name = "Tube",
            CustomMinimumSize = new Vector2(HudLayout.VigorTubeWidth, HudLayout.VigorTubeHeight),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        gauge.AddChild(tube);

        // 管子比画布高：中段按行平铺，平铺那一段是素材资源那一格的九宫格边距。
        var track = HudPlace.Stretch(_art.HudVigorTubeTrack, "hud/vigor_tube_track", NinePatchRect.AxisStretchMode.Tile);
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

        // 液面同样中段按行平铺，气泡跟着一轮一轮重复；它与空管一样高，上下那几行对得齐。
        var liquid = HudPlace.Stretch(_art.HudVigorTubeFill, "hud/vigor_tube_fill", NinePatchRect.AxisStretchMode.Tile);
        liquid.Name = "Liquid";
        liquid.AnchorRight = 1;
        liquid.AnchorTop = 1;
        liquid.AnchorBottom = 1;
        liquid.OffsetTop = -HudLayout.VigorTubeHeight;
        _shown.AddChild(liquid);
        _liquid = liquid;
        _bubbleFrames = FlowFrames.Upward(liquid);
        _bubbles = new GaugeFlow(_motion.BubblePixelsPerSecond, _bubbleFrames.Count);

        // 状态位只留位置：里面挂什么跟状态系统走，界面侧不新增读口。
        row.AddChild(new Control
        {
            Name = "Status",
            CustomMinimumSize = new Vector2(HudLayout.BaseStatusGridWidth, HudLayout.BaseStatusGridHeight),
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
}
