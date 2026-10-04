using Godot;
using Tinderhearth.Platform;
using Tinderhearth.Rules.Economy;
using Tinderhearth.Rules.UI;
using Tinderhearth.UI;

namespace Tinderhearth.World.Farm;

/// <summary>
/// 一片地：把 <see cref="Plot"/> 那台状态机接到瓦片层和作物精灵上。
/// </summary>
/// <remarks>
/// 规则一行都不在这里。状态、流转、浇水、荒置退化全在 <see cref="Plot"/> 里（规则层，不引用
/// Godot，有单元测试盯着）。本类只做三件事：把格坐标对上 <see cref="Plot"/> 实例、把玩家按下的
/// 那一下翻译成一次状态机调用、把状态画出来。规则出自设计仓 design/地块系统.md。
///
/// 一个键按当前格的状态派发，不是先选工具再用。六个状态各自恰好对应一个动作：未清理→清理、
/// 可耕→锄、已锄→播种、已播种→浇水、待收→收获、待清→清枯株。代价是实机试不到「对着可耕地
/// 按播种被拒」那条路，那条由 tests/Economy/PlotTests.cs 守着。
///
/// 播哪一种作物取随身栏选中那一格，判定在 <see cref="Planting.TryPlant"/>。
/// 格子里先装什么还是临时做法，见 <see cref="FillCarryBarWithSeeds"/>。
/// </remarks>
public partial class FarmField : Node
{
    /// <summary>耕地那一层。代码往它上面刷，作者不要手刷。</summary>
    /// <remarks>
    /// 耕地是叠在天然地形之上的独立一层。分两层的好处是荒置退化时清掉这一层，底下那层自己就
    /// 露出来了，于是「这格原来是草还是裸土」不用存。
    ///
    /// 这一层走四边拼接，也就是引擎内置地形模式里的 Match Sides，16 张图。天然地形那一层走的是
    /// 双网格，两套不一样。分层依据见设计仓 production/场景绘制约定.md 的「地面分几层」一节。
    /// </remarks>
    [Export]
    public TileMapLayer? TilledLayer { get; set; }

    /// <summary>
    /// 作物精灵放进哪个节点。要接那个勾了 Y Sort Enabled 的节点本身，角色也得在它下面。
    /// </summary>
    /// <remarks>
    /// 俯视的前后遮挡按脚底的纵向位置排，引擎那一侧就是 <c>y_sort_enabled</c>。
    ///
    /// 不要另给作物开一个子容器：父节点开了 Y 排序而某个子节点没开时，那个子节点参与排序，但
    /// 它自己的孩子会按它那一个 y 位置整体渲染。所以把作物塞进一个单独的 <c>Node2D</c> 里，
    /// 整片作物会一起压在角色前面或后面，而引擎不报错，只表现为走到作物后面却压在它上面。
    /// </remarks>
    [Export]
    public Node2D? CropsContainer { get; set; }

    /// <summary>当前操作格。全部逐格动作都从它读要作用的格子。</summary>
    [Export]
    public FarmCellCursor? Cursor { get; set; }

    /// <summary>输入门面。</summary>
    [Export]
    public InputRouter? Router { get; set; }

    /// <summary>这一片地左上角那一格的格坐标。</summary>
    [Export]
    public Vector2I FieldOrigin { get; set; }

    /// <summary>这一片地有几格宽、几格高。两个方向都要至少 1。</summary>
    [Export]
    public Vector2I FieldSizeInCells { get; set; } = Vector2I.One;

    /// <summary>这一片地起始处在哪个状态。</summary>
    /// <remarks>
    /// 这不是兜底默认值，而是一个要由场景回答的问题：地图上哪几格是荒地、哪几格已经开垦过，
    /// 本来就是作者刷地图时决定的。
    ///
    /// <see cref="Plot"/> 只接受三个起步状态（未清理、可耕、已锄），带作物的状态必须经过播种。
    /// 开局那一小块已开垦的种植区填 <see cref="PlotState.Tilled"/>，基地其余地方填
    /// <see cref="PlotState.Uncleared"/>。
    /// </remarks>
    [Export]
    public PlotState InitialState { get; set; } = PlotState.Uncleared;

    /// <summary>荒置退化天数：已锄的空格连着这么多天没作物就退回可耕。</summary>
    /// <remarks>
    /// 没有默认值，必须在检查器里填，而且要实机试：太短会罚出远门的玩家，太长则荒置这条规则
    /// 形同虚设。<see cref="Plot.AdvanceDay"/> 自己会拒掉小于 1 的值。
    /// </remarks>
    [Export]
    public int FallowRevertDays { get; set; }

    /// <summary>浇满水时一次收几件。</summary>
    /// <remarks>
    /// 下面三个收获量都没有默认值，都要实机试。它们的形式校验在 <see cref="HarvestYieldRule"/> 里。
    /// </remarks>
    [Export]
    public int HarvestBaseCount { get; set; }

    /// <summary>一次没浇时的产量系数，必须大于零：一次没浇也不该颗粒无收。</summary>
    [Export]
    public double MinWaterFactor { get; set; }

    /// <summary>浇满时的产量系数，不得低于下限。</summary>
    [Export]
    public double MaxWaterFactor { get; set; }

    /// <summary>耕地那一层用的是 TileSet 里第几个地形集。</summary>
    [Export]
    public int TilledTerrainSet { get; set; }

    /// <summary>干耕地是那个地形集里的第几种地形。</summary>
    [Export]
    public int DryTerrain { get; set; }

    /// <summary>湿耕地是第几种地形。</summary>
    /// <remarks>
    /// 干湿两套图形状完全相同、只换色带，但它们是两种地形、各自做四边拼接。所以一格湿的被干的
    /// 围着时，它显示成「孤立一格」那一张湿的，这是对的，不是拼接错了。做成两种地形而不是整片
    /// 换色，是因为湿的必须逐格看得出来，否则玩家会重复浇或漏浇。
    /// </remarks>
    [Export]
    public int WetTerrain { get; set; }

    /// <summary>各种作物那几张图。一种作物一份，靠标识与作物定义配对。</summary>
    [Export]
    public Godot.Collections.Array<CropArt> CropArts { get; set; } = [];

    /// <summary>随身栏那条六格。播种时取它选中那一格里的东西。</summary>
    /// <remarks>
    /// 缺了当场报错。没有随身栏就没法知道玩家手上拿的是什么，那时播种只能退回写死一种作物。
    /// </remarks>
    [Export]
    public CarrySlotBarView? CarryBar { get; set; }

    private TileMapLayer _tilled = null!;
    private Node2D _crops = null!;
    private FarmCellCursor _cursor = null!;
    private InputRouter _router = null!;
    private CarrySlotBarView _carry = null!;
    private HarvestYieldRule _yield = null!;

    private readonly Dictionary<Vector2I, Plot> _plots = [];
    private readonly Dictionary<string, CropDefinition> _cropDefs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CropArt> _cropArts = new(StringComparer.Ordinal);
    private readonly Dictionary<Vector2I, Sprite2D> _cropSprites = [];

    public override void _Ready()
    {
        _tilled = TilledLayer ?? throw Missing(nameof(TilledLayer), "耕地那一层（叠在天然地形之上的 TileMapLayer）");
        _crops = CropsContainer ?? throw Missing(nameof(CropsContainer), "放作物精灵的那个节点");
        _cursor = Cursor ?? throw Missing(nameof(Cursor), $"当前操作格（挂了 {nameof(FarmCellCursor)} 的节点）");
        _router = Router ?? throw Missing(nameof(Router), $"输入门面（挂了 {nameof(InputRouter)} 的节点）");
        _carry = CarryBar ?? throw Missing(nameof(CarryBar), $"随身栏（挂了 {nameof(CarrySlotBarView)} 的节点）");

        if (_tilled.TileSet is null)
        {
            throw new InvalidOperationException(
                $"{nameof(FarmField)}（节点 {Name}）接的耕地层（{_tilled.Name}）还没有 TileSet");
        }

        // 框和实际作用的格必须落在同一套网格上。接成两层时它们可能差半格（双网格的显示层就是
        // 刻意偏半格的），引擎不报错，表现是框在这一格、锄的是旁边那一格。
        if (!ReferenceEquals(_cursor.GridLayer, _tilled))
        {
            throw new InvalidOperationException(
                $"{nameof(FarmField)}（节点 {Name}）接的耕地层是「{_tilled.Name}」，"
                    + $"而当前操作格算坐标用的是「{_cursor.GridLayer?.Name}」，两者必须是同一个节点。"
                    + "尤其不要接双网格的显示层，它往左上偏了半格");
        }

        if (FieldSizeInCells.X < 1 || FieldSizeInCells.Y < 1)
        {
            throw new InvalidOperationException(
                $"{nameof(FarmField)}（节点 {Name}）的 {nameof(FieldSizeInCells)} 是 {FieldSizeInCells} —— "
                    + "两个方向都要至少 1 格，否则这一片地里一格都没有");
        }

        // 三个收获量的校验在 HarvestYieldRule 的构造函数里，所以这里构造一次就等于把它们验了。
        // 没填（全 0）会在这一行当场抛，不用等玩家第一次收获才发现。
        _yield = new HarvestYieldRule(HarvestBaseCount, MinWaterFactor, MaxWaterFactor);

        LoadCropDefinitions();
        IndexCropArts();
        FillCarryBarWithSeeds();
        BuildPlots();
        RefreshAll();
    }

    public override void _Process(double delta)
    {
        if (_router.IsJustPressed(InputActions.Interact))
        {
            ActOnCurrentCell();
        }
    }

    /// <summary>开发期的调试键：按 <c>N</c> 推进一天。</summary>
    /// <remarks>
    /// 直接读按键、不走 <c>InputMap</c>，因为它是工具键不是玩法绑定。进了输入映射就会出现在
    /// 「玩家能重绑的键」那份清单里，而玩家根本不该看见它。TrainingRoom.cs 那三个调试键同理。
    ///
    /// 正式的一天推进是睡觉那条路，走每日结算的固定步序，还没有实现。
    /// </remarks>
    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Echo: false, Keycode: Key.N })
        {
            AdvanceDay();
        }
    }

    /// <summary>把这一片地推进一天，然后重画。</summary>
    /// <remarks>
    /// 以后接上季节时，<see cref="Plot.ApplySeasonChange"/> 要排在本方法之前跑。反了的话，换季
    /// 那天的作物会先白长一天再枯死。
    /// </remarks>
    public void AdvanceDay()
    {
        foreach (var plot in _plots.Values)
        {
            plot.AdvanceDay(CropOf(plot), FallowRevertDays);
        }

        RefreshAll();
    }

    /// <summary>按当前操作格那一格的状态派发一个动作。</summary>
    private void ActOnCurrentCell()
    {
        if (!_cursor.IsWithinReach)
        {
            // 够不到时框已经画成不可用的颜色了，所以这里什么都不用做。
            // 打这一行只是给开发期看的，给玩家的提示归界面层，那一层还不存在。
            GD.Print("[地块] 够不到 ", _cursor.Cell);
            return;
        }

        if (!_plots.TryGetValue(_cursor.Cell, out var plot))
        {
            GD.Print("[地块] ", _cursor.Cell, " 不在这一片地里");
            return;
        }

        var acted = plot.State switch
        {
            PlotState.Uncleared => plot.Clear(),
            PlotState.Cleared => plot.Till(),
            PlotState.Tilled => PlantFromHand(plot),
            PlotState.Planted => plot.Water(),
            PlotState.Harvestable => Harvest(plot),
            PlotState.Withered => plot.ClearWithered(),
            _ => false,
        };

        GD.Print("[地块] ", _cursor.Cell, " → ", plot.State, acted ? "" : "（这一下没有效果）");

        if (acted)
        {
            RefreshAll();
        }
    }

    /// <summary>播一次，种子取随身栏选中那一格。</summary>
    /// <remarks>
    /// 判定在 <see cref="Planting.TryPlant"/>，本方法只把被拒的三种情况各打一行。
    ///
    /// 三种原因要分得开（手上是空格、手上不是种子、这一格不许播）。合并成一句「不能播」之后，
    /// 玩家按下去没反应，而他不知道该换格子、换东西还是先锄地。
    /// </remarks>
    private bool PlantFromHand(Plot plot)
    {
        var hand = _carry.Bar.Selected;
        var result = Planting.TryPlant(
            plot,
            hand.IsEmpty ? null : hand.ItemId,
            id => _cropDefs.TryGetValue(id, out var crop) ? crop : null);

        if (result != PlantResult.Planted)
        {
            GD.Print("[地块] 播不了 ", _cursor.Cell, "：", result switch
            {
                PlantResult.NothingSelected => "手上第 " + (_carry.Bar.SelectedIndex + 1) + " 格是空的",
                PlantResult.NotASeed => $"手上拿的「{hand.ItemId}」不是种子",
                PlantResult.CellNotReady => $"这一格是 {plot.State}，不是已锄的地",
                _ => result.ToString(),
            });
        }

        return result == PlantResult.Planted;
    }

    /// <summary>收一次。收上来的东西往哪去还没有实现，见方法里的注释。</summary>
    private bool Harvest(Plot plot)
    {
        var crop = CropOf(plot) ?? throw new InvalidOperationException(
            $"这一格是 {plot.State}、作物标识是 {plot.CropId}，但找不到那份作物定义");

        if (!plot.TryHarvest(crop, _yield, out var count))
        {
            return false;
        }

        // 件数到此为止。归属、付酬、入库都走生产系统那条管道，而它和背包都还不存在，所以现在
        // 只打出来。不要在这里顺手加一个容器装它，那会让入库多出第二个入口。
        GD.Print("[地块] 收上来 ", count, " 件 ", crop.YieldItemId, "（还没有仓库，先只打出来）");
        return true;
    }

    /// <summary>这一格里那种作物的定义；空地为 <c>null</c>。</summary>
    private CropDefinition? CropOf(Plot plot) =>
        plot.CropId is string id && _cropDefs.TryGetValue(id, out var crop) ? crop : null;

    private void LoadCropDefinitions()
    {
        var catalog = ContentBootstrap.BuildCatalog();
        foreach (var entry in catalog.Resolve(CropDefinition.ContentDirectory).Values)
        {
            var crop = CropDefinition.Parse(entry.Text, entry.RelativePath);
            _cropDefs[crop.Id] = crop;
        }

        if (_cropDefs.Count == 0)
        {
            throw new InvalidOperationException(
                $"{ModPaths.BaseContentRoot}/{CropDefinition.ContentDirectory}/ 下没有作物定义，"
                    + "至少要有一个 .json。字段表在设计仓 design/地块系统.md");
        }
    }

    /// <summary>把素材按标识索引起来，并逐份核对它配不配得上那条作物定义。</summary>
    private void IndexCropArts()
    {
        foreach (var art in CropArts)
        {
            if (art is null)
            {
                throw new InvalidOperationException(
                    $"{nameof(FarmField)}（节点 {Name}）的 {nameof(CropArts)} 里有一项是空的");
            }

            if (!_cropDefs.TryGetValue(art.CropId, out var crop))
            {
                throw new InvalidOperationException(
                    $"素材配的作物「{art.CropId}」没有对应的作物定义 —— "
                        + $"已载入的有：{string.Join("、", _cropDefs.Keys)}");
            }

            art.RequireMatching(crop);
            _cropArts[art.CropId] = art;
        }

        var missing = _cropDefs.Keys.Where(id => !_cropArts.ContainsKey(id)).ToList();
        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                $"这几种作物有定义但没有素材：{string.Join("、", missing)} —— "
                    + $"每一种都要在 {nameof(CropArts)} 里配一份");
        }
    }

    /// <summary>往随身栏里塞一份种子清单。临时做法，以后换成背包前六格的投影。</summary>
    /// <remarks>
    /// 只有「格子里先装什么」是临时的。换格、播种、被拒那几条路径都已经是正式的，所以以后换
    /// 数据来源不用改本类的其它地方。
    ///
    /// 按标识排序是为了每次运行结果一样。字典的枚举顺序不保证稳定，不排的话同一份内容在两次
    /// 运行里可能落在不同格子上，这种不稳定查起来很贵。
    ///
    /// 作物多于六种时只装前六种；少于六种时其余格子留空，空格本身是合法状态，不补假数据。
    /// </remarks>
    private void FillCarryBarWithSeeds()
    {
        var seeds = _cropDefs.Keys
            .OrderBy(id => id, StringComparer.Ordinal)
            .Take(CarrySlotBar.SlotCount)
            .Select(id => new CarrySlot(id, 1))
            .ToList();

        while (seeds.Count < CarrySlotBar.SlotCount)
        {
            seeds.Add(CarrySlot.Empty);
        }

        _carry.Bar.Fill(seeds);
        _carry.OnContentChanged();
    }

    private void BuildPlots()
    {
        for (var x = 0; x < FieldSizeInCells.X; x++)
        {
            for (var y = 0; y < FieldSizeInCells.Y; y++)
            {
                _plots[FieldOrigin + new Vector2I(x, y)] = new Plot(InitialState);
            }
        }
    }

    /// <summary>整片重画。</summary>
    /// <remarks>
    /// 整片刷而不是只刷改动过的那一格，因为四边拼接要看邻居：改一格会连带改它四周那几张图，
    /// 而「哪几格要跟着重拼」算错了不报错，只表现为边缘拼错。一小块地才几十格，不值得优化。
    /// </remarks>
    private void RefreshAll()
    {
        RefreshTilledSoil();
        RefreshCropSprites();
    }

    private void RefreshTilledSoil()
    {
        var dry = new Godot.Collections.Array<Vector2I>();
        var wet = new Godot.Collections.Array<Vector2I>();

        foreach (var (cell, plot) in _plots)
        {
            if (!ShowsTilledSoil(plot.State))
            {
                continue;
            }

            (plot.WateredToday ? wet : dry).Add(cell);
        }

        _tilled.Clear();
        if (dry.Count > 0)
        {
            _tilled.SetCellsTerrainConnect(dry, TilledTerrainSet, DryTerrain);
        }

        if (wet.Count > 0)
        {
            _tilled.SetCellsTerrainConnect(wet, TilledTerrainSet, WetTerrain);
        }
    }

    /// <summary>哪几个状态要画出耕地那一层。</summary>
    /// <remarks>
    /// 未清理和可耕都不画。可耕格就是普通的可建造格，把它画成犁开的土会让玩家把房子盖在犁沟上。
    /// </remarks>
    private static bool ShowsTilledSoil(PlotState state) => state
        is PlotState.Tilled or PlotState.Planted or PlotState.Harvestable or PlotState.Withered;

    private void RefreshCropSprites()
    {
        Vector2I tileSize = _tilled.TileSet.TileSize;

        foreach (var (cell, plot) in _plots)
        {
            Texture2D? texture = TextureFor(plot);
            if (texture is null)
            {
                if (_cropSprites.Remove(cell, out var stale))
                {
                    stale.QueueFree();
                }

                continue;
            }

            if (!_cropSprites.TryGetValue(cell, out var sprite))
            {
                // Centered = false 是自己算位置，这样「底边贴格子底边」写在代码里看得见，
                // 不依赖引擎怎么摆中心点。
                sprite = new Sprite2D { Centered = false, Name = $"Crop{cell.X}_{cell.Y}" };
                _crops.AddChild(sprite);
                _cropSprites[cell] = sprite;
            }

            sprite.Texture = texture;
            sprite.Position = CropPositionIn(cell, tileSize, texture);
        }
    }

    /// <summary>一株作物画在哪：横向居中，底边贴格子底边。</summary>
    /// <remarks>
    /// 贴底而不是居中，因为俯视的前后遮挡按脚底那一行排序，而作物是站在地块上的东西、不是地面
    /// 的一部分。图比一格高时它往上长出格子，这是对的，受约束的是它占住几格、不是那张图多大。
    ///
    /// 最后取整一次。像素图落在非整数位置上会被采样成错开一行或者抖动，而引擎不报错。
    /// 作物图宽是奇数时居中会差半像素，这一次取整把它吃掉。
    /// </remarks>
    private Vector2 CropPositionIn(Vector2I cell, Vector2I tileSize, Texture2D texture)
    {
        Vector2 centerInField = _crops.ToLocal(_tilled.ToGlobal(_tilled.MapToLocal(cell)));
        var x = centerInField.X - (texture.GetWidth() / 2f);
        var y = centerInField.Y + (tileSize.Y / 2f) - texture.GetHeight();
        return new Vector2(Mathf.Round(x), Mathf.Round(y));
    }

    /// <summary>这一格现在该显示哪一张作物图；没有作物时为 <c>null</c>。</summary>
    private Texture2D? TextureFor(Plot plot)
    {
        if (plot.CropId is not string id || !_cropArts.TryGetValue(id, out var art))
        {
            return null;
        }

        return plot.State switch
        {
            // 待收时 Stage 正好等于成熟那一阶段的序号，所以这两个状态读同一行。
            PlotState.Planted or PlotState.Harvestable => art.TextureForStage(plot.Stage),
            PlotState.Withered => art.WitheredTexture,
            _ => null,
        };
    }

    private InvalidOperationException Missing(string exportName, string what) =>
        new($"{nameof(FarmField)}（节点 {Name}）的 {exportName} 没接上 —— 它要的是{what}");
}
