using Tinderhearth.Rules.Economy;
using Tinderhearth.Rules.Foundation.Config;
using Tinderhearth.Rules.Progression;
using Xunit;
using Xunit.Sdk;

namespace Tinderhearth.Rules.Tests.Economy;

/// <summary>
/// 主角的背包：同种同品质合堆、堆满再占下一格、同种不同品质分格、无品质只按种类分，满载的两个条件，放不下整份拒收，
/// 格数从配置读、堆上限从物品定义读。
/// </summary>
/// <remarks>
/// 规则在设计仓 design/背包系统.md 的「一格一堆，堆的标识是「种类 ＋ 品质」」一节。物品用一份测试自己的小表，
/// 堆上限是随手挑的、不是数值。
/// </remarks>
public class BagTests
{
    private const string Turnip = "turnip";
    private const string Seed = "turnip_seed";
    private const string Stone = "stone";
    private const string Common = "common";
    private const string Mutated = "mutated";

    /// <summary>芜菁有品质、一堆 20；种子无品质、一堆 20；石头无品质、一堆 5 —— 两种堆上限，好看出它按种类读。</summary>
    private static readonly ItemCatalog Items = ItemCatalog.Build(
        [Item(Turnip, hasQuality: true, stackLimit: 20), Item(Seed, hasQuality: false, stackLimit: 20), Item(Stone, hasQuality: false, stackLimit: 5)],
        [new ItemCategory { Id = "herb", NameKey = "item_category.herb.name", ParentId = null }],
        [new ItemTier { Id = "low", NameKey = "item_rarity.low.name", SortOrder = 1, BasePriceFactor = null, PositiveAffixCount = 0, DisplayColor = "copper 3" }],
        [
            new ItemTier { Id = Common, NameKey = "item_quality.common.name", SortOrder = 2, BasePriceFactor = null, DisplayColor = "ash 6" },
            new ItemTier { Id = Mutated, NameKey = "item_quality.mutated.name", SortOrder = 5, BasePriceFactor = null, DisplayColor = "spirit 3" },
        ],
        name => name.Split(' ')[0]);

    [Fact]
    public void 同种同品质合进一堆()
    {
        var bag = new Bag(4, Items);

        Assert.True(bag.TryAdd(Turnip, Common, 3));
        Assert.True(bag.TryAdd(Turnip, Common, 4));

        var stack = Assert.Single(Stacks(bag));
        Assert.Equal(7, stack.Count);
        Assert.Equal(3, bag.EmptySlotCount);
    }

    [Fact]
    public void 堆到那种物品的上限再占下一格()
    {
        var bag = new Bag(4, Items);

        Assert.True(bag.TryAdd(Turnip, Common, 25));

        Assert.Equal([20, 5], Stacks(bag).Select(s => s.Count));
        Assert.Equal(Turnip, bag.Slots[0]?.ItemId);
        Assert.Equal(Turnip, bag.Slots[1]?.ItemId);
    }

    /// <summary>同种不同品质各占一格。后半是反证：断言「它们合成了一堆」必须失败，否则这条判不出分堆。</summary>
    [Fact]
    public void 同种不同品质各占一格()
    {
        var bag = new Bag(4, Items);

        Assert.True(bag.TryAdd(Turnip, Common, 1));
        Assert.True(bag.TryAdd(Turnip, Mutated, 1));

        Assert.Equal([Common, Mutated], Stacks(bag).Select(s => s.QualityId));
        Assert.Throws<SingleException>(() => Assert.Single(Stacks(bag)));
    }

    [Fact]
    public void 无品质的只按种类分堆()
    {
        var bag = new Bag(4, Items);

        Assert.True(bag.TryAdd(Seed, null, 3));
        Assert.True(bag.TryAdd(Seed, null, 2));

        var stack = Assert.Single(Stacks(bag));
        Assert.Equal(5, stack.Count);
        Assert.Null(stack.QualityId);
    }

    /// <summary>格子用尽，但有一堆同标识的没满：照样放得进去，合进那一堆。</summary>
    [Fact]
    public void 格子用尽但有半满的同标识堆时照样放得进去()
    {
        var bag = new Bag(2, Items);
        Assert.True(bag.TryAdd(Turnip, Common, 10));
        Assert.True(bag.TryAdd(Stone, null, 5));
        Assert.Equal(0, bag.EmptySlotCount);

        Assert.False(bag.IsFullFor(Turnip, Common));
        Assert.True(bag.TryAdd(Turnip, Common, 10));

        Assert.Equal([20, 5], Stacks(bag).Select(s => s.Count));
    }

    /// <summary>满载是两个条件都不成立：没有可合的同标识堆，也没有空格。</summary>
    [Fact]
    public void 没有可合的同标识堆也没有空格就是满载()
    {
        var bag = new Bag(2, Items);
        Assert.True(bag.TryAdd(Turnip, Common, 20));
        Assert.True(bag.TryAdd(Stone, null, 5));

        Assert.True(bag.IsFullFor(Turnip, Common));
        Assert.True(bag.IsFullFor(Turnip, Mutated));
        Assert.False(bag.TryAdd(Turnip, Common, 1));
    }

    /// <summary>一次放进来的那一份放不下，整份被拒：一件都不放，每一格都与之前一样。</summary>
    [Fact]
    public void 放不下的那一份整份被拒而背包一件不变()
    {
        var bag = new Bag(2, Items);
        Assert.True(bag.TryAdd(Turnip, Common, 15));
        var before = bag.Slots.ToList();

        Assert.False(bag.CanAdd(Turnip, Common, 26));
        Assert.False(bag.TryAdd(Turnip, Common, 26));

        Assert.Equal(before, bag.Slots);
    }

    /// <summary>堆上限按种类读物品定义：同一份背包里，石头五件一堆、芜菁二十件一堆。</summary>
    [Fact]
    public void 堆上限按种类从物品定义读()
    {
        var bag = new Bag(4, Items);

        Assert.True(bag.TryAdd(Stone, null, 7));
        Assert.True(bag.TryAdd(Turnip, Common, 7));

        Assert.Equal([5, 2, 7], Stacks(bag).Select(s => s.Count));
    }

    /// <summary>开新档时的格数按背包等级从配置读，等级是第一级；配置里没有的那一级当场抛。</summary>
    [Fact]
    public void 格数按背包等级从配置读()
    {
        var config = Config("[12, 30]");

        Assert.Equal(12, Bag.NewGame(config, Items).SlotCount);
        Assert.Equal(30, config.BackpackSlotsAt(2));
        Assert.Throws<ArgumentOutOfRangeException>(() => config.BackpackSlotsAt(3));
        Assert.Empty(Stacks(Bag.NewGame(config, Items)));
    }

    /// <summary>配置里没写背包格数、写了空表或写了不为正的一级，读配置时当场报错。</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("[]")]
    [InlineData("[16, 0]")]
    public void 背包格数没配好就当场报错(string? slots)
    {
        var json = slots is null
            ? """{ "rosterCapacity": 9, "buildableWidthCells": 80, "buildableHeightCells": 60 }"""
            : $$"""{ "rosterCapacity": 9, "buildableWidthCells": 80, "buildableHeightCells": 60, "backpackSlotsByLevel": {{slots}} }""";

        var error = Assert.ThrowsAny<ArgumentException>(() => GameConfig.Parse(json));

        Assert.Contains("BackpackSlotsByLevel", error.Message);
    }

    /// <summary>物品不在册、品质那一位与物品的声明对不上、件数不为正，是调用方的错：当场抛，不当成放不下。</summary>
    [Fact]
    public void 调用方给错的那一份当场抛()
    {
        var bag = new Bag(4, Items);

        Assert.Throws<ArgumentException>(() => bag.TryAdd("ghost", null, 1));
        Assert.Throws<ArgumentException>(() => bag.TryAdd(Turnip, null, 1));
        Assert.Throws<ArgumentException>(() => bag.TryAdd(Seed, Common, 1));
        Assert.Throws<ArgumentException>(() => bag.TryAdd(Turnip, "legendary", 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => bag.TryAdd(Turnip, Common, 0));
        Assert.Empty(Stacks(bag));
    }

    /// <summary>每一堆记着自己最近一次进东西的先后序号：后进的更大，合进旧的那一堆也算一次新的。</summary>
    [Fact]
    public void 最近获得跟着每一次进东西走()
    {
        var bag = new Bag(4, Items);
        Assert.True(bag.TryAdd(Turnip, Common, 1));
        Assert.True(bag.TryAdd(Stone, null, 1));
        Assert.True(bag.Slots[1]!.Value.AcquiredSequence > bag.Slots[0]!.Value.AcquiredSequence);

        Assert.True(bag.TryAdd(Turnip, Common, 1));

        Assert.True(bag.Slots[0]!.Value.AcquiredSequence > bag.Slots[1]!.Value.AcquiredSequence);
    }

    private static List<ItemStack> Stacks(Bag bag) => [.. bag.Slots.Where(s => s is not null).Select(s => s!.Value)];

    private static GameConfig Config(string backpackSlots) => GameConfig.Parse(
        $$"""{ "rosterCapacity": 9, "buildableWidthCells": 80, "buildableHeightCells": 60, "backpackSlotsByLevel": {{backpackSlots}} }""");

    private static ItemDefinition Item(string id, bool hasQuality, int stackLimit) => new()
    {
        Id = id,
        NameKey = $"item.{id}.name",
        DescriptionKey = $"item.{id}.description",
        CategoryId = "herb",
        RarityId = "low",
        HasQuality = hasQuality,
        StackLimit = stackLimit,
        BasePriceCopper = null,
        Discardable = true,
        Sellable = true,
        UseEffectId = null,
        ConfirmBeforeUse = false,
    };
}
