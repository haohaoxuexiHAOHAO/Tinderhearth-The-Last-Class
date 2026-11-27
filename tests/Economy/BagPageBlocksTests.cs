using Tinderhearth.Rules.Economy;
using Tinderhearth.Rules.Progression;
using Xunit;

namespace Tinderhearth.Rules.Tests.Economy;

/// <summary>
/// 背包页照网格画：每一堆占的那一块在页上哪、多大；点到一块里的哪一格都是那一堆；拿着一堆指着别处时，要占的那一块画在哪、
/// 放不放得下，而算这些不改背包。
/// </summary>
/// <remarks>
/// 页上先排随身栏那一排、再排网格（设计仓 design/界面系统.md 的「游戏菜单上：背包」一节）。物品用一份测试自己的小表：
/// 盾占 2×2，石头、种子、芜菁各占 1 格，堆上限是随手挑的、不是数值。
/// </remarks>
public class BagPageBlocksTests
{
    private const string Turnip = "turnip";
    private const string Seed = "turnip_seed";
    private const string Stone = "stone";
    private const string Shield = "shield";
    private const string Common = "common";

    private static readonly ItemCatalog Items = ItemCatalog.Build(
        [
            Item(Turnip, hasQuality: true, stackLimit: 20),
            Item(Seed, hasQuality: false, stackLimit: 20),
            Item(Stone, hasQuality: false, stackLimit: 5),
            Item(Shield, hasQuality: false, stackLimit: 1, width: 2, height: 2),
        ],
        [new ItemCategory { Id = "herb", NameKey = "item_category.herb.name", ParentId = null }],
        [new ItemTier { Id = "low", NameKey = "item_rarity.low.name", SortOrder = 1, BasePriceFactor = null, DisplayColor = "copper 3" }],
        [new ItemTier { Id = Common, NameKey = "item_quality.common.name", SortOrder = 2, BasePriceFactor = null, DisplayColor = "ash 6" }],
        name => name.Split(' ')[0]);

    /// <summary>
    /// 随身栏 4 格（第 0 格一堆种子），下面 4×3 的网格：盾在 (0, 0)、石头在 (3, 0)。页上第 0–3 格是随身栏，网格第 n 格是页上第 4 + n 格。
    /// </summary>
    private static Bag Sample() => Bag.Restore(
        [Stack(Seed, null, 3, 1), null, null, null],
        4, 3,
        [Stack(Shield, null, 1, 2), null, null, Stack(Stone, null, 2, 3), null, null, null, null, null, null, null, null],
        Items);

    [Fact]
    public void 每一堆占的那一块_随身栏那一排在上面_网格从下一行开始()
    {
        var blocks = BagPage.BlocksOf(Sample());

        Assert.Equal(3, blocks.Count);
        Assert.Equal((0, 0, 0, 1, 1, Seed), Shape(blocks[0]));
        Assert.Equal((4, 0, 1, 2, 2, Shield), Shape(blocks[1]));
        Assert.Equal((7, 3, 1, 1, 1, Stone), Shape(blocks[2]));
    }

    [Fact]
    public void 随身栏那几格不够一行也占一整行()
    {
        var bag = Bag.Restore([null, null, null], 4, 2, new ItemStack?[8], Items);

        Assert.Equal((2, 0), BagPage.PositionOf(bag, 2));
        Assert.Equal((0, 1), BagPage.PositionOf(bag, 3));
        Assert.Equal((1, 2), BagPage.PositionOf(bag, 3 + 5));
    }

    [Fact]
    public void 点到一块里的哪一格都是那一堆()
    {
        var bag = Sample();

        Assert.All([4, 5, 8, 9], index => Assert.Equal(4, BagPage.AnchorIndexOf(bag, index)));
        Assert.Equal(0, BagPage.AnchorIndexOf(bag, 0));
        Assert.Null(BagPage.AnchorIndexOf(bag, 6));
        Assert.Null(BagPage.AnchorIndexOf(bag, 1));
    }

    [Fact]
    public void 拿起时点的那一格对准指着的那一格_放得下就画在要占的那一块上()
    {
        // 从盾的右下那一格（网格第 5 格）拿起、指着网格第 10 格：左上角落在网格 (1, 1)，页上是第 1 列、第 2 行。
        Assert.Equal(new BagDropPreview(1, 2, 2, 2, Fits: true), BagPage.DropPreviewOf(Sample(), 4 + 5, 4 + 10));
    }

    [Fact]
    public void 压到别的东西就画放不下_出了网格的那一截不画()
    {
        var bag = Sample();

        // 盾从左上角拿起、指着网格第 2 格：右上那一格压着石头。
        Assert.Equal(new BagDropPreview(2, 1, 2, 2, Fits: false), BagPage.DropPreviewOf(bag, 4, 4 + 2));
        // 指着网格第 7 格：往右出了网格一格，只画网格里那一列。
        Assert.Equal(new BagDropPreview(3, 2, 1, 2, Fits: false), BagPage.DropPreviewOf(bag, 4, 4 + 7));
    }

    [Fact]
    public void 指着同标识的另一堆是合进去_画在那一堆上()
    {
        var bag = Bag.Restore(
            [], 4, 3,
            [null, null, null, Stack(Stone, null, 2, 1), null, null, null, null, null, null, null, Stack(Stone, null, 1, 2)],
            Items);

        Assert.Equal(new BagDropPreview(3, 0, 1, 1, Fits: true), BagPage.DropPreviewOf(bag, 11, 3));
    }

    [Fact]
    public void 大东西指着随身栏那一排画放不下_指着拿起的那一格什么都不画()
    {
        var bag = Sample();

        Assert.Equal(new BagDropPreview(1, 0, 1, 1, Fits: false), BagPage.DropPreviewOf(bag, 4, 1));
        Assert.Null(BagPage.DropPreviewOf(bag, 5, 5));
    }

    [Fact]
    public void 算预览不改背包()
    {
        var bag = Sample();
        var carry = bag.Carry.ToList();
        var slots = bag.Slots.ToList();

        Assert.Equal(BagMoveResult.Moved, bag.PreviewMove(BagPlace.Grid(0), BagPlace.Grid(6)));
        _ = BagPage.DropPreviewOf(bag, 4, 4 + 6);

        Assert.Equal(carry, bag.Carry);
        Assert.Equal(slots, bag.Slots);
    }

    [Fact]
    public void 指着被筛掉的那一格什么都不画()
    {
        var bag = Bag.Restore(
            [], 4, 1, [null, null, Stack(Turnip, Common, 2, 1), Stack(Stone, null, 2, 2)], Items);
        var controls = new BagControls();
        controls.NextQuality(Items);   // 只看寻常：石头无品质，被筛掉

        Assert.False(controls.Shows(bag, 3));
        Assert.Null(controls.Preview(bag, 2, 3));
        Assert.Equal(new BagDropPreview(0, 0, 1, 1, Fits: true), controls.Preview(bag, 2, 0));
    }

    private static (int Index, int Column, int Row, int Width, int Height, string Item) Shape(BagBlock block) =>
        (block.Index, block.Column, block.Row, block.Width, block.Height, block.Content.ItemId);

    private static ItemStack Stack(string item, string? quality, int count, int sequence) => new(item, quality, count, sequence);

    private static ItemDefinition Item(string id, bool hasQuality, int stackLimit, int width = 1, int height = 1) => new()
    {
        Id = id,
        NameKey = $"item.{id}.name",
        DescriptionKey = $"item.{id}.description",
        CategoryId = "herb",
        Footprint = new() { Width = width, Height = height },
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
