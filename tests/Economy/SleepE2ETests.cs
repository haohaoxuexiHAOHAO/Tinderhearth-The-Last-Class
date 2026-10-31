using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Tinderhearth.Rules.Economy;
using Tinderhearth.Rules.Foundation.Content;
using Tinderhearth.Rules.Foundation.Save;
using Tinderhearth.Rules.Foundation.Text;
using Xunit;

namespace Tinderhearth.Rules.Tests.Economy;

/// <summary>睡一觉能推进一天那份需求的端到端：开新档、种下去、一夜夜推进、醒来那份摘要、写出存档、读回来。</summary>
/// <remarks>
/// 作物与文本表读的都是工程里那一份（只读不写），走的是农场用的那几个读口：开新档与读档、结算驱动、窗里写什么。
/// 写档接在测试自己的写入口上，写出来的文本留在内存里，不碰真文件；变异的随机源由测试给。各一环自己的边界在
/// 各自的测试类里，这里只把它们串起来。窗上的措辞不在这里钉：期望值照文本表里那一条的写法算出来，作者改字不该让这里红。
/// </remarks>
public class SleepE2ETests
{
    private const int FallowDays = 7;
    private const string SavePath = "D:/saves/slot_01.json";

    /// <summary>种下去的那一格。</summary>
    private static readonly GridCell Seeded = new(5, 3);

    private static readonly TextCatalog Text = ShippedText();

    [Fact]
    public void 主路径_开新档播种推进到成熟变异必中_摘要列出那一格_写出的存档读回来日期与每一格都一致()
    {
        var crops = CropSheetE2ETests.ShippedCrops();
        var crop = crops.Values.OrderBy(c => c.Id, StringComparer.Ordinal).First();
        var room = new Bedroom(crops, rollMutation: () => true);
        var world = room.NewGame();
        Assert.Equal(Calendar.StartDate, world.State.Date);
        Assert.True(PlotAt(world.State, Seeded).Plant(crop));

        SettlementOutcome outcome = null!;
        for (var night = 0; night < crop.DaysToFirstRipe; night++)
        {
            outcome = room.Sleep(world);
            Assert.True(outcome.Advanced);
        }

        var plot = PlotAt(world.State, Seeded);
        Assert.Equal(PlotState.Harvestable, plot.State);
        Assert.True(plot.Mutated);

        // 每一夜醒来都交出一份摘要，只有长成那一夜有变化：那一格变异。
        Assert.Equal(crop.DaysToFirstRipe, room.Woke.Count);
        Assert.All(room.Woke.SkipLast(1), summary => Assert.False(summary.HasChanges));
        var lastNight = room.Woke[^1];
        Assert.Equal([new CropChange(Seeded, crop.Id, CropChangeKind.Mutated)], lastNight.CropChanges);

        // 醒来那个窗：最上面一行是那一夜结算的那一天，下面那一项照文本表的写法。
        var report = WakeReport.For(outcome, Text, SavePath);
        Assert.True(report.Advanced);
        Assert.Equal(WakeReport.DateText(lastNight.SettledDate, Text), report.Title);
        Assert.Equal([Line(WakeReport.MutatedKey, 1)], report.Lines);

        // 停掉游戏再进来：读回最后写出的那一份。
        var reloaded = room.Reload();
        Assert.Equal(world.State.Date, reloaded.Date);
        Assert.Equal(PlotSnapshot.Of(world.State), PlotSnapshot.Of(reloaded));
    }

    [Fact]
    public void 失败路径_写档失败时世界与存档都不动_摘要交出零次_处理好之后再睡一次就推进了()
    {
        var crops = CropSheetE2ETests.ShippedCrops();
        var room = new Bedroom(crops, rollMutation: () => false);
        var world = room.NewGame();
        Assert.True(PlotAt(world.State, Seeded).Plant(crops.Values.First()));
        Assert.True(room.Sleep(world).Advanced);

        var savedBefore = room.Saved;
        var dateBefore = world.State.Date;
        var plotsBefore = PlotSnapshot.Of(world.State);
        var wokeBefore = room.Woke.Count;

        room.WriteFails = true;
        var outcome = room.Sleep(world);

        Assert.False(outcome.Advanced);
        Assert.Equal(SettlementSteps.AutoSave, outcome.FailedStep);
        Assert.Equal(dateBefore, world.State.Date);
        Assert.Equal(plotsBefore, PlotSnapshot.Of(world.State));
        Assert.Same(savedBefore, room.Saved);
        Assert.Equal(wokeBefore, room.Woke.Count);
        Assert.Equal(dateBefore, room.Reload().Date);

        var report = WakeReport.For(outcome, Text, SavePath);
        Assert.False(report.Advanced);
        Assert.Equal(Text[WakeReport.SaveFailedKey], report.Title);
        Assert.Contains(Line(WakeReport.SavePathKey, SavePath), report.Lines);

        room.WriteFails = false;
        Assert.True(room.Sleep(world).Advanced);
        Assert.Equal(dateBefore.Next(), world.State.Date);
    }

    [Fact]
    public void 失败路径_同版本缺字段被拒_不开新档()
    {
        var crops = CropSheetE2ETests.ShippedCrops();
        var room = new Bedroom(crops, rollMutation: () => false);
        Assert.True(room.Sleep(room.NewGame()).Advanced);

        var root = JsonNode.Parse(room.Saved!)!.AsObject();
        var calendar = root[SaveFile.ShardsKey]![WorldShards.CalendarId]!.AsObject();
        var dropped = calendar.First().Key;
        Assert.True(calendar.Remove(dropped));

        var e = Assert.Throws<SaveRejectedException>(
            () => FarmSave.LoadOrNew(root.ToJsonString(), RegistryFor(room.Scene), crops, room.Scene));

        Assert.Equal(SaveRejection.MissingField, e.Reason);
        Assert.Contains($"「{dropped}」", e.Message);
    }

    [Fact]
    public void 失败路径_换季那天不当季的作物枯死_进了摘要也进了存档()
    {
        var springOnly = SpringOnly();
        var crops = new Dictionary<string, CropDefinition> { [springOnly.Id] = springOnly };
        var room = new Bedroom(crops, rollMutation: () => false);
        var lastDayOfSpring = new GameDate(1, Season.Spring, Calendar.DaysPerSeason);
        var world = new CurrentWorld(new WorldState(lastDayOfSpring, room.NewGame().State.Plots));
        Assert.True(PlotAt(world.State, Seeded).Plant(springOnly));

        var outcome = room.Sleep(world);

        Assert.True(outcome.Advanced);
        Assert.Equal(lastDayOfSpring.Next(), world.State.Date);
        Assert.Equal(Season.Summer, world.State.Date.Season);
        Assert.Equal(PlotState.Withered, PlotAt(world.State, Seeded).State);
        Assert.Equal([new CropChange(Seeded, springOnly.Id, CropChangeKind.Withered)], room.Woke.Single().CropChanges);
        Assert.Equal([Line(WakeReport.WitheredKey, 1)], WakeReport.For(outcome, Text, SavePath).Lines);
        Assert.Equal(PlotState.Withered, PlotAt(room.Reload(), Seeded).State);
    }

    /// <summary>睡一觉那一套：写档写进 <see cref="Saved"/>、醒来交出的摘要记进 <see cref="Woke"/>，不碰真文件。</summary>
    private sealed class Bedroom
    {
        public Bedroom(IReadOnlyDictionary<string, CropDefinition> crops, Func<bool> rollMutation)
        {
            Crops = crops;
            Registry = RegistryFor(Scene);
            Settlement = StandardSettlement.Create(crops, FallowDays, rollMutation, Write, Woke.Add);
        }

        /// <summary>场景里那片地：3×2，左上角不在原点，存进去读回来时格坐标真的被核到。</summary>
        public PlotGrid Scene { get; } = PlotGrid.Rectangle(new GridCell(4, 2), width: 3, height: 2, PlotState.Tilled);

        public IReadOnlyDictionary<string, CropDefinition> Crops { get; }

        public SaveShardRegistry Registry { get; }

        public DailySettlement Settlement { get; }

        /// <summary>每一夜醒来交出的摘要，按先后。</summary>
        public List<SettlementSummary> Woke { get; } = [];

        /// <summary>最后写成的那一份存档；一次都没写成时为 <c>null</c>。</summary>
        public string? Saved { get; private set; }

        /// <summary>为真时写档那一下抛出，就像存档文件设成了只读。</summary>
        public bool WriteFails { get; set; }

        /// <summary>还没有存档时进农场：按开局那一天与场景里那片地开新档。</summary>
        public CurrentWorld NewGame() => new(FarmSave.LoadOrNew(null, Registry, Crops, Scene));

        /// <summary>睡一觉：走结算那唯一的入口。</summary>
        public SettlementOutcome Sleep(CurrentWorld world) => Settlement.EndDay(world, DayEndSource.Sleep);

        /// <summary>停掉游戏再进来：用最后写成的那一份读回来。</summary>
        public WorldState Reload() => FarmSave.LoadOrNew(Saved, RegistryFor(Scene), Crops, Scene);

        private void Write(WorldState world)
        {
            if (WriteFails)
            {
                throw new IOException("写不进去");
            }

            Saved = FarmSave.Write(Registry, world);
        }
    }

    private static SaveShardRegistry RegistryFor(PlotGrid scene)
    {
        var registry = new SaveShardRegistry();
        WorldShards.Register(registry, scene);
        return registry;
    }

    private static Plot PlotAt(WorldState world, GridCell cell) =>
        world.Plots.TryGet(cell, out var plot) ? plot : throw new InvalidOperationException($"没有 {cell} 这一格");

    /// <summary>文本表里那一条照它的写法填上数。</summary>
    /// <remarks>区域写全名：引进 System.Globalization 的话，它那个 Calendar 与日历撞名。</remarks>
    private static string Line(string key, object value) =>
        string.Format(System.Globalization.CultureInfo.InvariantCulture, Text[key], value);

    /// <summary>工程里第一种作物，季节列表改成只在春季，走的仍是同一个解析。</summary>
    private static CropDefinition SpringOnly()
    {
        var (path, text) = CropSheetE2ETests.ShippedCropFiles().First();
        var crop = CropDefinition.Parse(
            Regex.Replace(text, "\"seasons\"\\s*:\\s*\\[[^\\]]*\\]", "\"seasons\": [\"Spring\"]"), path);
        Assert.Equal([Season.Spring], crop.Seasons);
        return crop;
    }

    private static TextCatalog ShippedText()
    {
        var path = Path.Combine(RepoFiles.Root(), "data", "text", "zh-CN.json");
        return new TextCatalog(ContentJson.Parse<Dictionary<string, string>>(File.ReadAllText(path), path));
    }
}
