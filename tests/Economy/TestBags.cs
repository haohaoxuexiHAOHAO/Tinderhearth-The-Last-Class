using Tinderhearth.Rules.Economy;
using Tinderhearth.Rules.Progression;

namespace Tinderhearth.Rules.Tests.Economy;

/// <summary>测试用的背包：一份很小的物品表，加一个空背包。只关心地块与日期的那些测试拿它填世界里背包那一格。</summary>
/// <remarks>芜菁有品质、一堆 20；芜菁种子无品质、一堆 20。堆上限是随手挑的，不是数值。</remarks>
internal static class TestBags
{
    public const string Turnip = "item_turnip";
    public const string TurnipSeed = "item_turnip_seed";
    public const string Common = "common";
    public const string Mutated = "mutated";

    public static readonly ItemCatalog Items = ItemCatalog.Build(
        [Item(Turnip, hasQuality: true), Item(TurnipSeed, hasQuality: false)],
        [new ItemCategory { Id = "herb", NameKey = "item_category.herb.name", ParentId = null }],
        [new ItemTier { Id = "low", NameKey = "item_rarity.low.name", SortOrder = 1, BasePriceFactor = null, PositiveAffixCount = 0, DisplayColor = "copper 3" }],
        [
            new ItemTier { Id = Common, NameKey = "item_quality.common.name", SortOrder = 2, BasePriceFactor = null, DisplayColor = "ash 6" },
            new ItemTier { Id = Mutated, NameKey = "item_quality.mutated.name", SortOrder = 5, BasePriceFactor = null, DisplayColor = "spirit 3" },
        ],
        name => name.Split(' ')[0]);

    /// <summary>一个空背包。</summary>
    public static Bag Empty(int slots = 4) => new(slots, Items);

    private static ItemDefinition Item(string id, bool hasQuality) => new()
    {
        Id = id,
        NameKey = $"item.{id}.name",
        DescriptionKey = $"item.{id}.description",
        CategoryId = "herb",
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
