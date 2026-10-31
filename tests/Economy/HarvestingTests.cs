using Tinderhearth.Rules.Economy;
using Tinderhearth.Rules.Progression;
using Xunit;

namespace Tinderhearth.Rules.Tests.Economy;

/// <summary>主角在地里收一株：先算、经生产那条管道进背包、放进去了才动地块；放不下那一株还在。</summary>
/// <remarks>
/// 这几条要有测试，因为失效方式都不报错：先改地块再放背包，放不下时那一株就没了；变异那一株记成寻常，收上来的
/// 价钱就不对；放不下时地块被动过一点（浇水记录、阶段），再收时收上来的就和本该收的不一样。
/// 产量与堆上限都是测试用值，改它们不该让任何一条失败。
/// </remarks>
public class HarvestingTests
{
    private const int FallowDays = 7;

    /// <summary>浇满收 10 件、一次没浇收一半。</summary>
    private static readonly HarvestYieldRule Yield = new(BaseCount: 10, MinWaterFactor: 0.5, MaxWaterFactor: 1.0);

    private static readonly CropDefinition Turnip = new()
    {
        Id = "turnip",
        StageDays = [1, 1],
        RegrowFromStage = null,
        Seasons = [Season.Spring],
        SeedItemId = TestBags.TurnipSeed,
        YieldItemId = TestBags.Turnip,
    };

    /// <summary>一格长到待收的地，每天都浇；掷变异那一下中不中由调用方定。</summary>
    private static Plot Ripe(bool mutate)
    {
        var plot = new Plot(PlotState.Tilled);
        Assert.True(plot.Plant(Turnip));
        for (var day = 0; day < Turnip.DaysToFirstRipe; day++)
        {
            Assert.True(plot.Water());
            plot.AdvanceDay(Turnip, FallowDays, () => mutate);
        }

        Assert.Equal(PlotState.Harvestable, plot.State);
        return plot;
    }

    /// <summary>一个装满了的背包：唯一那一格是一满堆种子，芜菁合不进去、也没有空格。</summary>
    private static Bag FullBag() => Bag.Restore([new ItemStack(TestBags.TurnipSeed, null, 20, 1)], TestBags.Items);

    [Fact]
    public void 收上来的那一份走完管道_产出是这一株_归主角_不付酬_进主角的背包()
    {
        var bag = TestBags.Empty();

        var attempt = Harvesting.ByProtagonist(Ripe(mutate: false), Turnip, Yield, bag);

        Assert.Equal(HarvestStatus.Stored, attempt.Status);
        Assert.True(attempt.Delivery.HasValue);
        var delivery = attempt.Delivery.Value;
        Assert.Equal(new ProducedGoods(TestBags.Turnip, TestBags.Common, 10), delivery.Goods);
        Assert.Equal(GoodsOwner.Protagonist, delivery.Owner);
        Assert.False(delivery.Paid);
        Assert.True(delivery.Stored);
        Assert.Equal(new ItemStack(TestBags.Turnip, TestBags.Common, 10, 1), bag.Slots[0]);
    }

    [Fact]
    public void 收完那一格照一次性作物回到已锄()
    {
        var plot = Ripe(mutate: false);

        Harvesting.ByProtagonist(plot, Turnip, Yield, TestBags.Empty());

        Assert.Equal(PlotState.Tilled, plot.State);
        Assert.Null(plot.CropId);
    }

    [Fact]
    public void 变异那一株收上来是变异品质_没变的是寻常()
    {
        var bag = TestBags.Empty();

        var mutated = Harvesting.ByProtagonist(Ripe(mutate: true), Turnip, Yield, bag);
        var plain = Harvesting.ByProtagonist(Ripe(mutate: false), Turnip, Yield, bag);

        Assert.Equal(TestBags.Mutated, mutated.Delivery?.Goods.QualityId);
        Assert.Equal(TestBags.Common, plain.Delivery?.Goods.QualityId);
        Assert.Equal(Harvesting.MutatedQualityId, TestBags.Mutated);
        Assert.Equal(Harvesting.UnmutatedQualityId, TestBags.Common);

        // 两个品质各是一堆，不合在一起。
        Assert.Equal(2, bag.Slots.Count(s => s?.ItemId == TestBags.Turnip));
    }

    [Fact]
    public void 背包放不下时那一格照旧待收_一样不变()
    {
        var plot = Ripe(mutate: true);
        var before = PlotSnapshot.Describe(plot);
        var bag = FullBag();
        var bagBefore = bag.Slots.ToList();

        var attempt = Harvesting.ByProtagonist(plot, Turnip, Yield, bag);

        Assert.Equal(HarvestStatus.BagFull, attempt.Status);
        Assert.Equal(before, PlotSnapshot.Describe(plot));
        Assert.Equal(bagBefore, bag.Slots);
        Assert.False(attempt.Delivery?.Stored);
    }

    /// <summary>放不下的那一下什么都没动，所以丢掉一堆腾出位置再收，收到的正是第一次本该收到的那一份。</summary>
    [Fact]
    public void 腾出位置再收_收到的与第一次本该收到的一样()
    {
        var plot = Ripe(mutate: true);
        var bag = FullBag();

        var rejected = Harvesting.ByProtagonist(plot, Turnip, Yield, bag);
        Assert.Equal(BagDiscardResult.Discarded, bag.TryDiscardAt(0));
        var stored = Harvesting.ByProtagonist(plot, Turnip, Yield, bag);

        Assert.Equal(HarvestStatus.Stored, stored.Status);
        Assert.Equal(rejected.Delivery?.Goods, stored.Delivery?.Goods);
        Assert.Equal(TestBags.Turnip, bag.Slots[0]?.ItemId);
    }

    [Fact]
    public void 不是待收的那一格什么都不发生()
    {
        var plot = new Plot(PlotState.Tilled);
        Assert.True(plot.Plant(Turnip));
        var bag = TestBags.Empty();

        var attempt = Harvesting.ByProtagonist(plot, Turnip, Yield, bag);

        Assert.Equal(HarvestStatus.NotHarvestable, attempt.Status);
        Assert.Null(attempt.Delivery);
        Assert.Equal(PlotState.Planted, plot.State);
        Assert.Equal(bag.SlotCount, bag.EmptySlotCount);
    }

    /// <summary>先算的那一半不改这一格，算出来的就是真收的时候那一份。</summary>
    [Fact]
    public void 先算不改这一格_算出来的与真收的一样()
    {
        var plot = Ripe(mutate: true);
        var before = PlotSnapshot.Describe(plot);

        Assert.True(plot.PeekHarvest(Turnip, Yield, out var peeked));
        Assert.Equal(before, PlotSnapshot.Describe(plot));

        Assert.True(plot.TryHarvest(Turnip, Yield, out var harvested));
        Assert.Equal(peeked, harvested);
    }

    [Fact]
    public void 收获要用的那两档品质不在册就当场抛()
    {
        var withoutMutated = ItemCatalog.Build(
            TestBags.Items.Items.Values,
            TestBags.Items.Categories.Values,
            TestBags.Items.Rarities,
            TestBags.Items.Qualities.Where(q => q.Id != Harvesting.MutatedQualityId),
            name => name.Split(' ')[0]);

        var e = Assert.Throws<InvalidDataException>(() => Harvesting.RequireQualitiesIn(withoutMutated));

        Assert.Contains(Harvesting.MutatedQualityId, e.Message);
        Harvesting.RequireQualitiesIn(TestBags.Items);
    }

    /// <summary>工程里那一份品质档位里有收获要用的那两档。</summary>
    [Fact]
    public void 工程里的品质档位有收获要用的那两档()
    {
        Harvesting.RequireQualitiesIn(Progression.ShippedItems.Load());
    }
}
