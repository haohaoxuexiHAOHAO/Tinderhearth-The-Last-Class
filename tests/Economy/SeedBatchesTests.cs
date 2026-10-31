using Tinderhearth.Rules.Economy;
using Tinderhearth.Rules.Progression;
using Xunit;

namespace Tinderhearth.Rules.Tests.Economy;

/// <summary>开发期调试键往背包里放的种子批：按标识排、每样一满堆，最后一批之后回到第一批，整批放不下一件不放。</summary>
/// <remarks>一批几种与堆上限都是测试用值，这里不写死随身栏的格数：改了格数，这几条照样成立。</remarks>
public class SeedBatchesTests
{
    private const int PerBatch = 3;
    private const int StackLimit = 20;

    /// <summary>几种种子，补零的标识按序号排就是按字符排；每种作物收上来都是芜菁，收获那一侧这里不关心。</summary>
    private static (List<CropDefinition> Crops, ItemCatalog Items) Seeds(int kinds)
    {
        var seedItems = Enumerable.Range(1, kinds).Select(n => Item($"seed_{n:00}")).ToList();
        var items = ItemCatalog.Build(
            [.. TestBags.Items.Items.Values, .. seedItems],
            TestBags.Items.Categories.Values,
            TestBags.Items.Rarities,
            TestBags.Items.Qualities,
            name => name.Split(' ')[0]);
        var crops = seedItems.Select((seed, i) => new CropDefinition
        {
            Id = $"crop_{i + 1:00}",
            StageDays = [1, 1],
            RegrowFromStage = null,
            Seasons = [Season.Spring],
            SeedItemId = seed.Id,
            YieldItemId = TestBags.Turnip,
        }).ToList();
        return (crops, items);
    }

    private static ItemDefinition Item(string id) => new()
    {
        Id = id,
        NameKey = $"item.{id}.name",
        DescriptionKey = $"item.{id}.description",
        CategoryId = "herb",
        RarityId = "low",
        HasQuality = false,
        StackLimit = StackLimit,
        BasePriceCopper = null,
        Discardable = true,
        Sellable = true,
        UseEffectId = null,
        ConfirmBeforeUse = false,
    };

    [Fact]
    public void 按标识排序_一批几种_每样一满堆且不带品质()
    {
        var (crops, items) = Seeds((PerBatch * 2) + 1);
        crops.Reverse();

        var first = SeedBatches.Batch(crops, 0, PerBatch, items);

        Assert.Equal(new[] { "seed_01", "seed_02", "seed_03" }, first.Select(p => p.ItemId));
        Assert.All(first, p =>
        {
            Assert.Null(p.QualityId);
            Assert.Equal(StackLimit, p.Count);
        });
    }

    [Fact]
    public void 最后一批不足一批时只有剩下那几样()
    {
        var (crops, items) = Seeds((PerBatch * 2) + 1);

        var last = SeedBatches.Batch(crops, 2, PerBatch, items);

        Assert.Equal(new[] { "seed_07" }, last.Select(p => p.ItemId));
    }

    /// <summary>挨批往后走，最后一批之后回到第一批；每一样都恰好落在某一批里一次。</summary>
    [Fact]
    public void 一圈之后回到第一批且每样都出现一次()
    {
        var (crops, items) = Seeds((PerBatch * 2) + 1);
        var seen = new List<string>();
        var batch = 0;

        for (var press = 0; press < SeedBatches.CountFor(crops.Count, PerBatch); press++)
        {
            seen.AddRange(SeedBatches.Batch(crops, batch, PerBatch, items).Select(p => p.ItemId));
            batch = SeedBatches.Next(batch, crops.Count, PerBatch);
        }

        Assert.Equal(0, batch);
        Assert.Equal(crops.Select(c => c.SeedItemId), seen);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    public void 批号越界当场报错(int batch)
    {
        var (crops, items) = Seeds((PerBatch * 2) + 1);

        Assert.Throws<ArgumentOutOfRangeException>(() => SeedBatches.Batch(crops, batch, PerBatch, items));
    }

    /// <summary>整批放不下就一件不放：背包只剩两格空着，三样种子那一批整批被拒，背包一件不变。</summary>
    [Fact]
    public void 整批放不下时一件不放()
    {
        var (crops, items) = Seeds(PerBatch);
        var bag = Bag.Restore([new ItemStack(TestBags.Turnip, TestBags.Common, 5, 1), null, null], items);
        var before = bag.Slots.ToList();

        Assert.False(bag.TryAddAll(SeedBatches.Batch(crops, 0, PerBatch, items)));

        Assert.Equal(before, bag.Slots);
    }

    [Fact]
    public void 放得下时整批进背包_一样一格()
    {
        var (crops, items) = Seeds(PerBatch);
        var bag = new Bag(PerBatch + 1, items);

        Assert.True(bag.TryAddAll(SeedBatches.Batch(crops, 0, PerBatch, items)));

        Assert.Equal(new string?[] { "seed_01", "seed_02", "seed_03" }, bag.Slots.Take(PerBatch).Select(s => s?.ItemId));
        Assert.All(bag.Slots.Take(PerBatch), s => Assert.Equal(StackLimit, s?.Count));
        Assert.Null(bag.Slots[PerBatch]);
    }
}
