using System.Reflection;
using System.Text.Json.Nodes;
using Tinderhearth.Rules.Economy;
using Tinderhearth.Rules.Foundation.Save;
using Xunit;

namespace Tinderhearth.Rules.Tests.Economy;

/// <summary>日历与那片地两个分片：存进去读出来一样、读的先后不影响结果、记得矛盾时点名哪一格。</summary>
/// <remarks>作物的阶段天数与荒置天数都是测试用值，改它们不该让任何一条失败。</remarks>
public class WorldShardsTests
{
    private const int FallowDays = 7;

    private static readonly CropDefinition Turnip = new()
    {
        Id = "turnip",
        StageDays = [1, 1],
        RegrowFromStage = null,
        Seasons = [Season.Spring],
        YieldItemId = "item_turnip",
    };

    /// <summary>能反复收的：收完退回第 1 阶段再长。</summary>
    private static readonly CropDefinition Berry = new()
    {
        Id = "berry",
        StageDays = [1, 2, 1],
        RegrowFromStage = 1,
        Seasons = [Season.Spring, Season.Summer],
        YieldItemId = "item_berry",
    };

    private static readonly IReadOnlyDictionary<string, CropDefinition> Crops =
        new Dictionary<string, CropDefinition> { [Turnip.Id] = Turnip, [Berry.Id] = Berry };

    private static readonly HarvestYieldRule Yield = new(BaseCount: 10, MinWaterFactor: 0.5, MaxWaterFactor: 1.0);

    /// <summary>
    /// 一片 3×3 的地，左上角在 (-1, 2)。按行：在长且浇过、待收且变异、收过一茬又在长且浇过；
    /// 枯死、荒置了一天、未清理；可耕、空着的已锄、待收没变异。
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

        return new WorldState(new GameDate(2, Season.Summer, 17), PlotGrid.FromRows(new GridCell(-1, 2), 3, 3, p));
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

    private static string FormalSaveOf(WorldState world)
    {
        var registry = new SaveShardRegistry();
        WorldShards.Register(registry, world.Plots);
        return SaveFile.WriteFormalSave(registry, WorldShards.Write(world));
    }

    private static void AssertSameWorld(WorldState expected, WorldState actual)
    {
        Assert.Equal(expected.Date, actual.Date);
        Assert.Equal(
            (expected.Plots.Origin, expected.Plots.Width, expected.Plots.Height),
            (actual.Plots.Origin, actual.Plots.Width, actual.Plots.Height));
        Assert.Equal(PlotSnapshot.Of(expected), PlotSnapshot.Of(actual));
    }

    [Fact]
    public void 日历与地块存进去读出来完全一致()
    {
        var world = BusyWorld();

        var back = WorldShards.Read(ShardsOf(FormalSaveOf(world)), Crops);

        AssertSameWorld(world, back);
        Assert.Contains(back.Plots.Cells, c => c.Plot.Mutated);
        Assert.Contains(back.Plots.Cells, c => c.Plot.WateredToday);
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

        var forward = WorldShards.Read(shards, Crops);

        AssertSameWorld(forward, WorldShards.Read(reversed, Crops));
        AssertSameWorld(forward, WorldShards.Read(shuffledFields, Crops));
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

    [Fact]
    public void 一份世界只写日历与地块两个分片()
    {
        string[] expected = [WorldShards.CalendarId, WorldShards.PlotsId];

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

        var e = Assert.Throws<InvalidDataException>(() => WorldShards.Read(calendarOnly, Crops));

        Assert.Contains(WorldShards.PlotsId, e.Message);
    }

    /// <summary>注册之后场景那片地再怎么变，默认值也还是注册那一刻的起始状态。</summary>
    [Fact]
    public void 两个分片的存档默认值就是开新档时那一份()
    {
        var scene = PlotGrid.Rectangle(new GridCell(4, 5), width: 2, height: 3, PlotState.Tilled);
        var newGame = WorldState.NewGame(scene.Clone());
        var registry = new SaveShardRegistry();
        string[] ids = [WorldShards.CalendarId, WorldShards.PlotsId];

        WorldShards.Register(registry, scene);
        scene.Cells.First().Plot.Plant(Turnip);
        var fromDefaults = WorldShards.Read(registry.Shards.ToDictionary(s => s.Id, s => s.Default()), Crops);

        Assert.Equal(ids, registry.Shards.Select(s => s.Id));
        Assert.All(registry.Shards, s => Assert.Equal(SaveSide.FormalSave, s.Side));
        AssertSameWorld(newGame, fromDefaults);
    }
}
