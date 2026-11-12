using System.Reflection;
using System.Text.Json.Nodes;
using Tinderhearth.Rules.Economy;
using Tinderhearth.Rules.Foundation.Save;
using Tinderhearth.Rules.Progression;
using Xunit;

namespace Tinderhearth.Rules.Tests.Economy;

/// <summary>日历、那片地与主角背包那几个分片：存进去读出来一样、读的先后不影响结果、记得矛盾时点名哪一格。</summary>
/// <remarks>作物的阶段天数与荒置天数、背包网格多大、随身栏几格都是测试用值，改它们不该让任何一条失败。</remarks>
public class WorldShardsTests
{
    private const int FallowDays = 7;

    /// <summary>现在配置的网格几格宽、几格高，随身栏几格。</summary>
    private const int GridWidth = 4;
    private const int GridHeight = 3;
    private const int CarryCells = 3;

    /// <summary>改成网格之前那一版的存档版本号：背包那一片一格一项，随身栏是开头那几格。</summary>
    private const int FlatBagVersion = 2;

    /// <summary>还没有背包那一片的那一版存档。</summary>
    private const int BeforeBagShard = 1;

    /// <summary>占 2×2、一堆只放 1 件、无品质的一样东西：网格里的一堆不只占一格时，位置读写才真的被核到。</summary>
    private const string Crate = "item_crate";

    private static readonly ItemCatalog Items = ItemCatalog.Build(
        [
            .. TestBags.Items.Items.Values,
            TestBags.Items.Items[TestBags.TurnipSeed] with { Id = Crate, Footprint = new() { Width = 2, Height = 2 }, StackLimit = 1 },
        ],
        TestBags.Items.Categories.Values,
        TestBags.Items.Rarities,
        TestBags.Items.Qualities,
        name => name.Split(' ')[0]);

    private static readonly CropDefinition Turnip = new()
    {
        Id = "turnip",
        StageDays = [1, 1],
        RegrowFromStage = null,
        Seasons = [Season.Spring],
        SeedItemId = "item_turnip_seed",
        YieldItemId = "item_turnip",
    };

    /// <summary>能反复收的：收完退回第 1 阶段再长。</summary>
    private static readonly CropDefinition Berry = new()
    {
        Id = "berry",
        StageDays = [1, 2, 1],
        RegrowFromStage = 1,
        Seasons = [Season.Spring, Season.Summer],
        SeedItemId = "item_berry_seed",
        YieldItemId = "item_berry",
    };

    private static readonly IReadOnlyDictionary<string, CropDefinition> Crops =
        new Dictionary<string, CropDefinition> { [Turnip.Id] = Turnip, [Berry.Id] = Berry };

    private static readonly HarvestYieldRule Yield = new(BaseCount: 10, MinWaterFactor: 0.5, MaxWaterFactor: 1.0);

    /// <summary>开新档时的背包：空的，现在配置的网格与随身栏那么大。</summary>
    private static Bag NewGameBag() => NewGameBag(GridWidth, GridHeight, CarryCells);

    private static Bag NewGameBag(int width, int height, int carryCells) => new(width, height, carryCells, Items);

    /// <summary>
    /// 一片 3×3 的地，左上角在 (-1, 2)。按行：在长且浇过、待收且变异、收过一茬又在长且浇过；
    /// 枯死、荒置了一天、未清理；可耕、空着的已锄、待收没变异。
    /// 背包：随身栏是一堆变异芜菁、一个空格、一堆种子；4×3 的网格里 (0, 0) 一堆寻常芜菁，(1, 0) 一个箱子盖住往右下的 2×2，
    /// (3, 1) 一堆种子，(2, 2) 又一堆寻常芜菁。
    /// </summary>
    private static WorldState BusyWorld()
    {
        var p = new List<Plot>
        {
            new(PlotState.Tilled), new(PlotState.Tilled), new(PlotState.Tilled),
            new(PlotState.Tilled), new(PlotState.Tilled), new(PlotState.Uncleared),
            new(PlotState.Cleared), new(PlotState.Tilled), new(PlotState.Tilled),
        };

        p[0].Plant(Turnip);
        p[0].Water();

        p[1].Plant(Turnip);
        p[1].Water();
        GrowDays(p[1], Turnip, Turnip.DaysToFirstRipe, mutate: true);

        p[2].Plant(Berry);
        GrowDays(p[2], Berry, Berry.DaysToFirstRipe, mutate: false);
        p[2].TryHarvest(Berry, Yield, out _);
        p[2].Water();
        GrowDays(p[2], Berry, 1, mutate: false);
        p[2].Water();

        p[3].Plant(Turnip);
        p[3].ApplySeasonChange(Turnip, Season.Summer);

        p[4].AdvanceDay(null, FallowDays, () => false);

        p[8].Plant(Berry);
        GrowDays(p[8], Berry, Berry.DaysToFirstRipe, mutate: false);

        var anchors = new ItemStack?[GridWidth * GridHeight];
        anchors[Cell(0, 0)] = new ItemStack(TestBags.Turnip, TestBags.Common, 20, 3);
        anchors[Cell(1, 0)] = new ItemStack(Crate, null, 1, 5);
        anchors[Cell(3, 1)] = new ItemStack(TestBags.TurnipSeed, null, 5, 1);
        anchors[Cell(2, 2)] = new ItemStack(TestBags.Turnip, TestBags.Common, 4, 6);
        var bag = Bag.Restore(
            [new ItemStack(TestBags.Turnip, TestBags.Mutated, 3, 2), null, new ItemStack(TestBags.TurnipSeed, null, 12, 4)],
            GridWidth,
            GridHeight,
            anchors,
            Items);

        return new WorldState(new GameDate(2, Season.Summer, 17), PlotGrid.FromRows(new GridCell(-1, 2), 3, 3, p), bag);
    }

    /// <summary>现在配置的网格里 (x, y) 那一格的格号。</summary>
    private static int Cell(int x, int y) => (y * GridWidth) + x;

    /// <summary>网格里每一堆和它左上角那一格的 (列, 行)，从上到下、从左到右：与网格多宽无关，宽窄不同的两个背包也比得了。</summary>
    private static List<(int X, int Y, ItemStack Stack)> StacksByPosition(Bag bag) =>
    [
        .. bag.Slots
            .Select((stack, cell) => (Stack: stack, Cell: cell))
            .Where(s => s.Stack is not null)
            .Select(s => (s.Cell % bag.GridWidth, s.Cell / bag.GridWidth, s.Stack!.Value)),
    ];

    private static void GrowDays(Plot plot, CropDefinition crop, int days, bool mutate)
    {
        for (var i = 0; i < days; i++)
        {
            plot.AdvanceDay(crop, FallowDays, () => mutate);
        }
    }

    /// <summary>把一份存档的文本拆成按标识给的分片，与读档时交给各持有者的是同一个形状。</summary>
    private static Dictionary<string, JsonObject> ShardsOf(string text) =>
        JsonNode.Parse(text)![SaveFile.ShardsKey]!.AsObject().ToDictionary(p => p.Key, p => p.Value!.AsObject());

    private static SaveShardRegistry RegistryFor(PlotGrid plots)
    {
        var registry = new SaveShardRegistry();
        WorldShards.Register(registry, plots, NewGameBag());
        return registry;
    }

    private static string FormalSaveOf(WorldState world) =>
        SaveFile.WriteFormalSave(RegistryFor(world.Plots), WorldShards.Write(world));

    private static void AssertSameWorld(WorldState expected, WorldState actual)
    {
        Assert.Equal(expected.Date, actual.Date);
        Assert.Equal(
            (expected.Plots.Origin, expected.Plots.Width, expected.Plots.Height),
            (actual.Plots.Origin, actual.Plots.Width, actual.Plots.Height));
        Assert.Equal(PlotSnapshot.Of(expected), PlotSnapshot.Of(actual));
        Assert.Equal(expected.Bag.Carry, actual.Bag.Carry);
        Assert.Equal((expected.Bag.GridWidth, expected.Bag.GridHeight), (actual.Bag.GridWidth, actual.Bag.GridHeight));
        Assert.Equal(expected.Bag.Slots, actual.Bag.Slots);
    }

    [Fact]
    public void 日历地块与背包存进去读出来完全一致()
    {
        var world = BusyWorld();

        var back = WorldShards.Read(ShardsOf(FormalSaveOf(world)), Crops, NewGameBag());

        AssertSameWorld(world, back);
        Assert.Contains(back.Plots.Cells, c => c.Plot.Mutated);
        Assert.Contains(back.Plots.Cells, c => c.Plot.WateredToday);
        Assert.Null(back.Bag.Carry[1]);
        Assert.Equal(TestBags.Mutated, back.Bag.Carry[0]?.QualityId);
        Assert.Equal(Crate, back.Bag.Slots[Cell(1, 0)]?.ItemId);
        Assert.False(back.Bag.CanAdd(Crate, null, 1));
    }

    /// <summary>读回来之后再进东西，序号接着各堆里最大的那个往后排，不从头数。</summary>
    [Fact]
    public void 读回来的背包序号接着往后排()
    {
        var back = WorldShards.Read(ShardsOf(FormalSaveOf(BusyWorld())), Crops, NewGameBag());

        Assert.True(back.Bag.TryAdd(TestBags.TurnipSeed, null, 1));

        Assert.Equal(7, back.Bag.Carry[2]?.AcquiredSequence);
    }

    /// <summary>
    /// 接着排的那个序号是从各堆算出来的，不是另存的一格：改掉存档里一堆的序号再读回来，新进的东西跟着排到它后面。
    /// 另存一格的实现过不了这一条（设计仓 design/存档系统.md 的「派生量不单独存」一节）。
    /// </summary>
    [Fact]
    public void 改了存档里一堆的序号新进的东西跟着排到它后面()
    {
        var bag = WorldShards.WriteBag(BusyWorld().Bag);
        bag["gridAcquired"]![0] = 40;

        var back = WorldShards.ReadBag(bag, NewGameBag());
        Assert.True(back.TryAdd(TestBags.TurnipSeed, null, 1));

        Assert.Equal(41, back.Carry[2]?.AcquiredSequence);
    }

    /// <summary>分片按什么先后交进来、分片里各格按什么先后写，读出来都一样。</summary>
    [Fact]
    public void 分片按逆序与乱序读结果一致()
    {
        var world = BusyWorld();
        var shards = WorldShards.Write(world);
        var reversed = shards.Reverse().ToDictionary(p => p.Key, p => p.Value);
        var shuffledFields = shards.ToDictionary(
            p => p.Key,
            p => new JsonObject([.. p.Value.Reverse().Select(f => KeyValuePair.Create(f.Key, f.Value?.DeepClone()))]));

        var forward = WorldShards.Read(shards, Crops, NewGameBag());

        AssertSameWorld(forward, WorldShards.Read(reversed, Crops, NewGameBag()));
        AssertSameWorld(forward, WorldShards.Read(shuffledFields, Crops, NewGameBag()));
        Assert.NotEqual(shards.Keys, reversed.Keys);
    }

    /// <summary>给地块加一项状态而不存它，这一条失败：那一项会在读档之后悄悄回到初始值。</summary>
    [Fact]
    public void 地块分片存的正是一格地的全部状态()
    {
        static string CamelCase(string name) => char.ToLowerInvariant(name[0]) + name[1..];
        var plotProperties = typeof(Plot).GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => p.Name).ToList();
        var recordProperties = typeof(PlotRecord).GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => p.Name);

        Assert.Equal(plotProperties.Order(), recordProperties.Order());
        Assert.Equal(plotProperties.Select(CamelCase).Order(), WorldShards.PlotStateKeys.Order());
        Assert.Equal(
            WorldShards.PlotLayoutKeys.Concat(WorldShards.PlotStateKeys).Order(),
            WorldShards.WritePlots(BusyWorld().Plots).Select(p => p.Key).Order());
    }

    /// <summary>
    /// 给一堆加一项而不存它，这一条失败：那一项会在读档之后悄悄回到初始值。随身栏每一格是一堆的那几项，网格里每一堆再加
    /// 左上角在第几列、第几行。分片里也只有这几串与网格多大：接着排的序号由它们算得出来，另存一格这一条也失败。
    /// </summary>
    [Fact]
    public void 背包分片存的正是一堆的全部字段()
    {
        string[] stackFields = ["ItemId", "QualityId", "Count", "AcquiredSequence"];
        const int positionFields = 2;
        var stackProperties = typeof(ItemStack).GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => p.Name);

        Assert.Equal(stackFields.Order(), stackProperties.Order());
        Assert.Equal(stackFields.Length, WorldShards.BagCarryKeys.Count);
        Assert.Equal(stackFields.Length + positionFields, WorldShards.BagGridStackKeys.Count);
        Assert.Equal(
            WorldShards.BagCarryKeys.Concat(WorldShards.BagGridSizeKeys).Concat(WorldShards.BagGridStackKeys).Order(),
            WorldShards.WriteBag(BusyWorld().Bag).Select(p => p.Key).Order());
    }

    [Fact]
    public void 一份世界写的正是日历地块与背包那几个分片()
    {
        string[] expected = [WorldShards.CalendarId, WorldShards.PlotsId, WorldShards.BagId];

        Assert.Equal(expected, WorldShards.Write(BusyWorld()).Keys);
    }

    [Theory]
    [InlineData("空地带着作物标识", "(1, 3)")]
    [InlineData("计数为负", "(0, 3)")]
    [InlineData("阶段超出那种作物的阶段数", "(-1, 2)")]
    [InlineData("在长的作物记着变异", "(-1, 2)")]
    [InlineData("认不出的状态", "(0, 4)")]
    [InlineData("一串的项数不对", "「stage」")]
    public void 一格地记的几项彼此矛盾时被拒并点名哪一格(string broken, string named)
    {
        var plots = WorldShards.WritePlots(BusyWorld().Plots);
        switch (broken)
        {
            case "空地带着作物标识":
                plots["cropId"]![5] = "turnip";
                break;
            case "计数为负":
                plots["daysFallow"]![4] = -1;
                break;
            case "阶段超出那种作物的阶段数":
                plots["stage"]![0] = Turnip.RipeStage + 3;
                break;
            case "在长的作物记着变异":
                plots["mutated"]![0] = true;
                break;
            case "认不出的状态":
                plots["state"]![7] = "Flooded";
                break;
            case "一串的项数不对":
                plots["stage"]!.AsArray().RemoveAt(8);
                break;
        }

        var e = Assert.Throws<InvalidDataException>(() => WorldShards.ReadPlots(plots, Crops));

        Assert.Contains(named, e.Message);
    }

    /// <summary>有几处读不出来就报几处，不是撞到第一处就停。</summary>
    [Fact]
    public void 几格同时读不出来时一次全报()
    {
        var plots = WorldShards.WritePlots(BusyWorld().Plots);
        plots["cropId"]![5] = "turnip";
        plots["daysFallow"]![4] = -1;

        var e = Assert.Throws<InvalidDataException>(() => WorldShards.ReadPlots(plots, Crops));

        Assert.Contains("(1, 3)", e.Message);
        Assert.Contains("(0, 3)", e.Message);
    }

    [Fact]
    public void 地里那种作物找不到定义时被拒并点名哪一格与哪个标识()
    {
        var onlyTurnip = new Dictionary<string, CropDefinition> { [Turnip.Id] = Turnip };

        var e = Assert.Throws<InvalidDataException>(
            () => WorldShards.ReadPlots(WorldShards.WritePlots(BusyWorld().Plots), onlyTurnip));

        Assert.Contains(Berry.Id, e.Message);
        Assert.Contains("(1, 2)", e.Message);
        Assert.Contains("(1, 4)", e.Message);
    }

    /// <summary>存档里有物品定义里找不到的标识：被拒，点名哪一格、哪个标识。网格里的一堆写成它左上角那一格的 (列, 行)。</summary>
    [Theory]
    [InlineData("carryItemId", 0, "随身栏第 1 格")]
    [InlineData("gridItemId", 2, "网格 (3, 1)")]
    public void 背包里认不出的物品被拒并点名哪一格与哪个标识(string key, int index, string named)
    {
        var bag = WorldShards.WriteBag(BusyWorld().Bag);
        bag[key]![index] = "item_ghost";

        var e = Assert.Throws<InvalidDataException>(() => WorldShards.ReadBag(bag, NewGameBag()));

        Assert.Contains($"{named}：物品「item_ghost」不在册", e.Message);
    }

    /// <summary>存档里的网格比现在配置的宽或高：被拒，说清两边各多大。多出来的那一片没有地方放，而任何入口都不自动丢弃。</summary>
    [Theory]
    [InlineData(GridWidth - 1, GridHeight)]
    [InlineData(GridWidth, GridHeight - 1)]
    public void 存档里的网格比配置宽或高就被拒并说清两边各多大(int width, int height)
    {
        var saved = WorldShards.WriteBag(BusyWorld().Bag);

        var e = Assert.Throws<InvalidDataException>(() => WorldShards.ReadBag(saved, NewGameBag(width, height, CarryCells)));

        Assert.Contains($"记的网格是 {GridWidth}×{GridHeight}", e.Message);
        Assert.Contains($"现在配置的网格是 {width}×{height}", e.Message);
    }

    /// <summary>
    /// 存档里的网格比现在配置的小（升了一级）：读得进来，每一堆还在原来那一列那一行，箱子照样盖着它那一块，新多出来的格空着。
    /// 照格号搬过去的实现过不了这一条：网格变宽之后，同一个格号是另一列另一行。
    /// </summary>
    [Fact]
    public void 存档里的网格比配置小就照现在的大小读_每一堆还在原来那一列那一行()
    {
        var world = BusyWorld();
        var bigger = NewGameBag(GridWidth + 2, GridHeight + 1, CarryCells);

        var back = WorldShards.ReadBag(WorldShards.WriteBag(world.Bag), bigger);

        Assert.Equal((bigger.GridWidth, bigger.GridHeight), (back.GridWidth, back.GridHeight));
        Assert.Equal(StacksByPosition(world.Bag), StacksByPosition(back));
        Assert.Equal(world.Bag.Carry, back.Carry);
        Assert.Equal(bigger.SlotCount - world.Bag.SlotCount + world.Bag.EmptySlotCount, back.EmptySlotCount);
    }

    /// <summary>存档里的随身栏比现在的格数多：被拒，说清两边各几格。</summary>
    [Fact]
    public void 存档里的随身栏比现在的格数多就被拒并说清两边各几格()
    {
        var saved = WorldShards.WriteBag(BusyWorld().Bag);

        var e = Assert.Throws<InvalidDataException>(
            () => WorldShards.ReadBag(saved, NewGameBag(GridWidth, GridHeight, CarryCells - 1)));

        Assert.Contains($"随身栏有 {CarryCells} 格", e.Message);
        Assert.Contains($"只有 {CarryCells - 1} 格", e.Message);
    }

    /// <summary>存档里的随身栏比现在的格数少：读得进来，原来那几格不动，后面补空格。</summary>
    [Fact]
    public void 存档里的随身栏比现在的格数少就在后面补空格()
    {
        var world = BusyWorld();

        var back = WorldShards.ReadBag(WorldShards.WriteBag(world.Bag), NewGameBag(GridWidth, GridHeight, CarryCells + 2));

        Assert.Equal([.. world.Bag.Carry, null, null], back.Carry);
        Assert.Equal(world.Bag.Slots, back.Slots);
    }

    /// <summary>随身栏的空格却记着件数、品质或序号：被拒，点名哪一格。</summary>
    [Theory]
    [InlineData("carryCount", "3")]
    [InlineData("carryQualityId", "\"common\"")]
    [InlineData("carryAcquired", "1")]
    public void 空格却记着东西就被拒并点名(string key, string valueJson)
    {
        var bag = WorldShards.WriteBag(BusyWorld().Bag);
        bag[key]![1] = JsonNode.Parse(valueJson);

        var e = Assert.Throws<InvalidDataException>(() => WorldShards.ReadBag(bag, NewGameBag()));

        Assert.Contains("随身栏第 2 格是空格", e.Message);
    }

    /// <summary>一堆的件数超过那种物品的堆上限、品质那一位与物品的声明不符、或者序号不为正：被拒，点名哪一格。</summary>
    [Theory]
    [InlineData("gridCount", 0, "21", "网格 (0, 0)")]
    [InlineData("gridCount", 1, "2", "网格 (1, 0)")]
    [InlineData("gridQualityId", 0, "null", "网格 (0, 0)")]
    [InlineData("carryQualityId", 0, "null", "随身栏第 1 格")]
    [InlineData("gridAcquired", 2, "0", "网格 (3, 1)")]
    [InlineData("carryAcquired", 2, "0", "随身栏第 3 格")]
    public void 一堆记得与物品定义对不上就被拒并点名(string key, int index, string valueJson, string named)
    {
        var bag = WorldShards.WriteBag(BusyWorld().Bag);
        bag[key]![index] = JsonNode.Parse(valueJson);

        var e = Assert.Throws<InvalidDataException>(() => WorldShards.ReadBag(bag, NewGameBag()));

        Assert.Contains($"{named}：", e.Message);
    }

    /// <summary>随身栏几格、网格里几堆各取种类那一串的项数：别的哪一串长短不一样，被拒并点名那一串。</summary>
    [Theory]
    [InlineData("carryCount")]
    [InlineData("gridCount")]
    [InlineData("gridY")]
    public void 背包几串不一样长就被拒并点名哪一串(string key)
    {
        var bag = WorldShards.WriteBag(BusyWorld().Bag);
        bag[key]!.AsArray().RemoveAt(0);

        var e = Assert.Throws<InvalidDataException>(() => WorldShards.ReadBag(bag, NewGameBag()));

        Assert.Contains($"「{key}」", e.Message);
    }

    /// <summary>
    /// 网格里一堆的位置记坏了：左上角不在存档里那张网格里、两堆的左上角在同一格、没有物品标识、压着另一堆、往右下出了网格。
    /// 被拒，点名哪一堆。前三样是读的时候查的、点名第几堆；后两样要摆起来才看得出，点名它左上角那一格。
    /// </summary>
    [Theory]
    [InlineData("左上角在网格右边外面", "网格里第 3 堆（「item_turnip_seed」）的左上角 (4, 1) 不在存档里 4×3 的网格里")]
    [InlineData("左上角在网格上边外面", "网格里第 3 堆（「item_turnip_seed」）的左上角 (3, -1) 不在存档里 4×3 的网格里")]
    [InlineData("两堆的左上角在同一格", "网格里第 4 堆（「item_turnip」）与第 3 堆（「item_turnip_seed」）的左上角都在 (3, 1)")]
    [InlineData("没有物品标识", "网格里第 3 堆没有物品标识")]
    [InlineData("压着另一堆", "网格 (2, 1)：「item_turnip」与左上角在 (1, 0) 的「item_crate」压在一起")]
    [InlineData("往右下出了网格", "网格 (3, 0)：「item_crate」占 2×2 格，从这里往右下摆出了 4×3 的网格")]
    public void 网格里一堆的位置记坏了就被拒并点名哪一堆(string broken, string named)
    {
        // 网格里那几堆按左上角从上到下、从左到右写：第 1 堆 (0, 0) 芜菁、第 2 堆 (1, 0) 箱子、第 3 堆 (3, 1) 种子、第 4 堆 (2, 2) 芜菁。
        var bag = WorldShards.WriteBag(BusyWorld().Bag);
        switch (broken)
        {
            case "左上角在网格右边外面":
                bag["gridX"]![2] = GridWidth;
                break;
            case "左上角在网格上边外面":
                bag["gridY"]![2] = -1;
                break;
            case "两堆的左上角在同一格":
                bag["gridX"]![3] = 3;
                bag["gridY"]![3] = 1;
                break;
            case "没有物品标识":
                bag["gridItemId"]![2] = null;
                break;
            case "压着另一堆":
                bag["gridY"]![3] = 1;
                break;
            case "往右下出了网格":
                bag["gridX"]![1] = 3;
                break;
        }

        var e = Assert.Throws<InvalidDataException>(() => WorldShards.ReadBag(bag, NewGameBag()));

        Assert.Contains(named, e.Message);
    }

    [Fact]
    public void 日历记的不是日历上的一天时被拒()
    {
        JsonObject[] broken =
        [
            new() { ["year"] = 0, ["season"] = "Spring", ["day"] = 1 },
            new() { ["year"] = 1, ["season"] = "Spring", ["day"] = Calendar.DaysPerSeason + 1 },
            new() { ["year"] = 1, ["season"] = "Monsoon", ["day"] = 1 },
            new() { ["year"] = 1, ["season"] = "Spring" },
            new() { ["year"] = "一", ["season"] = "Spring", ["day"] = 1 },
        ];

        Assert.All(broken, shard => Assert.Throws<InvalidDataException>(() => WorldShards.ReadCalendar(shard)));
    }

    [Fact]
    public void 缺一个分片时被拒并点名它()
    {
        var calendarOnly = new Dictionary<string, JsonObject> { [WorldShards.CalendarId] = WorldShards.WriteCalendar(Calendar.StartDate) };

        var e = Assert.Throws<InvalidDataException>(() => WorldShards.Read(calendarOnly, Crops, NewGameBag()));

        Assert.Contains(WorldShards.PlotsId, e.Message);
    }

    /// <summary>注册之后场景那片地再怎么变，默认值也还是注册那一刻的起始状态；背包的默认值是开新档时那个空背包。</summary>
    [Fact]
    public void 几个分片的存档默认值就是开新档时那一份()
    {
        var scene = PlotGrid.Rectangle(new GridCell(4, 5), width: 2, height: 3, PlotState.Tilled);
        var newGame = WorldState.NewGame(scene.Clone(), NewGameBag());
        string[] ids = [WorldShards.CalendarId, WorldShards.PlotsId, WorldShards.BagId];

        var registry = RegistryFor(scene);
        scene.Cells.First().Plot.Plant(Turnip);
        var fromDefaults = WorldShards.Read(registry.Shards.ToDictionary(s => s.Id, s => s.Default()), Crops, NewGameBag());

        Assert.Equal(ids, registry.Shards.Select(s => s.Id));
        Assert.All(registry.Shards, s => Assert.Equal(SaveSide.FormalSave, s.Side));
        AssertSameWorld(newGame, fromDefaults);
    }

    /// <summary>
    /// 还没有背包那一片的那一版存档：读进来补一个空背包，日期与每一格地照旧。
    /// </summary>
    /// <remarks>那一版写出来的就是现在这样少了背包那一片的那一份；读得进来也说明最低兼容版本不高于那一版。</remarks>
    [Fact]
    public void 还没有背包那一片的存档读进来补一个空背包()
    {
        var world = BusyWorld();
        var root = JsonNode.Parse(FormalSaveOf(world))!.AsObject();
        root[SaveFile.VersionKey] = BeforeBagShard;
        root[SaveFile.ShardsKey]!.AsObject().Remove(WorldShards.BagId);

        var back = WorldShards.Read(SaveFile.ReadFormalSave(RegistryFor(world.Plots), root.ToJsonString()), Crops, NewGameBag());

        Assert.Equal(world.Date, back.Date);
        Assert.Equal(PlotSnapshot.Of(world), PlotSnapshot.Of(back));
        Assert.True(back.Bag.IsEmpty);
        Assert.Equal((GridWidth, GridHeight, CarryCells), (back.Bag.GridWidth, back.Bag.GridHeight, back.Bag.CarryCount));
    }

    /// <summary>改成网格之前那一版的背包那一片：一格一项，种类、品质、件数、序号各一串，随身栏是开头那几格。</summary>
    private static JsonObject FlatBag(IReadOnlyList<ItemStack?> slots) => new()
    {
        ["itemId"] = new JsonArray([.. slots.Select(s => JsonValue.Create(s?.ItemId))]),
        ["qualityId"] = new JsonArray([.. slots.Select(s => JsonValue.Create(s?.QualityId))]),
        ["count"] = new JsonArray([.. slots.Select(s => (JsonNode)JsonValue.Create(s?.Count ?? 0))]),
        ["acquired"] = new JsonArray([.. slots.Select(s => (JsonNode)JsonValue.Create(s?.AcquiredSequence ?? 0))]),
    };

    /// <summary>一份改成网格之前那一版的存档：日历与那片地照现在写，背包那一片换成那一版的形状。</summary>
    private static string FlatBagSave(WorldState world, IReadOnlyList<ItemStack?> slots)
    {
        var root = JsonNode.Parse(FormalSaveOf(world))!.AsObject();
        root[SaveFile.VersionKey] = FlatBagVersion;
        root[SaveFile.ShardsKey]![WorldShards.BagId] = FlatBag(slots);
        return root.ToJsonString();
    }

    /// <summary>
    /// 改成网格之前那一版的存档读进来：开头那几格（随身栏几格那么多）进随身栏那一排，其余按原来的格号从左上往右下摆进网格，
    /// 一件不少；日期与每一格地照旧。写出去是现在的形状，再读回来一样。
    /// </summary>
    [Fact]
    public void 改成网格之前那一版的背包读进来_开头那几格进随身栏_其余照格号摆进网格()
    {
        var world = BusyWorld();
        var slots = new ItemStack?[CarryCells + 7];
        slots[0] = new ItemStack(TestBags.TurnipSeed, null, 12, 1);
        slots[2] = new ItemStack(TestBags.Turnip, TestBags.Common, 3, 2);
        slots[CarryCells] = new ItemStack(TestBags.Turnip, TestBags.Mutated, 4, 3);
        slots[CarryCells + 6] = new ItemStack(TestBags.TurnipSeed, null, 2, 4);
        var registry = RegistryFor(world.Plots);

        var back = WorldShards.Read(SaveFile.ReadFormalSave(registry, FlatBagSave(world, slots)), Crops, NewGameBag());

        Assert.Equal(world.Date, back.Date);
        Assert.Equal(PlotSnapshot.Of(world), PlotSnapshot.Of(back));
        Assert.Equal(slots[..CarryCells], back.Bag.Carry);
        Assert.Equal(
            [(0, 0, slots[CarryCells]!.Value), (2, 1, slots[CarryCells + 6]!.Value)],
            StacksByPosition(back.Bag));

        var again = WorldShards.Read(SaveFile.ReadFormalSave(registry, FarmSave.Write(registry, back)), Crops, NewGameBag());
        AssertSameWorld(back, again);
    }

    /// <summary>改成网格之前那一版的背包比现在的随身栏加网格还多格：被拒，说清两边各几格。多出来的那几格没有地方放。</summary>
    [Fact]
    public void 改成网格之前那一版的背包格数比现在多就被拒并说清两边各几格()
    {
        var world = BusyWorld();
        var room = CarryCells + (GridWidth * GridHeight);
        var slots = new ItemStack?[room + 1];
        slots[0] = new ItemStack(TestBags.TurnipSeed, null, 12, 1);

        var e = Assert.Throws<InvalidDataException>(
            () => SaveFile.ReadFormalSave(RegistryFor(world.Plots), FlatBagSave(world, slots)));

        Assert.Contains($"记的背包有 {room + 1} 格", e.Message);
        Assert.Contains($"现在的背包只有 {room} 格", e.Message);
    }

    /// <summary>改成网格之前那一版的背包里，空格却记着东西：照那一版的规矩被拒，点名第几格。</summary>
    [Fact]
    public void 改成网格之前那一版的背包里空格却记着东西就被拒并点名()
    {
        var world = BusyWorld();
        var saved = JsonNode.Parse(FlatBagSave(world, new ItemStack?[CarryCells + 2]))!.AsObject();
        saved[SaveFile.ShardsKey]![WorldShards.BagId]!["count"]![CarryCells + 1] = 3;

        var e = Assert.Throws<InvalidDataException>(
            () => SaveFile.ReadFormalSave(RegistryFor(world.Plots), saved.ToJsonString()));

        Assert.Contains($"第 {CarryCells + 2} 格是空格", e.Message);
    }

    /// <summary>同一版的存档缺背包那一片：被拒，那不是旧档，是被写坏了。</summary>
    [Fact]
    public void 同一版的存档缺背包那一片就被拒()
    {
        var world = BusyWorld();
        var root = JsonNode.Parse(FormalSaveOf(world))!.AsObject();
        root[SaveFile.ShardsKey]!.AsObject().Remove(WorldShards.BagId);

        var e = Assert.Throws<SaveRejectedException>(
            () => SaveFile.ReadFormalSave(RegistryFor(world.Plots), root.ToJsonString()));

        Assert.Equal(SaveRejection.MissingField, e.Reason);
        Assert.Contains(WorldShards.BagId, e.Message);
    }

    /// <summary>同一版的存档缺网格里那几堆的位置：被拒，那不是旧档，是被写坏了。网格里的东西不按项数推位置。</summary>
    [Fact]
    public void 同一版的存档缺网格里那几堆的位置就被拒()
    {
        var world = BusyWorld();
        var root = JsonNode.Parse(FormalSaveOf(world))!.AsObject();
        Assert.True(root[SaveFile.ShardsKey]![WorldShards.BagId]!.AsObject().Remove("gridX"));

        var e = Assert.Throws<SaveRejectedException>(
            () => SaveFile.ReadFormalSave(RegistryFor(world.Plots), root.ToJsonString()));

        Assert.Equal(SaveRejection.MissingField, e.Reason);
        Assert.Contains("「gridX」", e.Message);
    }

    /// <summary>追加内容永远是兼容的那一种：存档里存的是标识，多一种作物不碰任何一个已经存下的标识。</summary>
    [Fact]
    public void 只往作物定义里加一种作物旧存档照样打得开()
    {
        var world = BusyWorld();
        var text = FormalSaveOf(world);
        var moreCrops = new Dictionary<string, CropDefinition>(Crops)
        {
            ["radish"] = Turnip with { Id = "radish", YieldItemId = "item_radish" },
        };

        var back = WorldShards.Read(SaveFile.ReadFormalSave(RegistryFor(world.Plots), text), moreCrops, NewGameBag());

        AssertSameWorld(world, back);
    }
}
