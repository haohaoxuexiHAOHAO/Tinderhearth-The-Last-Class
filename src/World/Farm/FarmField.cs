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
/// Godot，有单元测试盯着），那片地本身在规则层的世界状态里（<see cref="WorldState"/>）。本类只做三件事：
/// 把瓦片层的格坐标对上那片地的一格、把玩家按下的那一下翻译成一次状态机调用、把状态画出来。
/// 规则出自设计仓 design/地块系统.md。
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

    /// <summary>作物长成那一下掷变异的基础值，全库一个。</summary>
    /// <remarks>
    /// 这两个变异几率的量都没有默认值，要实机种过几季才定得下来。形式校验在 <see cref="MutationChanceRule"/> 里：
    /// 基础值必须大于零、不大于上限，上限必须小于 1。
    /// </remarks>
    [Export]
    public double MutationBaseChance { get; set; }

    /// <summary>加了事件与设备的修饰之后，变异几率最多到多少。必须小于 1。</summary>
    [Export]
    public double MutationChanceCap { get; set; }

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
    private MutationChanceRule _mutation = null!;
    private int _seedBatch;
    private WorldState _world = null!;

    private readonly Dictionary<string, CropDefinition> _cropDefs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CropArt> _cropArts = new(StringComparer.Ordinal);
    private readonly Dictionary<Vector2I, Sprite2D> _cropSprites = [];

    public override void _Ready()
    {
        // 先停掉每帧回调，全部校验过了再开。_Ready 抛出之后引擎并不会停掉这个节点，_Process
        // 照旧每帧跑而下面那几个字段还是 null —— 于是说清缺了什么的那一条会被成百条空引用刷走。
        SetProcess(false);

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

        // 两个变异几率的量同理，没填（全 0）也在这一行当场抛。
        _mutation = new MutationChanceRule(MutationBaseChance, MutationChanceCap);

        LoadCropDefinitions();
        IndexCropArts();
        FillCarryBarWithSeeds();
        NewWorld();
        RefreshAll();

        SetProcess(true);
    }

    public override void _Process(double delta)
    {
        if (_router.IsJustPressed(InputActions.Interact))
        {
            ActOnCurrentCell();
        }
    }

    /// <summary>开发期的调试键：按 <c>N</c> 推进一天，按 <c>B</c> 换下一批种子。</summary>
    /// <remarks>
    /// 直接读按键、不走 <c>InputMap</c>，因为它们是工具键不是玩法绑定。进了输入映射就会出现在
    /// 「玩家能重绑的键」那份清单里，而玩家根本不该看见它们。TrainingRoom.cs 那三个调试键同理。
    ///
    /// 正式的一天推进是睡觉那条路，走每日结算的固定步序，还没有实现。
    /// </remarks>
    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true, Echo: false } key)
        {
            return;
        }

        switch (key.Keycode)
        {
            case Key.N: AdvanceDay(); break;
            case Key.B: NextSeedBatch(); break;
        }
    }

    /// <summary>开发期的调试键：按 <c>B</c> 把随身栏换成下一批种子，最后一批之后回到第一批。</summary>
    /// <remarks>
    /// 作物比随身栏的格数多，不换批就种不到后面那几种。它与 <c>N</c> 一样直接读按键、不进输入映射，
    /// 理由同上；换的是格子里装什么，选中第几格不变。
    /// </remarks>
    private void NextSeedBatch()
    {
        _seedBatch = SeedBatches.Next(_seedBatch, _cropDefs.Count);
        FillCarryBarWithSeeds();
        GD.Print("[地块] 随身栏换成第 ", _seedBatch + 1, " 批种子，共 ", SeedBatches.CountFor(_cropDefs.Count), " 批");
    }

    /// <summary>把这一片地推进一天，然后重画。这一晚长成时变异的每一格各打一行。</summary>
    /// <remarks>
    /// 以后接上季节时，<see cref="Plot.ApplySeasonChange"/> 要排在本方法之前跑。反了的话，换季
    /// 那天的作物会先白长一天再枯死。
    ///
    /// 变异那几行是睡后简报那一项的替身：简报还没有，归设计仓台账 GP-56。
    /// </remarks>
    public void AdvanceDay()
    {
        foreach (var (cell, plot) in _world.Plots.Cells)
        {
            var wasMutated = plot.Mutated;
            plot.AdvanceDay(CropOf(plot), FallowRevertDays, RollMutation);
            if (plot.Mutated && !wasMutated)
            {
                GD.Print("[地块] ", cell, " 的 ", plot.CropId, " 长成时变异了");
            }
        }

        RefreshAll();
    }

    /// <summary>掷一次变异，用系统随机数。几率读的是这一刻作用在地上的修饰。</summary>
    /// <remarks>
    /// 事件与设备那两路修饰还没有来源，归设计仓台账 GP-52 与 GP-28，所以现在交进去的是空的，几率就是基础值。
    /// </remarks>
    private bool RollMutation() => Random.Shared.NextDouble() < _mutation.ChanceWith([]);

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

        if (!_world.Plots.TryGet(ToGridCell(_cursor.Cell), out var plot))
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

        if (!plot.TryHarvest(crop, _yield, out var harvest))
        {
            return false;
        }

        // 收获结果到此为止。归属、付酬、入库都走生产系统那条管道，而它和背包都还不存在，所以现在
        // 只打出来。不要在这里顺手加一个容器装它，那会让入库多出第二个入口。
        GD.Print("[地块] 收上来 ", harvest.Count, " 件 ", crop.YieldItemId, harvest.Mutated ? "（变异）" : "",
                 "（还没有仓库，先只打出来）");
        return true;
    }

    /// <summary>这一格里那种作物的定义；空地为 <c>null</c>。</summary>
    private CropDefinition? CropOf(Plot plot) =>
        plot.CropId is string id && _cropDefs.TryGetValue(id, out var crop) ? crop : null;

    /// <summary>读全部作物定义。怎么读、什么算坏与启动时那一次核对是同一处（<see cref="CropDefinition.LoadAll"/>）。</summary>
    private void LoadCropDefinitions()
    {
        foreach (var (id, crop) in CropDefinition.LoadAll(ContentBootstrap.BuildCatalog()))
        {
            _cropDefs[id] = crop;
        }
    }

    /// <summary>核全部作物素材，再按标识索引起来。有一处不对就抛，全部问题一次列出来。</summary>
    /// <remarks>判在规则层（<see cref="CropArtCheck"/>），这里只把每一份配了什么交过去。</remarks>
    private void IndexCropArts()
    {
        var problems = CropArtCheck.Problems(_cropDefs.Values, [.. CropArts.Select(art => art?.ToEntry())]);
        if (problems.Count > 0)
        {
            throw new InvalidOperationException(
                $"{nameof(FarmField)}（节点 {Name}）的作物素材有 {problems.Count} 处不对：\n  " + string.Join("\n  ", problems));
        }

        foreach (var art in CropArts)
        {
            _cropArts[art.CropId] = art;
        }
    }

    /// <summary>往随身栏里塞当前那一批种子。临时做法，以后换成背包前几格的投影。</summary>
    /// <remarks>
    /// 只有「格子里先装什么」是临时的。换格、播种、被拒那几条路径都已经是正式的，所以以后换
    /// 数据来源不用改本类的其它地方。怎么排、怎么分批在 <see cref="SeedBatches"/>。
    /// </remarks>
    private void FillCarryBarWithSeeds()
    {
        var seeds = SeedBatches.Batch(_cropDefs.Keys, _seedBatch);

        // 种子还没有图标：物品定义和它上面图标那一格都还不存在（设计仓台账 GP-22），所以格子先只
        // 画框。到那时这里换成按标识查那一格 —— 装东西的和给图标的仍是同一处，不另开一张表。
        _carry.Icons = static _ => null;
        _carry.Bar.Fill(seeds);
        _carry.OnContentChanged();
    }

    /// <summary>开新档：日期是开局那一天，地按检查器里那几格铺出来。</summary>
    private void NewWorld() => _world = WorldState.NewGame(
        PlotGrid.Rectangle(ToGridCell(FieldOrigin), FieldSizeInCells.X, FieldSizeInCells.Y, InitialState));

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

        foreach (var (cell, plot) in _world.Plots.Cells)
        {
            if (!ShowsTilledSoil(plot.State))
            {
                continue;
            }

            (plot.WateredToday ? wet : dry).Add(ToTileCell(cell));
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

        foreach (var (gridCell, plot) in _world.Plots.Cells)
        {
            Vector2I cell = ToTileCell(gridCell);
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
                // Centered = false 是自己算图往哪画，这样「原点在接地那一行、图往上画」写在代码里看得见，
                // 不依赖引擎怎么摆中心点。
                sprite = new Sprite2D { Centered = false, Name = $"Crop{cell.X}_{cell.Y}" };
                _crops.AddChild(sprite);
                _cropSprites[cell] = sprite;
            }

            sprite.Texture = texture;
            sprite.Position = CropOriginIn(cell, tileSize);
            sprite.Offset = DrawnUpFromOrigin(texture);
        }
    }

    /// <summary>一株作物的原点：格子底边中点，取整。</summary>
    /// <remarks>
    /// Y 排序比的是节点位置、不看图画在哪，所以原点要落在作物接地的那一行，与角色的脚底原点同一个口径
    /// （设计仓 production/场景绘制约定.md 的「前后遮挡按脚底位置排序」一节）。原点放在图的左上角时，
    /// 人站进那一格的上半截，脚底比原点靠下，就压到了作物上面。
    ///
    /// 取整是因为像素图落在非整数位置上会被采样成错开一行或者抖动，而引擎不报错。
    /// </remarks>
    private Vector2 CropOriginIn(Vector2I cell, Vector2I tileSize)
    {
        Vector2 centerInField = _crops.ToLocal(_tilled.ToGlobal(_tilled.MapToLocal(cell)));
        return new Vector2(Mathf.Round(centerInField.X), Mathf.Round(centerInField.Y + (tileSize.Y / 2f)));
    }

    /// <summary>图相对原点画在哪：横向居中、底边贴着原点，往上画。</summary>
    /// <remarks>
    /// 图比一格高时它往上长出格子，这是对的，受约束的是它占住几格、不是那张图多大。图宽是奇数时居中会差
    /// 半像素，向下取整把它吃掉，图始终落在整像素上。
    /// </remarks>
    private static Vector2 DrawnUpFromOrigin(Texture2D texture) =>
        new(-Mathf.Floor(texture.GetWidth() / 2f), -texture.GetHeight());

    /// <summary>这一格现在该显示哪一张作物图；没有作物时为 <c>null</c>。</summary>
    private Texture2D? TextureFor(Plot plot)
    {
        if (plot.CropId is not string id || !_cropArts.TryGetValue(id, out var art))
        {
            return null;
        }

        return plot.State switch
        {
            // 变异那一株替成熟那一张；那一位只在待收时为真，所以只有这一行读它。
            PlotState.Harvestable when plot.Mutated => art.MutatedRipeTexture,
            // 待收时 Stage 正好等于成熟那一阶段的序号，所以这两个状态读同一行。
            PlotState.Planted or PlotState.Harvestable => art.TextureForStage(plot.Stage),
            PlotState.Withered => art.WitheredTexture,
            _ => null,
        };
    }

    private InvalidOperationException Missing(string exportName, string what) =>
        new($"{nameof(FarmField)}（节点 {Name}）的 {exportName} 没接上 —— 它要的是{what}");

    /// <summary>瓦片层的格坐标换成规则层那一片地的格坐标。两边一格对一格，只是类型不同。</summary>
    private static GridCell ToGridCell(Vector2I cell) => new(cell.X, cell.Y);

    /// <summary>规则层那一片地的一格换回瓦片层的格坐标。</summary>
    private static Vector2I ToTileCell(GridCell cell) => new(cell.X, cell.Y);
}
