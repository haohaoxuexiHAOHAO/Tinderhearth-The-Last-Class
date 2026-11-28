using System.Text.Json.Nodes;
using Tinderhearth.Rules.Economy;
using Tinderhearth.Rules.Foundation.Save;
using Tinderhearth.Rules.UI;
using Xunit;

namespace Tinderhearth.Rules.Tests.Economy;

/// <summary>东西能拿在手上那份需求的端到端：开新档、放一批种子、从背包播下去、推进到成熟、收进背包、写档读回。</summary>
/// <remarks>
/// 作物、物品与配置读的都是工程里那一份（只读不写），走的是基地用的那几个读口：开新档与读档、调试键那一批、
/// 从背包播种、结算驱动、收获那一道闸。变异的随机源由测试按顺序给，写档写进内存、不碰真文件。各一环自己的边界
/// 在各自的测试类里，这里只把它们串起来。基地那一套搭在 <see cref="BaseSetup"/>。
/// </remarks>
public class ItemsInBagE2ETests
{
    private static readonly HarvestYieldRule Yield = new(BaseCount: 4, MinWaterFactor: 0.5, MaxWaterFactor: 1.0);

    /// <summary>播下去的那两格。</summary>
    private static readonly GridCell First = new(4, 2);
    private static readonly GridCell Second = new(5, 2);

    [Fact]
    public void 主路径_开新档放种子播两格推进到成熟一变一不变_收成两堆进背包_写档读回背包日期与每一格都一致()
    {
        var setup = new BaseSetup(rolls: [true, false]);
        var world = setup.NewGame();
        Assert.True(world.State.Bag.IsEmpty);

        // 调试键放下第一批：先落进随身栏那一排，第一格是第一样种子的一满堆。
        Assert.True(world.State.Bag.TryAddAll(SeedBatches.Batch(setup.Crops.Values, 0, CarrySlotBar.SlotCount, setup.Items)));
        var seed = world.State.Bag.Carry[0]!.Value;
        var crop = CropDefinition.BySeed(setup.Crops)[seed.ItemId];

        // 从随身栏第一格播两格：每播一颗那一格少一颗。
        Assert.Equal(PlantResult.Planted, setup.PlantFromFirstSlot(world, First));
        Assert.Equal(PlantResult.Planted, setup.PlantFromFirstSlot(world, Second));
        Assert.Equal(seed.Count - 2, world.State.Bag.Carry[0]?.Count);

        for (var night = 0; night < crop.DaysToFirstRipe; night++)
        {
            Assert.True(setup.Sleep(world).Advanced);
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

        var harvested = world.State.Bag.Carry.Concat(world.State.Bag.Slots)
            .OfType<ItemStack>().Where(s => s.ItemId == crop.YieldItemId).ToList();
        string?[] qualities = [Harvesting.MutatedQualityId, Harvesting.UnmutatedQualityId];
        Assert.Equal(qualities.Order(StringComparer.Ordinal), harvested.Select(s => s.QualityId).Order(StringComparer.Ordinal));

        // 停掉游戏再进来：读回写出的那一份。
        var saved = setup.Write(world.State);
        var back = setup.Load(saved);
        Assert.Equal(world.State.Date, back.Date);
        Assert.Equal(PlotSnapshot.Of(world.State), PlotSnapshot.Of(back));
        Assert.Equal(world.State.Bag.Carry, back.Bag.Carry);
        Assert.Equal(world.State.Bag.Slots, back.Bag.Slots);
    }

    [Fact]
    public void 失败路径_背包满时收获被拒_那一格照旧待收_背包一件不变()
    {
        var setup = new BaseSetup(rolls: [false]);
        var world = setup.NewGame();
        var crop = setup.Crops.Values.OrderBy(c => c.Id, StringComparer.Ordinal).First();
        Assert.True(PlotAt(world.State, First).Plant(crop));
        for (var night = 0; night < crop.DaysToFirstRipe; night++)
        {
            Assert.True(setup.Sleep(world).Advanced);
        }

        // 随身栏那一排与网格的每一格都是一满堆种子：没有可合的同标识堆，也没有空格。
        var bag = world.State.Bag;
        var seeds = setup.Items.Items.Values.Where(i => !i.HasQuality).OrderBy(i => i.Id, StringComparer.Ordinal).ToList();
        for (var slot = 0; slot < bag.CarryCount + bag.SlotCount; slot++)
        {
            var kind = seeds[slot % seeds.Count];
            Assert.True(bag.TryAdd(kind.Id, null, kind.StackLimit));
        }

        Assert.Equal(0, bag.EmptySlotCount);
        var before = bag.Carry.Concat(bag.Slots).ToList();
        var plot = PlotAt(world.State, First);

        var attempt = Harvesting.ByProtagonist(plot, crop, Yield, bag);

        Assert.Equal(HarvestStatus.BagFull, attempt.Status);
        Assert.Equal(PlotState.Harvestable, plot.State);
        Assert.Equal(before, bag.Carry.Concat(bag.Slots));
    }

    [Fact]
    public void 调试键给大东西_开新档的背包放到放不下为止_再给被拒的原因是放不下这么大的_那一下背包一件不变()
    {
        var setup = new BaseSetup(rolls: []);
        var bag = setup.NewGame().State.Bag;
        var kinds = BigItemSamples.Kinds(setup.Items);
        Assert.NotEmpty(kinds);

        // 照调试键那样一件一件给：放得下就往后轮一样，放不下就停在那一件上。
        var next = 0;
        var stored = 0;
        while (true)
        {
            var (itemId, qualityId, count) = BigItemSamples.Sample(setup.Items, next);
            if (!bag.TryAdd(itemId, qualityId, count))
            {
                break;
            }

            stored++;
            next = BigItemSamples.Next(next, kinds.Count);
            Assert.True(stored <= bag.SlotCount, "一件大东西至少占两格，放进去的件数不会比网格的格数还多");
        }

        Assert.True(stored > 0, "开新档时背包是空的，至少放得下一件");
        Assert.All(bag.Carry, slot => Assert.Null(slot));
        var before = bag.Carry.Concat(bag.Slots).ToList();
        var (refusedId, refusedQuality, refusedCount) = BigItemSamples.Sample(setup.Items, next);

        // 随身栏那一排还空着，所以不是满了：有空格，却凑不出一块放得下它的地方。
        Assert.Equal(BagRefusal.NoRoomForSize, bag.RefusalFor(refusedId, refusedQuality, refusedCount));
        Assert.Equal(before, bag.Carry.Concat(bag.Slots));
    }

    [Fact]
    public void 失败路径_同一版的存档缺背包那一片被拒()
    {
        var setup = new BaseSetup(rolls: []);
        var root = JsonNode.Parse(setup.Write(setup.NewGame().State))!.AsObject();
        Assert.True(root[SaveFile.ShardsKey]!.AsObject().Remove(WorldShards.BagId));

        var e = Assert.Throws<SaveRejectedException>(() => setup.Load(root.ToJsonString()));

        Assert.Equal(SaveRejection.MissingField, e.Reason);
        Assert.Contains(WorldShards.BagId, e.Message);
    }

    [Fact]
    public void 失败路径_存档里有认不出的物品标识被拒并点名它()
    {
        var setup = new BaseSetup(rolls: []);
        var world = setup.NewGame();
        Assert.True(world.State.Bag.TryAddAll(SeedBatches.Batch(setup.Crops.Values, 0, CarrySlotBar.SlotCount, setup.Items)));
        var root = JsonNode.Parse(setup.Write(world.State))!.AsObject();

        // 背包那一片里随身栏物品标识那一串的第一格（那一批的第一堆落在那里），换成一个不在册的标识。
        var itemIds = root[SaveFile.ShardsKey]![WorldShards.BagId]![WorldShards.BagCarryKeys[0]]!.AsArray();
        itemIds[0] = "item_never_heard_of";

        var e = Assert.Throws<InvalidDataException>(() => setup.Load(root.ToJsonString()));

        Assert.Contains("item_never_heard_of", e.Message);
    }

    [Fact]
    public void 失败路径_选中收获物去播被拒_背包与那一格都不动()
    {
        var setup = new BaseSetup(rolls: []);
        var world = setup.NewGame();
        var crop = setup.Crops.Values.OrderBy(c => c.Id, StringComparer.Ordinal).First();
        Assert.True(world.State.Bag.TryAdd(crop.YieldItemId, Harvesting.UnmutatedQualityId, 3));
        Assert.Equal(crop.YieldItemId, world.State.Bag.Carry[0]?.ItemId);
        var before = world.State.Bag.Carry.ToList();

        Assert.Equal(PlantResult.NotASeed, setup.PlantFromFirstSlot(world, First));

        Assert.Equal(before, world.State.Bag.Carry);
        Assert.Equal(PlotState.Tilled, PlotAt(world.State, First).State);
    }

    private static Plot PlotAt(WorldState world, GridCell cell) => BaseSetup.PlotAt(world, cell);
}
