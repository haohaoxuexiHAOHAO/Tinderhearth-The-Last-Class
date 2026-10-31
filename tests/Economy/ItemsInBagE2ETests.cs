using System.Text.Json.Nodes;
using Tinderhearth.Rules.Economy;
using Tinderhearth.Rules.Foundation.Config;
using Tinderhearth.Rules.Foundation.Save;
using Tinderhearth.Rules.Progression;
using Tinderhearth.Rules.Tests.Progression;
using Tinderhearth.Rules.UI;
using Xunit;

namespace Tinderhearth.Rules.Tests.Economy;

/// <summary>东西能拿在手上那份需求的端到端：开新档、放一批种子、从背包播下去、推进到成熟、收进背包、写档读回。</summary>
/// <remarks>
/// 作物、物品与配置读的都是工程里那一份（只读不写），走的是农场用的那几个读口：开新档与读档、调试键那一批、
/// 从背包播种、结算驱动、收获那一道闸。变异的随机源由测试按顺序给，写档写进内存、不碰真文件。各一环自己的边界
/// 在各自的测试类里，这里只把它们串起来。
/// </remarks>
public class ItemsInBagE2ETests
{
    private const int FallowDays = 7;

    private static readonly HarvestYieldRule Yield = new(BaseCount: 4, MinWaterFactor: 0.5, MaxWaterFactor: 1.0);

    /// <summary>播下去的那两格。</summary>
    private static readonly GridCell First = new(4, 2);
    private static readonly GridCell Second = new(5, 2);

    [Fact]
    public void 主路径_开新档放种子播两格推进到成熟一变一不变_收成两堆进背包_写档读回背包日期与每一格都一致()
    {
        var farm = new Farm(rolls: [true, false]);
        var world = farm.NewGame();
        Assert.Equal(world.State.Bag.SlotCount, world.State.Bag.EmptySlotCount);

        // 调试键放下第一批：随身栏是背包开头那几格，第一格是第一样种子的一满堆。
        Assert.True(world.State.Bag.TryAddAll(SeedBatches.Batch(farm.Crops.Values, 0, CarrySlotBar.SlotCount, farm.Items)));
        var seed = world.State.Bag.Slots[0]!.Value;
        var crop = CropDefinition.BySeed(farm.Crops)[seed.ItemId];

        // 从背包第一格播两格：每播一颗那一格少一颗。
        Assert.Equal(PlantResult.Planted, farm.PlantFromFirstSlot(world, First));
        Assert.Equal(PlantResult.Planted, farm.PlantFromFirstSlot(world, Second));
        Assert.Equal(seed.Count - 2, world.State.Bag.Slots[0]?.Count);

        for (var night = 0; night < crop.DaysToFirstRipe; night++)
        {
            Assert.True(farm.Sleep(world).Advanced);
        }

        var plots = new[] { PlotAt(world.State, First), PlotAt(world.State, Second) };
        Assert.All(plots, plot => Assert.Equal(PlotState.Harvestable, plot.State));
        Assert.Equal(1, plots.Count(plot => plot.Mutated));

        // 收进背包：变异与没变的各是一堆，地退回已锄。
        foreach (var plot in plots)
        {
            Assert.Equal(HarvestStatus.Stored, Harvesting.ByProtagonist(plot, crop, Yield, world.State.Bag).Status);
            Assert.Equal(PlotState.Tilled, plot.State);
        }

        var harvested = world.State.Bag.Slots.OfType<ItemStack>().Where(s => s.ItemId == crop.YieldItemId).ToList();
        string?[] qualities = [Harvesting.MutatedQualityId, Harvesting.UnmutatedQualityId];
        Assert.Equal(qualities.Order(StringComparer.Ordinal), harvested.Select(s => s.QualityId).Order(StringComparer.Ordinal));

        // 停掉游戏再进来：读回写出的那一份。
        var saved = farm.Write(world.State);
        var back = farm.Load(saved);
        Assert.Equal(world.State.Date, back.Date);
        Assert.Equal(PlotSnapshot.Of(world.State), PlotSnapshot.Of(back));
        Assert.Equal(world.State.Bag.Slots, back.Bag.Slots);
    }

    [Fact]
    public void 失败路径_背包满时收获被拒_那一格照旧待收_背包一件不变()
    {
        var farm = new Farm(rolls: [false]);
        var world = farm.NewGame();
        var crop = farm.Crops.Values.OrderBy(c => c.Id, StringComparer.Ordinal).First();
        Assert.True(PlotAt(world.State, First).Plant(crop));
        for (var night = 0; night < crop.DaysToFirstRipe; night++)
        {
            Assert.True(farm.Sleep(world).Advanced);
        }

        // 每一格都是一满堆种子：没有可合的同标识堆，也没有空格。
        var bag = world.State.Bag;
        var seeds = farm.Items.Items.Values.Where(i => !i.HasQuality).OrderBy(i => i.Id, StringComparer.Ordinal).ToList();
        for (var slot = 0; slot < bag.SlotCount; slot++)
        {
            var kind = seeds[slot % seeds.Count];
            Assert.True(bag.TryAdd(kind.Id, null, kind.StackLimit));
        }

        Assert.Equal(0, bag.EmptySlotCount);
        var before = bag.Slots.ToList();
        var plot = PlotAt(world.State, First);

        var attempt = Harvesting.ByProtagonist(plot, crop, Yield, bag);

        Assert.Equal(HarvestStatus.BagFull, attempt.Status);
        Assert.Equal(PlotState.Harvestable, plot.State);
        Assert.Equal(before, bag.Slots);
    }

    [Fact]
    public void 失败路径_同一版的存档缺背包那一片被拒()
    {
        var farm = new Farm(rolls: []);
        var root = JsonNode.Parse(farm.Write(farm.NewGame().State))!.AsObject();
        Assert.True(root[SaveFile.ShardsKey]!.AsObject().Remove(WorldShards.BagId));

        var e = Assert.Throws<SaveRejectedException>(() => farm.Load(root.ToJsonString()));

        Assert.Equal(SaveRejection.MissingField, e.Reason);
        Assert.Contains(WorldShards.BagId, e.Message);
    }

    [Fact]
    public void 失败路径_存档里有认不出的物品标识被拒并点名它()
    {
        var farm = new Farm(rolls: []);
        var world = farm.NewGame();
        Assert.True(world.State.Bag.TryAddAll(SeedBatches.Batch(farm.Crops.Values, 0, CarrySlotBar.SlotCount, farm.Items)));
        var root = JsonNode.Parse(farm.Write(world.State))!.AsObject();

        // 背包那一片里物品标识那一串的第一格，换成一个不在册的标识。
        var itemIds = root[SaveFile.ShardsKey]![WorldShards.BagId]![WorldShards.BagSlotKeys[0]]!.AsArray();
        itemIds[0] = "item_never_heard_of";

        var e = Assert.Throws<InvalidDataException>(() => farm.Load(root.ToJsonString()));

        Assert.Contains("item_never_heard_of", e.Message);
    }

    [Fact]
    public void 失败路径_选中收获物去播被拒_背包与那一格都不动()
    {
        var farm = new Farm(rolls: []);
        var world = farm.NewGame();
        var crop = farm.Crops.Values.OrderBy(c => c.Id, StringComparer.Ordinal).First();
        Assert.True(world.State.Bag.TryAdd(crop.YieldItemId, Harvesting.UnmutatedQualityId, 3));
        var before = world.State.Bag.Slots.ToList();

        Assert.Equal(PlantResult.NotASeed, farm.PlantFromFirstSlot(world, First));

        Assert.Equal(before, world.State.Bag.Slots);
        Assert.Equal(PlotState.Tilled, PlotAt(world.State, First).State);
    }

    /// <summary>农场那一套：工程里的作物、物品与配置，场景里一片地，写档写进内存。</summary>
    private sealed class Farm
    {
        private readonly Queue<bool> _rolls;

        public Farm(IEnumerable<bool> rolls)
        {
            _rolls = new Queue<bool>(rolls);
            Items = ShippedItems.Load();
            Crops = CropSheetE2ETests.ShippedCrops();
            var config = GameConfig.Parse(File.ReadAllText(Path.Combine(RepoFiles.Root(), "data", "config", "game.json")));
            NewGameBag = Bag.NewGame(config, Items);
            Settlement = StandardSettlement.Create(Crops, FallowDays, () => _rolls.Dequeue(), _ => { }, _ => { });
        }

        /// <summary>场景里那片地：2×2，左上角不在原点，读回来时格坐标真的被核到。</summary>
        public PlotGrid Scene { get; } = PlotGrid.Rectangle(new GridCell(4, 2), width: 2, height: 2, PlotState.Tilled);

        public ItemCatalog Items { get; }

        public IReadOnlyDictionary<string, CropDefinition> Crops { get; }

        /// <summary>开新档时的背包：第一级的格数，空的。</summary>
        public Bag NewGameBag { get; }

        public DailySettlement Settlement { get; }

        /// <summary>还没有存档时进农场。</summary>
        public CurrentWorld NewGame() => new(Load(null));

        /// <summary>睡一觉：走结算那唯一的入口。</summary>
        public SettlementOutcome Sleep(CurrentWorld world) => Settlement.EndDay(world, DayEndSource.Sleep);

        /// <summary>手上是背包第一格（随身栏选中第一格），播到给定那一格。</summary>
        public PlantResult PlantFromFirstSlot(CurrentWorld world, GridCell cell) =>
            Planting.TryPlantFromBag(PlotAt(world.State, cell), world.State.Bag, 0, CropDefinition.BySeed(Crops).GetValueOrDefault);

        public string Write(WorldState world) => FarmSave.Write(Registry(), world);

        public WorldState Load(string? saved) => FarmSave.LoadOrNew(saved, Registry(), Crops, Scene, NewGameBag);

        private SaveShardRegistry Registry()
        {
            var registry = new SaveShardRegistry();
            WorldShards.Register(registry, Scene, NewGameBag);
            return registry;
        }
    }

    private static Plot PlotAt(WorldState world, GridCell cell) =>
        world.Plots.TryGet(cell, out var plot) ? plot : throw new InvalidOperationException($"没有 {cell} 这一格");
}
