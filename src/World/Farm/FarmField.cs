using Godot;
using Tinderhearth.Platform;
using Tinderhearth.Rules.Economy;
using Tinderhearth.Rules.UI;
using Tinderhearth.UI;

namespace Tinderhearth.World.Farm;

/// <summary>
/// 一片地：把 <see cref="Plot"/> 那台状态机接到瓦片层与作物精灵上（`GP-87` 的引擎侧那一半）。
/// </summary>
/// <remarks>
/// **规则一行都不在这里。** 状态、流转、浇水那两个量、荒置退化全在 <see cref="Plot"/>（规则层，
/// 不引用 Godot，有单元测试盯着），权威是设计仓 `design/地块系统.md`。本类只做三件引擎侧的事：
/// 把格坐标对上 <see cref="Plot"/> 实例、把玩家按下的那一下翻译成一次状态机调用、把状态画出来。
///
/// **一个键按当前格的状态派发，不是先选工具再用。** 六个状态各自恰好对应一个玩家动作
/// （未清理→清理、可耕→锄、已锄→播种、已播种→浇水、待收→收获、待清→清枯株），所以派发表
/// 是状态机形状的直接读法、不是一个简化。**代价写明**：这样试不到「对着可耕地按播种被拒」那条
/// 路径 —— 玩家没有办法主动选一个不该用的动作。那条拒绝路径由规则层单测守着
/// （`tests/Economy/PlotTests.cs`），实机要试到它得等工具栏（`GP-23` 与 `UI-13`）。
///
/// **播哪一种作物取随身栏选中那一格**（`GP-128`）。原先它是检查器里填死的一个标识，那个脚手架
/// 已经删掉 —— **不留两条路并存**。判在规则层（<see cref="Planting.TryPlant"/>），那三条被拒路径
/// （手上是空格、手上不是种子、这一格不许播）**各说得出是哪一种**。
///
/// **格子里先装什么仍然是脚手架**：现在由本类把已载入的作物按标识排序塞进随身栏
/// （<see cref="FillCarryBarWithSeeds"/>），而正式来源是背包（`GP-23`），换来源归 `UI-35`。
/// **换来源不改本类的任何行为** —— 它读的是「选中那一格是什么」，不是「那一格是从哪来的」。
///
/// 按 `ADR-0009`：节点树与全部参数值归作者，本类不自己建场景树、不留能用的默认值，缺一样当场
/// 报错说清缺谁。**作物精灵是例外，也只是表面上的例外** —— 它按格动态生成，容器节点仍然是作者
/// 搭的，本类只往那个容器里放实例。
/// </remarks>
public partial class FarmField : Node
{
    /// <summary>耕地那一层。**代码往它上面刷，作者不要手刷。**</summary>
    /// <remarks>
    /// 已耕地是**叠在天然地形之上的独立一层**（[场景绘制约定 · 地面分几层，判据是「谁决定它在哪」]）。
    /// 分两层不是为了好看：荒置退化时清掉这一层，底下那层自己就露出来了 —— 于是
    /// 「这格原来是什么」不用存，草地锄的退回草、裸土锄的退回裸土。
    ///
    /// 这一层走**四边拼接**（引擎内置地形模式里的 Match Sides，16 张），而天然地形那一层走双网格。
    /// 判据是「这一格是不是玩家单独操作的对象」，理由在那一份，本类不复述。
    /// </remarks>
    [Export]
    public TileMapLayer? TilledLayer { get; set; }

    /// <summary>
    /// 作物精灵放进哪个节点。**要接那个勾了 Y Sort Enabled 的节点本身，而且角色也要在它下面。**
    /// </summary>
    /// <remarks>
    /// 俯视的前后遮挡按脚底的纵向位置排（[场景绘制约定 · 前后遮挡按脚底位置排序]），引擎那一侧就是
    /// <c>y_sort_enabled</c>。
    ///
    /// **为什么不另给作物开一个子容器**：官方文档写明，父节点开了 Y 排序而某个子节点没开时，那个
    /// 子节点参与排序、但**它自己的孩子会按它那一个 y 位置整体渲染**。所以把作物塞进一个单独的
    /// <c>Node2D</c> 里，整片作物就会一起压在角色前面或后面 —— 而这**不报错**，只表现为「走到作物
    /// 后面却压在它上面」。让作物精灵与角色平级是唯一一层排序，行为不依赖嵌套语义。
    /// </remarks>
    [Export]
    public Node2D? CropsContainer { get; set; }

    /// <summary>当前操作格。**全部逐格动作只读它一个口。**</summary>
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

    /// <summary>
    /// 这一片地起始处在哪个状态。
    /// </summary>
    /// <remarks>
    /// **这不是一个兜底默认值，是一个真的要由场景回答的问题**：地图上哪几格是荒地、哪几格已经开垦
    /// 过，本来就是作者刷地图时决定的。<see cref="Plot"/> 只接受三个起步状态（未清理、可耕、已锄），
    /// 带作物的状态必须经过播种。
    ///
    /// 正典那句「开局基地上已有一小块已开垦的种植区」落在开局流程里、不落在这里 —— 那一小块填
    /// <see cref="PlotState.Tilled"/>，而基地其余地方填 <see cref="PlotState.Uncleared"/>。
    /// </remarks>
    [Export]
    public PlotState InitialState { get; set; } = PlotState.Uncleared;

    /// <summary>荒置退化天数：已锄的空格连着这么多天没作物就退回可耕。</summary>
    /// <remarks>
    /// **刻意没有默认值**，归[数值模型](设计仓 `design/数值模型.md`)的尚未给值表、跟 `GP-7` 走。
    /// 太短会罚出征的玩家，太长则荒置这条规则形同虚设 —— 所以它要实机试。
    /// <see cref="Plot.AdvanceDay"/> 自己会拒掉小于 1 的值。
    /// </remarks>
    [Export]
    public int FallowRevertDays { get; set; }

    /// <summary>浇满水时一次收几件。</summary>
    /// <remarks>三个收获量都没有默认值，理由同上；<see cref="HarvestYieldRule"/> 自己会校验它们的形式。</remarks>
    [Export]
    public int HarvestBaseCount { get; set; }

    /// <summary>一次没浇时的产量系数，**必须大于零** —— 一次没浇也不能颗粒无收。</summary>
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

    /// <summary>
    /// 湿耕地是第几种地形。
    /// </summary>
    /// <remarks>
    /// 干湿两套形状完全相同、只换色带（[场景绘制约定 · 耕地要干与湿两套]），但它们是两种地形、
    /// 各自做四边拼接：**湿的必须逐格看得出来**，否则玩家会重复浇或漏浇。所以一格湿的被干的围着时
    /// 它显示成「孤立一格」那一张湿的，这是对的、不是拼接错了。
    /// </remarks>
    [Export]
    public int WetTerrain { get; set; }

    /// <summary>各种作物那几张图。一种作物一份，靠标识与作物定义配对。</summary>
    [Export]
    public Godot.Collections.Array<CropArt> CropArts { get; set; } = [];

    /// <summary>随身栏那条六格。**播种取它选中那一格**（`GP-128`）。</summary>
    /// <remarks>
    /// 它替掉了原先那个在检查器里填死一个作物标识的脚手架 —— 那个属性已经删掉，**不留两条路并存**。
    ///
    /// 缺了当场报错：没有随身栏就没有「玩家手上是什么」这个读口，而那时播种只能回到写死一种作物，
    /// 也就是回到那个脚手架。
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

        // 框与实际作用的格必须落在同一套网格上。分开接两层之后它们可能差半格（双网格的显示层
        // 就是刻意偏了半格的），而那**不报错** —— 表现是框在这一格、锄的是旁边那一格。
        if (!ReferenceEquals(_cursor.GridLayer, _tilled))
        {
            throw new InvalidOperationException(
                $"{nameof(FarmField)}（节点 {Name}）接的耕地层是「{_tilled.Name}」，"
                    + $"而当前操作格算坐标用的是「{_cursor.GridLayer?.Name}」—— 两者必须是同一个节点。"
                    + "**尤其不要接双网格的显示层**，它刻意往左上偏了半格");
        }

        if (FieldSizeInCells.X < 1 || FieldSizeInCells.Y < 1)
        {
            throw new InvalidOperationException(
                $"{nameof(FarmField)}（节点 {Name}）的 {nameof(FieldSizeInCells)} 是 {FieldSizeInCells} —— "
                    + "两个方向都要至少 1 格，否则这一片地里一格都没有");
        }

        // 三个量的形式校验在 HarvestYieldRule 自己身上，所以这里构造一次就等于把它们验了。
        // 缺值（全 0）会在这一行当场抛，而不是等到玩家第一次收获时才发现。
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

    /// <summary>
    /// 调试键：<c>N</c> 推进一天。
    /// </summary>
    /// <remarks>
    /// **刻意不过 <c>InputMap</c>** —— 它是开发期的工具键、不是玩法绑定，混进去会让「玩家能重绑的键」
    /// 那份清单里多出一个他根本不该看见的条目（与 `src/World/TrainingRoom.cs` 那三个键同一条理由）。
    ///
    /// 正式的一天推进是睡觉那条路，走每日结算那串固定步序（`GP-56`，里程碑二）。**那一串步序一步
    /// 不增**是正典的硬约束，而地块的按天推进全部并进「作物生长与枯死结算」那一步之内。
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
    /// **换季判定要排在它之前**（正典的结算顺序里季节更替在作物生长之前），否则换季那天的作物会
    /// 先白长一天再枯死。季节本身还没有代码，所以那一步在里程碑二接上时是
    /// <see cref="Plot.ApplySeasonChange"/> 先跑一遍、再跑本方法。
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
            // 够不到时框已经画成不可用了，所以这里只需要不做事。打一行是给开发期看的 ——
            // 给玩家的提示归界面侧（`UI-27`），那一层还不存在。
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

    /// <summary>
    /// 播一次，种子取随身栏选中那一格（`GP-128`）。
    /// </summary>
    /// <remarks>
    /// **判在规则层**（<see cref="Planting.TryPlant"/>），本方法只把那三条被拒路径各打一行 ——
    /// 给玩家的提示归界面侧（`UI-27`），那一层还不存在。
    ///
    /// **三条原因必须分得开**，这是 `GP-128` 的承重项：合并成一句「不能播」之后玩家按下去没反应
    /// 而他不知道该换格、换东西还是先锄地。
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

    /// <summary>收一次。件数交给谁在设计里已经定了，而那条管道还不存在 —— 见下面注释。</summary>
    private bool Harvest(Plot plot)
    {
        var crop = CropOf(plot) ?? throw new InvalidOperationException(
            $"这一格是 {plot.State}、作物标识是 {plot.CropId}，但找不到那份作物定义");

        if (!plot.TryHarvest(crop, _yield, out var count))
        {
            return false;
        }

        // **件数到此为止。** 归属、付酬与入库一律走生产系统那条管道（`GP-28`），而它与背包
        // （`GP-23`）都还不存在，所以现在只打出来。**不要在这里顺手加一个容器** —— 那会让入库
        // 有第二个入口，而设计里那条管道是唯一的。
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
                $"{ModPaths.BaseContentRoot}/{CropDefinition.ContentDirectory}/ 下没有作物定义 —— "
                    + "至少要有一个 .json，字段表在设计仓 `design/地块系统.md`");
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

    /// <summary>
    /// 往随身栏里塞一份种子清单。**这是脚手架**，`UI-35` 会把它换成背包前六格的投影。
    /// </summary>
    /// <remarks>
    /// **它为什么还是脚手架**：真正的来源是背包（`GP-23`），而背包还不存在。但它与原先那个写死
    /// 一个作物标识的导出属性**形状完全不同** —— 那个属性让玩家在游戏里换不了作物，而这一份
    /// 只是「格子里先装什么」，换格、播种与被拒那几条路径都已经是正式的。
    ///
    /// 按标识排序是为了**可重复**：字典的枚举顺序不保证稳定，不排的话同一份内容在两次运行里
    /// 可能落在不同格子上，而那种不稳定查起来很贵。
    ///
    /// 作物多于六种时只装得下前六种，**少于六种时其余格子是空的** —— 空格是合法状态
    /// （<see cref="Tinderhearth.Rules.UI.CarrySlot.Empty"/>），不补假数据。
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

    /// <summary>
    /// 整片重画。
    /// </summary>
    /// <remarks>
    /// **整片而不是只改动过的那一格**：四边拼接要看邻居，改一格会连带改它四周那几张图，而
    /// 「哪几格要跟着重拼」算错了不报错、只表现为边缘拼错。一小块地的格数是几十，整片重刷不值得优化。
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
    /// 未清理与可耕都不画：可耕格就是普通的可建造格，把它画成犁开的土会让玩家把房子盖在犁沟上
    /// （这正是「锄是独立的一步」那条设计的理由）。
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
                // Centered = false：自己算位置，这样「脚底贴格底」是显式的，不依赖引擎怎么摆中心。
                sprite = new Sprite2D { Centered = false, Name = $"Crop{cell.X}_{cell.Y}" };
                _crops.AddChild(sprite);
                _cropSprites[cell] = sprite;
            }

            sprite.Texture = texture;
            sprite.Position = CropPositionIn(cell, tileSize, texture);
        }
    }

    /// <summary>
    /// 一株作物画在哪：横向居中，**底边贴格子底边**。
    /// </summary>
    /// <remarks>
    /// 贴底而不是居中，因为俯视的前后遮挡按脚底那一行排序，而作物是站在地块上的东西、不是地面层
    /// （[场景绘制约定 · 作物按它声明的阶段数交付]）。图比一格高时它往上长出去，这是对的 ——
    /// 受约束的是它占住几格，不是那张图多大。
    ///
    /// **最后取整一次**：像素图落在非整数位置上会被采样成错开一行或抖动，而那不报错。
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
            // 待收时 Stage 恰好等于成熟那一阶段的序号，所以两个状态读同一行。
            PlotState.Planted or PlotState.Harvestable => art.TextureForStage(plot.Stage),
            PlotState.Withered => art.WitheredTexture,
            _ => null,
        };
    }

    private InvalidOperationException Missing(string exportName, string what) =>
        new($"{nameof(FarmField)}（节点 {Name}）的 {exportName} 没接上 —— 它要的是{what}");
}
