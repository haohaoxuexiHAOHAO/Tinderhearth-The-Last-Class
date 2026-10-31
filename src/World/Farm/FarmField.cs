using Godot;
using Tinderhearth.Platform;
using Tinderhearth.Rules.Economy;
using Tinderhearth.Rules.Foundation.Content;
using Tinderhearth.Rules.Foundation.Save;
using Tinderhearth.Rules.Foundation.Text;
using Tinderhearth.Rules.Progression;
using Tinderhearth.Rules.UI;
using Tinderhearth.UI;

namespace Tinderhearth.World.Farm;

/// <summary>
/// 一片地：把 <see cref="Plot"/> 那台状态机接到瓦片层和作物精灵上。
/// </summary>
/// <remarks>
/// 规则一行都不在这里。状态、流转、浇水、荒置退化全在 <see cref="Plot"/> 里（规则层，不引用
/// Godot，有单元测试盯着），那片地本身在规则层的世界状态里（<see cref="WorldState"/>）。本类只把引擎那一侧接到
/// 规则上：把瓦片层的格坐标对上那片地的一格；把玩家按下的那一下翻译成一次状态机调用，或者站在床边时的一次睡觉；
/// 把状态画出来；读回与写下存档那个文件。规则出自设计仓 design/地块系统.md。
///
/// 一个键按当前格的状态派发，不是先选工具再用。六个状态各自恰好对应一个动作：未清理→清理、
/// 可耕→锄、已锄→播种、已播种→浇水、待收→收获、待清→清枯株。代价是实机试不到「对着可耕地
/// 按播种被拒」那条路，那条由 tests/Economy/PlotTests.cs 守着。
///
/// 播哪一种作物取随身栏选中那一格，随身栏是背包前几格的投影；判定与扣种子在 <see cref="Planting.TryPlantFromBag"/>。
/// 还没有商店，种子由开发期调试键往背包里放（<see cref="GiveSeedBatch"/>）。
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
    /// 形同虚设。小于 1（没填就是 0）时启动当场报错。
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

    /// <summary>每个物品的图标。一个物品一份，靠标识与物品定义配对；在册的每一个都要有。</summary>
    /// <remarks>
    /// 缺了、重了、变异那一格没写，农场启动时一次全报（<see cref="ItemIconCheck"/>）。随身栏的格子画的就是它。
    /// </remarks>
    [Export]
    public Godot.Collections.Array<ItemIcon> ItemIcons { get; set; } = [];

    /// <summary>随身栏那条六格。播种时取它选中那一格里的东西。</summary>
    /// <remarks>
    /// 缺了当场报错。没有随身栏就没法知道玩家手上拿的是什么，那时播种只能退回写死一种作物。
    /// </remarks>
    [Export]
    public CarrySlotBarView? CarryBar { get; set; }

    /// <summary>即时提示那一行。一个动作被拒时，屏上由它说清是哪一种。</summary>
    /// <remarks>缺了当场报错：没有它，被拒的那一下在屏上就是没反应，而玩家会把没反应当成坏了。</remarks>
    [Export]
    public InstantNoticeView? Notice { get; set; }

    private TileMapLayer _tilled = null!;
    private Node2D _crops = null!;
    private FarmCellCursor _cursor = null!;
    private InputRouter _router = null!;
    private CarrySlotBarView _carry = null!;
    private InstantNoticeView _notice = null!;
    private HarvestYieldRule _yield = null!;
    private MutationChanceRule _mutation = null!;
    private int _seedBatch;
    private CurrentWorld _world = null!;
    private DailySettlement _settlement = null!;
    private SaveShardRegistry _registry = null!;
    private SaveStore _store = null!;
    private TextCatalog _text = TextCatalog.Empty;
    private ItemCatalog _items = null!;

    /// <summary>开新档时的背包：空的，第一级的格数。存档默认值与读档时的格数上限都从它来。</summary>
    private Bag _newGameBag = null!;
    private readonly List<Bed> _beds = [];
    private SleepPresentation? _wake;

    private readonly Dictionary<string, CropDefinition> _cropDefs = new(StringComparer.Ordinal);

    /// <summary>按种子那一样物品找作物：手上拿的是哪一份种子，播下去就是哪一种。</summary>
    private IReadOnlyDictionary<string, CropDefinition> _cropsBySeed = null!;
    private readonly Dictionary<string, CropArt> _cropArts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ItemIcon> _itemIcons = new(StringComparer.Ordinal);
    private readonly Dictionary<Vector2I, Sprite2D> _cropSprites = [];

    public override void _Ready()
    {
        // 先停掉每帧回调与调试键，全部校验过了、存档也读回来了再开。_Ready 抛出之后引擎并不会停掉这个节点，
        // _Process 照旧每帧跑而下面那几个字段还是 null —— 于是说清缺了什么的那一条会被成百条空引用刷走；
        // 存档读不出来之后再按推进一天那个调试键，也只会在那条报错下面多刷一条空引用。
        SetProcess(false);
        SetProcessUnhandledKeyInput(false);

        _tilled = TilledLayer ?? throw Missing(nameof(TilledLayer), "耕地那一层（叠在天然地形之上的 TileMapLayer）");
        _crops = CropsContainer ?? throw Missing(nameof(CropsContainer), "放作物精灵的那个节点");
        _cursor = Cursor ?? throw Missing(nameof(Cursor), $"当前操作格（挂了 {nameof(FarmCellCursor)} 的节点）");
        _router = Router ?? throw Missing(nameof(Router), $"输入门面（挂了 {nameof(InputRouter)} 的节点）");
        _carry = CarryBar ?? throw Missing(nameof(CarryBar), $"随身栏（挂了 {nameof(CarrySlotBarView)} 的节点）");
        _notice = Notice ?? throw Missing(nameof(Notice), $"即时提示那一行（挂了 {nameof(InstantNoticeView)} 的节点）");

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

        if (FallowRevertDays < 1)
        {
            throw new InvalidOperationException(
                $"{nameof(FarmField)}（节点 {Name}）的 {nameof(FallowRevertDays)} 是 {FallowRevertDays} —— "
                    + "荒置退化天数至少 1 天，要在检查器里填");
        }

        // 作物要指到在册的物品，所以物品先读；读法与主场景是同一处（ContentBootstrap.LoadItems）。
        var catalog = ContentBootstrap.BuildCatalog();
        _text = ContentBootstrap.LoadText(catalog);
        _items = ContentBootstrap.LoadItems(catalog, _text);
        Harvesting.RequireQualitiesIn(_items);
        _newGameBag = Bag.NewGame(ContentBootstrap.LoadConfig(catalog), _items);
        LoadCropDefinitions(catalog);
        _cropsBySeed = CropDefinition.BySeed(_cropDefs);
        _settlement = StandardSettlement.Create(_cropDefs, FallowRevertDays, RollMutation, WriteSave, PrintSummary);
        IndexCropArts();
        IndexItemIcons();

        // 背包里的每一样都在册，在册的每一样都核过有图标，所以这里按标识直接取。
        _carry.Icons = (itemId, qualityId) => _itemIcons[itemId].TextureFor(qualityId);
        LoadWorld();
        RefreshAll();

        // 床与醒来那一套各在自己的 _Ready 里登记过来，谁先谁后看场景树，所以等这一帧所有节点都就绪了再核。
        Callable.From(RequireWakeWhereBedsAre).CallDeferred();

        SetProcess(true);
        SetProcessUnhandledKeyInput(true);
    }

    /// <summary>摆了床就必须有醒来那一套：没有的话睡下去既没有淡出淡入，也没有摘要弹窗。</summary>
    private void RequireWakeWhereBedsAre()
    {
        if (_beds.Count > 0 && _wake is null)
        {
            throw new InvalidOperationException(
                $"{nameof(FarmField)}（节点 {Name}）这片地摆了床（{string.Join("、", _beds.Select(b => b.Name))}），"
                    + $"却没有醒来那一套（挂了 {nameof(SleepPresentation)} 的节点）—— 睡下去之后没有淡出淡入，也没有摘要弹窗");
        }
    }

    /// <summary>交互键只在这里听：站在床边是睡，否则作用在当前操作格上，由规则层挑。</summary>
    public override void _Process(double delta)
    {
        if (!_router.IsJustPressed(InputActions.Interact))
        {
            return;
        }

        switch (FarmInteraction.Pick(_beds.Any(bed => bed.SleeperInReach)))
        {
            case FarmInteractTarget.Bed: Sleep(); break;
            case FarmInteractTarget.CurrentCell: ActOnCurrentCell(); break;
        }
    }

    /// <summary>一张床把自己登记到这片地上，由床在它的 _Ready 里调。</summary>
    /// <remarks>床与这片地谁先 _Ready 取决于它们在场景树里的先后，所以这里只记下来，不读这片地的任何状态。</remarks>
    internal void RegisterBed(Bed bed)
    {
        if (!_beds.Contains(bed))
        {
            _beds.Add(bed);
        }
    }

    /// <summary>床离开场景树时把自己撤掉。</summary>
    internal void UnregisterBed(Bed bed) => _beds.Remove(bed);

    /// <summary>醒来那一套把自己登记到这片地上，由它在自己的 _Ready 里调。一片地只认一套。</summary>
    internal void RegisterWake(SleepPresentation wake)
    {
        if (_wake is not null && !ReferenceEquals(_wake, wake))
        {
            throw new InvalidOperationException(
                $"{nameof(FarmField)}（节点 {Name}）接了两套醒来的表现（{_wake.Name} 与 {wake.Name}）—— 一片地只摆一套");
        }

        _wake = wake;
    }

    /// <summary>醒来那一套离开场景树时把自己撤掉。</summary>
    internal void UnregisterWake(SleepPresentation wake)
    {
        if (ReferenceEquals(_wake, wake))
        {
            _wake = null;
        }
    }

    /// <summary>开发期的调试键：推进一天、换下一批种子。哪个键登在 <see cref="DebugKeys"/>。</summary>
    /// <remarks>
    /// 直接读按键、不走 <c>InputMap</c>，理由在 <see cref="DebugKeys"/>；键从那一处取，冲突测试才看得见它们。
    ///
    /// 推进一天与床走同一条路（<see cref="Sleep"/>），所以摆了醒来那一套时它也淡出淡入、也弹摘要。
    /// 睡下到醒来那一段里两个键都不响应：它们不经输入门面，门面上那道闸挡不住它们，只能在这里挡。
    /// </remarks>
    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true, Echo: false } key || _wake?.InputClosed == true)
        {
            return;
        }

        if (InputMapInstaller.IsDebugKey(key, DebugKeys.FarmEndDay))
        {
            Sleep();
        }
        else if (InputMapInstaller.IsDebugKey(key, DebugKeys.FarmGiveSeeds))
        {
            GiveSeedBatch();
        }
    }

    /// <summary>开发期的调试键：往背包里放下一批种子，每样一满堆，最后一批之后回到第一批。</summary>
    /// <remarks>
    /// 还没有商店，种子由它给；一批几种取随身栏几格。它与推进一天那个键一样直接读按键、不进输入映射，理由同上。
    /// 整批放不下就一件不放，屏上那一行说清，这一批也不往后挪 —— 腾出位置再按，给的还是这一批。
    /// 放进去的种子落在背包里第一个放得下的地方；随身栏只看得到背包前几格，后面那几格要挪到前面来才拿得到。
    /// </remarks>
    private void GiveSeedBatch()
    {
        var batch = SeedBatches.Batch(_cropDefs.Values, _seedBatch, CarrySlotBar.SlotCount, _items);
        var stored = _world.State.Bag.TryAddAll(batch);
        var batches = SeedBatches.CountFor(_cropDefs.Count, CarrySlotBar.SlotCount);

        if (FarmNotices.ForSeedBatch(stored, _text) is string line)
        {
            _notice.Show(line);
            GD.Print("[地块] 第 ", _seedBatch + 1, " 批种子（共 ", batches, " 批）没放进背包：", line);
            return;
        }

        GD.Print("[地块] 第 ", _seedBatch + 1, " 批种子（共 ", batches, " 批）放进了背包：",
                 string.Join("、", batch.Select(p => $"{p.ItemId}×{p.Count}")));
        _seedBatch = SeedBatches.Next(_seedBatch, _cropDefs.Count, CarrySlotBar.SlotCount);
        RefreshCarryBar();
    }

    /// <summary>睡下去：床与推进一天那个调试键都调它，别处调不到。</summary>
    /// <remarks>
    /// 推进一天没有第二条路，调试键也走这一条（设计仓 design/每日结算系统.md 的「一个入口，两个来源」一节）：
    /// 另开一条的话，调试推的那天没走完整串，而那种不一致在排缺陷时会把人带到错的方向。
    ///
    /// 摆了醒来那一套（<see cref="SleepPresentation"/>）就由它走淡出、结算、淡入、弹窗；没摆就当场结算，
    /// 结果只打在输出面板上。后一种是作物素材表那份实机清单搭出来的农场，那时还没有床。
    /// </remarks>
    private void Sleep()
    {
        if (_wake is null)
        {
            EndDay();
            return;
        }

        _wake.Begin(EndDay, _text, _store.Path);
    }

    /// <summary>推进一天：走每日结算那唯一的入口，然后重画，并把结果打在输出面板上。只由 <see cref="Sleep"/> 走到。</summary>
    /// <remarks>没推进时世界一格没动，输出面板上说清停在哪一步、为什么；停在自动存档那一步时连存档路径一起写。</remarks>
    private SettlementOutcome EndDay()
    {
        var outcome = _settlement.EndDay(_world, DayEndSource.Sleep);
        if (!outcome.Advanced)
        {
            GD.PrintErr("[结算] 这一天没有推进：停在「", outcome.FailedStep, "」，", outcome.Reason,
                        outcome.FailedStep == SettlementSteps.AutoSave ? $"（存档 {_store.Path}）" : "");
            return outcome;
        }

        GD.Print("[结算] 推进到 ", WakeReport.DateText(_world.State.Date, _text));
        RefreshAll();
        return outcome;
    }

    /// <summary>写正式存档：拼成文本、原子替换那个文件，写成之后报出路径与各分片的字节数。</summary>
    /// <remarks>
    /// 写不成就抛：结算接住它，这一天不推进，那句话就是原因。
    ///
    /// 写进文件那一下必须是这一步里最后一件会失败的事：它之后再抛，盘上已经是新的一天，结算却当这一天没有推进。
    /// 所以各分片的字节数在写之前量好。
    /// </remarks>
    private void WriteSave(WorldState world)
    {
        var text = FarmSave.Write(_registry, world);
        var shards = SaveFile.ShardBytes(text);
        var total = _store.Write(text);
        GD.Print("[存档] 写成 ", _store.Path, "，共 ", total, " 字节（",
                 string.Join("、", shards.Select(s => $"{s.Id} {s.Bytes}")), "）");
    }

    /// <summary>醒来那一步交出来的摘要：完整明细逐格打在输出面板上，一格一行。</summary>
    /// <remarks>
    /// 完整明细的家是手环上那一页，那一页还没有，归设计仓台账 UI-20。简版弹窗读的也是这一份摘要，由醒来那一套弹。
    /// </remarks>
    private void PrintSummary(SettlementSummary summary)
    {
        foreach (var line in WakeReport.DetailLines(summary, _text, id => _cropDefs[id].NameKeyIn(_items)))
        {
            GD.Print("[结算] ", line);
        }
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

        if (!_world.State.Plots.TryGet(ToGridCell(_cursor.Cell), out var plot))
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

    /// <summary>播一次：手上那一格是背包里的哪一格，就从那一格拿一颗种子去播。</summary>
    /// <remarks>
    /// 判定与扣种子在 <see cref="Planting.TryPlantFromBag"/>，被拒时写哪一句在 <see cref="FarmNotices"/>；本方法把那一句交给
    /// 即时提示，输出面板也打同一句，带上是哪一格。随身栏是背包前几格的投影，所以选中第几格就是背包第几格。
    ///
    /// 三种原因要分得开（手上是空格、手上不是种子、这一格不许播）。合并成一句「不能播」之后，
    /// 玩家按下去没反应，而他不知道该换格子、换东西还是先锄地。
    /// </remarks>
    private bool PlantFromHand(Plot plot)
    {
        var result = Planting.TryPlantFromBag(
            plot,
            _world.State.Bag,
            _carry.Bar.SelectedIndex,
            id => _cropsBySeed.TryGetValue(id, out var crop) ? crop : null);

        if (FarmNotices.ForSowing(result, _text) is string line)
        {
            _notice.Show(line);
            GD.Print("[地块] 播不了 ", _cursor.Cell, "：", line);
        }

        return result == PlantResult.Planted;
    }

    /// <summary>收一次：收成经生产那条管道进主角的背包，放不下那一株照旧待收。</summary>
    /// <remarks>
    /// 判在规则层（<see cref="Harvesting.ByProtagonist"/>），被拒时写哪一句在 <see cref="FarmNotices"/>。不要在这里绕过它直接往背包里放：
    /// 产出入库只有管道那一处，多一个入口，「放不下那一株还在」就只在其中一处成立。
    /// </remarks>
    private bool Harvest(Plot plot)
    {
        var crop = CropOf(plot) ?? throw new InvalidOperationException(
            $"这一格是 {plot.State}、作物标识是 {plot.CropId}，但找不到那份作物定义");

        var attempt = Harvesting.ByProtagonist(plot, crop, _yield, _world.State.Bag);
        if (FarmNotices.ForHarvest(attempt.Status, _text) is string line)
        {
            _notice.Show(line);
            GD.Print("[地块] 收不了 ", _cursor.Cell, "：", line);
        }

        if (attempt is { Status: HarvestStatus.Stored, Delivery: { } delivery })
        {
            var goods = delivery.Goods;
            GD.Print("[地块] 收上来 ", goods.Count, " 件 ", goods.ItemId, "（", goods.QualityId, "），进了背包");
        }

        return attempt.Status == HarvestStatus.Stored;
    }

    /// <summary>这一格里那种作物的定义；空地为 <c>null</c>。</summary>
    private CropDefinition? CropOf(Plot plot) =>
        plot.CropId is string id && _cropDefs.TryGetValue(id, out var crop) ? crop : null;

    /// <summary>读全部作物定义。怎么读、什么算坏与启动时那一次核对是同一处（<see cref="CropDefinition.LoadAll"/>）。</summary>
    private void LoadCropDefinitions(ContentCatalog catalog)
    {
        foreach (var (id, crop) in CropDefinition.LoadAll(catalog, _items))
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

    /// <summary>核全部物品图标，再按标识索引起来。有一处不对就抛，全部问题一次列出来。</summary>
    /// <remarks>
    /// 判在规则层（<see cref="ItemIconCheck"/>），这里只把每一份配了什么交过去。启动时就核，不等画到那一格：
    /// 缺图在格子里的样子是空着，而玩家分不清「这一格没东西」和「这一格有东西但没图」。
    /// </remarks>
    private void IndexItemIcons()
    {
        var problems = ItemIconCheck.Problems(_items, [.. ItemIcons.Select(icon => icon?.ToEntry())]);
        if (problems.Count > 0)
        {
            throw new InvalidOperationException(
                $"{nameof(FarmField)}（节点 {Name}）的物品图标有 {problems.Count} 处不对：\n  " + string.Join("\n  ", problems));
        }

        foreach (var icon in ItemIcons)
        {
            _itemIcons[icon.ItemId] = icon;
        }
    }

    /// <summary>随身栏照背包前几格重画：随身栏自己不存东西，背包变了它就跟着变。</summary>
    /// <remarks>选中第几格不跟着内容走，那由 <see cref="CarrySlotBar.Fill"/> 保证。品质跟着那一堆走，画哪一张图标看它。</remarks>
    private void RefreshCarryBar()
    {
        var bagSlots = _world.State.Bag.Slots
            .Select(s => s is { } stack ? new CarrySlot(stack.ItemId, stack.Count, stack.QualityId) : CarrySlot.Empty)
            .ToList();
        _carry.Bar.Fill(CarrySlotBar.ProjectionOf(bagSlots));
        _carry.OnContentChanged();
    }

    /// <summary>有存档就读回，没有就开新档：日期是开局那一天，地按检查器里那几格铺出来。</summary>
    /// <remarks>
    /// 存档在、却读不出来或被拒时当场报错，不开新档：开了新档，下一次睡觉就会把那份存档盖掉，而它多半只是改过一次
    /// 场景里那片地、或者装少了一个 mod。判在规则层（<see cref="FarmSave"/>），这里只把文件读进来、把报错写上路径。
    /// </remarks>
    private void LoadWorld()
    {
        var sceneField = PlotGrid.Rectangle(ToGridCell(FieldOrigin), FieldSizeInCells.X, FieldSizeInCells.Y, InitialState);
        _registry = new SaveShardRegistry();
        WorldShards.Register(_registry, sceneField, _newGameBag);
        _store = new SaveStore(ModPaths.ResolveUserPath(ModPaths.SaveRoot));

        string? text;
        WorldState world;
        try
        {
            text = _store.ReadOrNull();
            world = FarmSave.LoadOrNew(text, _registry, _cropDefs, sceneField, _newGameBag);
        }
        catch (Exception e) when (e is SaveRejectedException or InvalidDataException or IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException(
                $"存档 {_store.Path} 读不出来，没有开新档（开了新档，下一次睡觉就会把它盖掉）：{e.Message}", e);
        }

        _world = new CurrentWorld(world);
        GD.Print(text is null ? "[存档] 还没有存档，按开局那一天开新档：" : "[存档] 读回存档：", _store.Path,
                 "，", WakeReport.DateText(world.Date, _text));
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
        RefreshCarryBar();
    }

    private void RefreshTilledSoil()
    {
        var dry = new Godot.Collections.Array<Vector2I>();
        var wet = new Godot.Collections.Array<Vector2I>();

        foreach (var (cell, plot) in _world.State.Plots.Cells)
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

        foreach (var (gridCell, plot) in _world.State.Plots.Cells)
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
