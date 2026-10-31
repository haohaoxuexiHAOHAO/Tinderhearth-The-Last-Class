using System.Reflection;
using System.Text.Json.Nodes;
using Tinderhearth.Rules.Economy;
using Tinderhearth.Rules.Foundation.Save;
using Xunit;

namespace Tinderhearth.Rules.Tests.Economy;

/// <summary>日历、那片地与主角背包那几个分片：存进去读出来一样、读的先后不影响结果、记得矛盾时点名哪一格。</summary>
/// <remarks>作物的阶段天数与荒置天数都是测试用值，改它们不该让任何一条失败。</remarks>
public class WorldShardsTests
{
    private const int FallowDays = 7;

    /// <summary>现在配置的背包格数，测试用值。</summary>
    private const int BagSlots = 6;

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

    /// <summary>开新档时的背包：空的、现在配置的格数。</summary>
    private static Bag NewGameBag() => TestBags.Empty(BagSlots);

    /// <summary>
    /// 一片 3×3 的地，左上角在 (-1, 2)。按行：在长且浇过、待收且变异、收过一茬又在长且浇过；
    /// 枯死、荒置了一天、未清理；可耕、空着的已锄、待收没变异。背包里有变异与寻常两堆芜菁、一堆种子，中间隔着空格。
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

        var bag = Bag.Restore(
            [
                new ItemStack(TestBags.Turnip, TestBags.Mutated, 3, 2),
                null,
                new ItemStack(TestBags.TurnipSeed, null, 12, 4),
                new ItemStack(TestBags.Turnip, TestBags.Common, 20, 3),
                null,
                null,
            ],
            TestBags.Items);

        return new WorldState(new GameDate(2, Season.Summer, 17), PlotGrid.FromRows(new GridCell(-1, 2), 3, 3, p), bag);
    }

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
        Assert.Null(back.Bag.Slots[1]);
        Assert.Equal(TestBags.Mutated, back.Bag.Slots[0]?.QualityId);
    }

    /// <summary>读回来之后再进东西，序号接着各堆里最大的那个往后排，不从头数。</summary>
    [Fact]
    public void 读回来的背包序号接着往后排()
    {
        var back = WorldShards.Read(ShardsOf(FormalSaveOf(BusyWorld())), Crops, NewGameBag());

        Assert.True(back.Bag.TryAdd(TestBags.TurnipSeed, null, 1));

        Assert.Equal(5, back.Bag.Slots[2]?.AcquiredSequence);
    }

    /// <summary>
    /// 接着排的那个序号是从各堆算出来的，不是另存的一格：改掉存档里一堆的序号再读回来，新进的东西跟着排到它后面。
    /// 另存一格的实现过不了这一条（设计仓 design/存档系统.md 的「派生量不单独存」一节）。
    /// </summary>
    [Fact]
    public void 改了存档里一堆的序号新进的东西跟着排到它后面()
    {
        var bag = WorldShards.WriteBag(BusyWorld().Bag);
        bag["acquired"]![0] = 40;

        var back = WorldShards.ReadBag(bag, NewGameBag());
        Assert.True(back.TryAdd(TestBags.TurnipSeed, null, 1));

        Assert.Equal(41, back.Slots[2]?.AcquiredSequence);
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
    /// 给一堆加一项而不存它，这一条失败：那一项会在读档之后悄悄回到初始值。
    /// 分片里也只有这几串：格数与接着排的序号由它们算得出来，另存一格这一条也失败。
    /// </summary>
    [Fact]
    public void 背包分片存的正是一堆的全部字段()
    {
        string[] stackFields = ["ItemId", "QualityId", "Count", "AcquiredSequence"];
        var stackProperties = typeof(ItemStack).GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => p.Name);

        Assert.Equal(stackFields.Order(), stackProperties.Order());
        Assert.Equal(WorldShards.BagSlotKeys.Count, stackFields.Length);
        Assert.Equal(WorldShards.BagSlotKeys.Order(), WorldShards.WriteBag(BusyWorld().Bag).Select(p => p.Key).Order());
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

    /// <summary>存档里有物品定义里找不到的标识：被拒，点名哪一格、哪个标识。</summary>
    [Fact]
    public void 背包里认不出的物品被拒并点名哪一格与哪个标识()
    {
        var bag = WorldShards.WriteBag(BusyWorld().Bag);
        bag["itemId"]![2] = "item_ghost";

        var e = Assert.Throws<InvalidDataException>(() => WorldShards.ReadBag(bag, NewGameBag()));

        Assert.Contains("第 3 格", e.Message);
        Assert.Contains("item_ghost", e.Message);
    }

    /// <summary>存档里的背包比现在配置的格数多：被拒，说清两边各是几格。</summary>
    [Fact]
    public void 存档里的背包格数比配置多就被拒并说清两边各几格()
    {
        var saved = WorldShards.WriteBag(BusyWorld().Bag);

        var e = Assert.Throws<InvalidDataException>(() => WorldShards.ReadBag(saved, TestBags.Empty(BagSlots - 2)));

        Assert.Contains($"{BagSlots} 格", e.Message);
        Assert.Contains($"{BagSlots - 2} 格", e.Message);
    }

    /// <summary>存档里的背包比现在配置的格数少：读得进来，原来那几格不动，后面补空格。</summary>
    [Fact]
    public void 存档里的背包格数比配置少就在后面补空格()
    {
        var world = BusyWorld();

        var back = WorldShards.ReadBag(WorldShards.WriteBag(world.Bag), TestBags.Empty(BagSlots + 3));

        Assert.Equal(BagSlots + 3, back.SlotCount);
        Assert.Equal(world.Bag.Slots, back.Slots.Take(BagSlots));
        Assert.All(back.Slots.Skip(BagSlots), slot => Assert.Null(slot));
    }

    /// <summary>空格那一格却记着件数、品质或序号：被拒，点名哪一格。</summary>
    [Theory]
    [InlineData("count", "3")]
    [InlineData("qualityId", "\"common\"")]
    [InlineData("acquired", "1")]
    public void 空格却记着东西就被拒并点名(string key, string valueJson)
    {
        var bag = WorldShards.WriteBag(BusyWorld().Bag);
        bag[key]![1] = JsonNode.Parse(valueJson);

        var e = Assert.Throws<InvalidDataException>(() => WorldShards.ReadBag(bag, NewGameBag()));

        Assert.Contains("第 2 格", e.Message);
    }

    /// <summary>一堆的件数超过那种物品的堆上限、或者品质那一位与物品的声明不符：被拒，点名哪一格。</summary>
    [Theory]
    [InlineData("count", "21", "第 4 格")]
    [InlineData("qualityId", "null", "第 1 格")]
    public void 一堆记得与物品定义对不上就被拒并点名(string key, string valueJson, string named)
    {
        var bag = WorldShards.WriteBag(BusyWorld().Bag);
        var slot = int.Parse(named[2..^2].Trim()) - 1;
        bag[key]![slot] = JsonNode.Parse(valueJson);

        var e = Assert.Throws<InvalidDataException>(() => WorldShards.ReadBag(bag, NewGameBag()));

        Assert.Contains(named, e.Message);
    }

    [Fact]
    public void 一堆的序号不为正就被拒并点名()
    {
        var bag = WorldShards.WriteBag(BusyWorld().Bag);
        bag["acquired"]![3] = 0;

        var e = Assert.Throws<InvalidDataException>(() => WorldShards.ReadBag(bag, NewGameBag()));

        Assert.Contains("第 4 格", e.Message);
    }

    /// <summary>格数取种类那一串的项数：别的哪一串长短不一样，被拒并点名那一串。</summary>
    [Fact]
    public void 背包几串不一样长就被拒并点名哪一串()
    {
        var bag = WorldShards.WriteBag(BusyWorld().Bag);
        bag["count"]!.AsArray().RemoveAt(5);

        var e = Assert.Throws<InvalidDataException>(() => WorldShards.ReadBag(bag, NewGameBag()));

        Assert.Contains("「count」", e.Message);
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
    /// 上一版的存档没有背包那一片：读进来补一个空背包，日期与每一格地照旧。
    /// </summary>
    /// <remarks>上一版写出来的就是现在这样少了背包那一片、版本号小一的那一份；读得进来也说明最低兼容版本不高于上一版。</remarks>
    [Fact]
    public void 上一版的存档读进来补一个空背包()
    {
        var world = BusyWorld();
        var root = JsonNode.Parse(FormalSaveOf(world))!.AsObject();
        root[SaveFile.VersionKey] = SaveFile.CurrentVersion - 1;
        root[SaveFile.ShardsKey]!.AsObject().Remove(WorldShards.BagId);

        var back = WorldShards.Read(SaveFile.ReadFormalSave(RegistryFor(world.Plots), root.ToJsonString()), Crops, NewGameBag());

        Assert.Equal(world.Date, back.Date);
        Assert.Equal(PlotSnapshot.Of(world), PlotSnapshot.Of(back));
        Assert.Equal(BagSlots, back.Bag.EmptySlotCount);
        Assert.Equal(BagSlots, back.Bag.SlotCount);
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
