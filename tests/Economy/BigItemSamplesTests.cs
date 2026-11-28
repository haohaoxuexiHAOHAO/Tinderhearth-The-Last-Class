using Tinderhearth.Rules.Economy;
using Tinderhearth.Rules.Progression;
using Xunit;

namespace Tinderhearth.Rules.Tests.Economy;

/// <summary>开发期调试键给的大东西：占不止一格的那几样按标识轮着给，一次一件，有品质的给寻常那一档。</summary>
/// <remarks>物品用一份测试自己的小表：盾占 2×2 有品质，旗占 1×3 无品质，种子与芜菁各占 1 格。</remarks>
public class BigItemSamplesTests
{
    private const string Shield = "shield";
    private const string Banner = "banner";

    private static readonly ItemCatalog Items = ItemCatalog.Build(
        [
            Item(Shield, hasQuality: true, width: 2, height: 2),
            Item("turnip", hasQuality: true),
            Item(Banner, hasQuality: false, width: 1, height: 3),
            Item("turnip_seed", hasQuality: false),
        ],
        [new ItemCategory { Id = "herb", NameKey = "item_category.herb.name", ParentId = null }],
        [new ItemTier { Id = "low", NameKey = "item_rarity.low.name", SortOrder = 1, BasePriceFactor = null, DisplayColor = "copper 3" }],
        [new ItemTier { Id = Harvesting.UnmutatedQualityId, NameKey = "item_quality.common.name", SortOrder = 2, BasePriceFactor = null, DisplayColor = "ash 6" }],
        name => name.Split(' ')[0]);

    [Fact]
    public void 只算占不止一格的那几样_按标识排()
    {
        Assert.Equal([Banner, Shield], BigItemSamples.Kinds(Items));
    }

    [Fact]
    public void 一次一件_有品质的给寻常那一档_无品质的不带品质()
    {
        Assert.Equal((Banner, (string?)null, 1), BigItemSamples.Sample(Items, 0));
        Assert.Equal((Shield, Harvesting.UnmutatedQualityId, 1), BigItemSamples.Sample(Items, 1));
    }

    [Fact]
    public void 最后一样之后回到第一样_越界当场报错()
    {
        Assert.Equal(1, BigItemSamples.Next(0, 2));
        Assert.Equal(0, BigItemSamples.Next(1, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => BigItemSamples.Next(2, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => BigItemSamples.Sample(Items, 2));
    }

    [Fact]
    public void 一样大东西都没有时是空的()
    {
        var small = ItemCatalog.Build(
            [Item("turnip_seed", hasQuality: false)],
            [new ItemCategory { Id = "herb", NameKey = "item_category.herb.name", ParentId = null }],
            [new ItemTier { Id = "low", NameKey = "item_rarity.low.name", SortOrder = 1, BasePriceFactor = null, DisplayColor = "copper 3" }],
            [new ItemTier { Id = Harvesting.UnmutatedQualityId, NameKey = "item_quality.common.name", SortOrder = 2, BasePriceFactor = null, DisplayColor = "ash 6" }],
            name => name.Split(' ')[0]);

        Assert.Empty(BigItemSamples.Kinds(small));
    }

    private static ItemDefinition Item(string id, bool hasQuality, int width = 1, int height = 1) => new()
    {
        Id = id,
        NameKey = $"item.{id}.name",
        DescriptionKey = $"item.{id}.description",
        CategoryId = "herb",
        Footprint = new() { Width = width, Height = height },
        RarityId = "low",
        HasQuality = hasQuality,
        StackLimit = 20,
        BasePriceCopper = null,
        Discardable = true,
        Sellable = true,
        UseEffectId = null,
        ConfirmBeforeUse = false,
    };
}
