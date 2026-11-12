using Tinderhearth.Rules.Economy;
using Tinderhearth.Rules.Progression;
using Tinderhearth.Rules.UI;
using Xunit;

namespace Tinderhearth.Rules.Tests.Economy;

/// <summary>背包里的排序、筛选、一键整理、挪格与丢弃，以及随身栏那一排不被排序与整理换掉。</summary>
/// <remarks>
/// 这几样失效都不报错：排序读了原来的格号，排两次就是两个样子；整理多并了或少并了一件，总数悄悄变了；
/// 排序碰了随身栏，玩家手上那一格换了东西而他没按任何键。物品表是测试自己的：芜菁在药材下面、有品质、一堆 20；
/// 铁在矿石下面、有品质、一堆 10；药材与矿石都在材料下面；种子无品质；信件无品质、一堆 1、不可丢弃。
/// 堆上限是随手挑的，不是数值。
/// </remarks>
public class BagArrangementTests
{
    private const string Turnip = "turnip";
    private const string Iron = "iron";
    private const string Seed = "seed";
    private const string Letter = "letter";
    private const string Common = "common";
    private const string Fine = "fine";
    private const string Mutated = "mutated";

    private static readonly ItemCatalog Items = ItemCatalog.Build(
        [
            Item(Turnip, "herb", hasQuality: true, stack: 20),
            Item(Iron, "ore", hasQuality: true, stack: 10),
            Item(Seed, "seed", hasQuality: false, stack: 20),
            Item(Letter, "quest", hasQuality: false, stack: 1, discardable: false),
        ],
        [
            Category("material", parent: null),
            Category("herb", parent: "material"),
            Category("ore", parent: "material"),
            Category("seed", parent: null),
            Category("quest", parent: null),
        ],
        [new ItemTier { Id = "low", NameKey = "item_rarity.low.name", SortOrder = 1, BasePriceFactor = null, PositiveAffixCount = 0, DisplayColor = "copper 3" }],
        [
            new ItemTier { Id = Common, NameKey = "item_quality.common.name", SortOrder = 2, BasePriceFactor = null, DisplayColor = "ash 6" },
            new ItemTier { Id = Fine, NameKey = "item_quality.fine.name", SortOrder = 3, BasePriceFactor = null, DisplayColor = "ash 7" },
            new ItemTier { Id = Mutated, NameKey = "item_quality.mutated.name", SortOrder = 5, BasePriceFactor = null, DisplayColor = "spirit 3" },
        ],
        name => name.Split(' ')[0]);

    /// <summary>放五样进一个 8 格、没有随身栏那一排的背包，按放进去的先后各占一格：种子最早、信件最近。</summary>
    private static Bag FiveKinds()
    {
        var bag = new Bag(8, 1, Items);
        Assert.True(bag.TryAdd(Seed, null, 5));
        Assert.True(bag.TryAdd(Turnip, Common, 3));
        Assert.True(bag.TryAdd(Iron, Fine, 2));
        Assert.True(bag.TryAdd(Turnip, Mutated, 4));
        Assert.True(bag.TryAdd(Letter, null, 1));
        return bag;
    }

    private static ItemStack Stack(string item, string? quality, int count, int sequence) => new(item, quality, count, sequence);

    /// <summary>读回一份一行、没有随身栏那一排的背包：给的那几格从左往右排。</summary>
    private static Bag Row(IReadOnlyList<ItemStack?> slots) => Bag.Restore(slots.Count, 1, slots, Items);

    /// <summary>读回一份带随身栏那一排的背包，网格是一行。</summary>
    private static Bag WithCarry(IReadOnlyList<ItemStack?> carry, IReadOnlyList<ItemStack?> grid) =>
        Bag.Restore(carry, grid.Count, 1, grid, Items);

    /// <summary>网格每一格写成「种类:品质:件数」，空格写 _，一眼看得出摆法。</summary>
    private static string Layout(Bag bag) => Describe(bag.Slots);

    /// <summary>随身栏那一排，写法同 <see cref="Layout"/>。</summary>
    private static string CarryLayout(Bag bag) => Describe(bag.Carry);

    private static string Describe(IEnumerable<ItemStack?> cells) =>
        string.Join(" ", cells.Select(s => s is { } x ? $"{x.ItemId}:{x.QualityId ?? "-"}:{x.Count}" : "_"));

    private static Dictionary<(string, string?), int> Totals(Bag bag) =>
        bag.Carry.Concat(bag.Slots).OfType<ItemStack>()
            .GroupBy(s => (s.ItemId, s.QualityId)).ToDictionary(g => g.Key, g => g.Sum(s => s.Count));

    [Theory]
    [InlineData(BagSortKey.Quality, "turnip:mutated:4 iron:fine:2 turnip:common:3 letter:-:1 seed:-:5 _ _ _")]
    [InlineData(BagSortKey.Category, "turnip:mutated:4 turnip:common:3 iron:fine:2 letter:-:1 seed:-:5 _ _ _")]
    [InlineData(BagSortKey.Recent, "letter:-:1 turnip:mutated:4 iron:fine:2 turnip:common:3 seed:-:5 _ _ _")]
    public void 三个键各排一次_同键排两次结果相同(BagSortKey key, string expected)
    {
        var bag = FiveKinds();

        bag.Sort(key, BagFilter.All);
        Assert.Equal(expected, Layout(bag));

        bag.Sort(key, BagFilter.All);
        Assert.Equal(expected, Layout(bag));
    }

    [Fact]
    public void 排出来的样子不读原来的格号_打乱了再排还是同一个样子()
    {
        var sorted = FiveKinds();
        sorted.Sort(BagSortKey.Quality, BagFilter.All);

        var shuffled = FiveKinds();
        shuffled.Move(0, 7);
        shuffled.Move(2, 5);
        shuffled.Move(1, 3);
        shuffled.Sort(BagSortKey.Quality, BagFilter.All);

        Assert.Equal(Layout(sorted), Layout(shuffled));
    }

    [Fact]
    public void 一键整理把同标识的散堆并成满堆加至多一个零头_装满的不动_总数一件不变()
    {
        var bag = Row(
        [
            Stack(Turnip, Common, 15, 1), null, Stack(Turnip, Common, 15, 4), Stack(Turnip, Common, 20, 2),
            Stack(Turnip, Mutated, 3, 3), null, Stack(Seed, null, 5, 5), Stack(Seed, null, 7, 6),
        ]);
        var before = Totals(bag);

        bag.Tidy(BagSortKey.Category, BagFilter.All);

        Assert.Equal(before, Totals(bag));
        Assert.Equal(8, bag.SlotCount);
        Assert.Equal("turnip:mutated:3 turnip:common:20 turnip:common:20 turnip:common:10 seed:-:12 _ _ _", Layout(bag));

        // 并出来的那几堆记参与合并的散堆里最近的那个序号；原本装满的那一堆照旧。
        var commons = bag.Slots.OfType<ItemStack>().Where(s => s.Holds(Turnip, Common)).Select(s => s.AcquiredSequence).Order();
        Assert.Equal([2, 4, 4], commons);
        Assert.Equal(6, bag.Slots.OfType<ItemStack>().Single(s => s.ItemId == Seed).AcquiredSequence);
    }

    [Fact]
    public void 整理两次与整理一次一样_没有散堆又排好了时整理什么都不变()
    {
        var bag = Row(
        [
            Stack(Seed, null, 5, 1), Stack(Turnip, Common, 15, 2), null, Stack(Turnip, Common, 8, 3), Stack(Iron, Fine, 4, 4),
        ]);

        bag.Tidy(BagSortKey.Recent, BagFilter.All);
        var once = Layout(bag);
        bag.Tidy(BagSortKey.Recent, BagFilter.All);

        Assert.Equal(once, Layout(bag));

        var tidy = Row(
        [
            Stack(Turnip, Common, 7, 3), Stack(Iron, Fine, 4, 2), Stack(Seed, null, 5, 1), null,
        ]);
        var untouched = Layout(tidy);

        tidy.Tidy(BagSortKey.Category, BagFilter.All);

        Assert.Equal(untouched, Layout(tidy));
    }

    [Fact]
    public void 随身栏那一排排序与整理一格都不碰_网格里的散堆也不并进随身栏那一堆()
    {
        var bag = WithCarry(
            [Stack(Seed, null, 5, 1), Stack(Turnip, Common, 3, 2), null],
            [Stack(Iron, Fine, 2, 3), Stack(Turnip, Common, 4, 4), null, Stack(Seed, null, 6, 5)]);
        var carry = CarryLayout(bag);
        var before = Totals(bag);

        bag.Tidy(BagSortKey.Recent, BagFilter.All);
        Assert.Equal(carry, CarryLayout(bag));
        Assert.Equal("seed:-:6 turnip:common:4 iron:fine:2 _", Layout(bag));
        Assert.Equal(before, Totals(bag));

        bag.Sort(BagSortKey.Quality, BagFilter.All);
        Assert.Equal(carry, CarryLayout(bag));
    }

    [Fact]
    public void 排序与整理之后随身栏那一排一格不变_空着的那一格也没被网格里的东西填上()
    {
        // 随身栏那一排是零散的种子与铁、最后一格空着；网格里也是零散的种子与铁。整理把网格里的并起来、排好，
        // 随身栏那一排一格不变 —— 玩家手上那一格不会在他没按任何键时换了东西，空着的那一格也不会被填上。
        var carryCells = CarrySlotBar.SlotCount;
        ItemStack? Scattered(int i) => Stack(i % 2 == 0 ? Seed : Iron, i % 2 == 0 ? null : Fine, 1, i + 1);
        var carry = Enumerable.Range(0, carryCells).Select(i => i < carryCells - 1 ? Scattered(i) : null).ToList();
        var grid = Enumerable.Range(carryCells, 6).Select(Scattered).ToList();
        var bag = WithCarry(carry, grid);
        var controls = new BagControls();
        var before = CarryLayout(bag);
        var gridBefore = Layout(bag);

        controls.SortBy(bag, BagSortKey.Quality);
        Assert.Equal(before, CarryLayout(bag));
        controls.Tidy(bag);
        Assert.Equal(before, CarryLayout(bag));
        Assert.Null(bag.Carry[^1]);
        Assert.NotEqual(gridBefore, Layout(bag));

        // 反过来：同一批随身栏里的东西放在网格里，整理就会把它们并起来、挪开 —— 上面那几条不是碰巧没动。
        var exposed = Row(carry);
        exposed.Tidy(BagSortKey.Quality, BagFilter.All);
        Assert.NotEqual(before, Layout(exposed));
    }

    [Fact]
    public void 筛选态下排序与整理只在看得见的那几格里重排_被筛掉的那几堆待在原地()
    {
        var bag = Row(
        [
            Stack(Seed, null, 5, 1), Stack(Turnip, Common, 3, 2), null, Stack(Iron, Fine, 2, 3), Stack(Turnip, Mutated, 4, 4),
        ]);
        var herbs = new BagFilter("herb", null);

        bag.Sort(BagSortKey.Quality, herbs);

        // 看得见的是第 1、2、4 格（种子与铁被筛掉，空格看得见）：两堆芜菁按品质摆进前两格，第 4 格变空。
        Assert.Equal("seed:-:5 turnip:mutated:4 turnip:common:3 iron:fine:2 _", Layout(bag));

        bag.Tidy(BagSortKey.Recent, herbs);
        Assert.Equal("seed", bag.Slots[0]?.ItemId);
        Assert.Equal("iron", bag.Slots[3]?.ItemId);
    }

    [Fact]
    public void 按大类筛连它下面的小类一起看得见_按品质筛时没有品质的看不见()
    {
        var materials = new BagFilter("material", null);
        Assert.True(materials.Shows(Stack(Turnip, Common, 1, 1), Items));
        Assert.True(materials.Shows(Stack(Iron, Fine, 1, 1), Items));
        Assert.False(materials.Shows(Stack(Seed, null, 1, 1), Items));

        var fine = new BagFilter(null, Fine);
        Assert.True(fine.Shows(Stack(Iron, Fine, 1, 1), Items));
        Assert.False(fine.Shows(Stack(Turnip, Common, 1, 1), Items));
        Assert.False(fine.Shows(Stack(Seed, null, 1, 1), Items));

        // 两样一起筛是「都要满足」。
        Assert.False(new BagFilter("herb", Fine).Shows(Stack(Iron, Fine, 1, 1), Items));
        Assert.True(BagFilter.All.Shows(Stack(Letter, null, 1, 1), Items));
    }

    [Fact]
    public void 挪格_目标格空着就搬过去_有东西就两格互换()
    {
        var bag = FiveKinds();

        bag.Move(0, 6);
        Assert.Equal("_ turnip:common:3 iron:fine:2 turnip:mutated:4 letter:-:1 _ seed:-:5 _", Layout(bag));

        bag.Move(1, 2);
        Assert.Equal("_ iron:fine:2 turnip:common:3 turnip:mutated:4 letter:-:1 _ seed:-:5 _", Layout(bag));

        bag.Move(3, 3);
        Assert.Equal("_ iron:fine:2 turnip:common:3 turnip:mutated:4 letter:-:1 _ seed:-:5 _", Layout(bag));

        // 同标识的两堆挪到一起也只是互换：合堆是整理的事。
        var two = Row([Stack(Turnip, Common, 3, 1), Stack(Turnip, Common, 4, 2)]);
        two.Move(0, 1);
        Assert.Equal("turnip:common:4 turnip:common:3", Layout(two));

        Assert.Throws<ArgumentOutOfRangeException>(() => bag.Move(0, 8));
        Assert.Throws<ArgumentOutOfRangeException>(() => bag.Move(-1, 0));
    }

    [Fact]
    public void 丢弃把一整堆移出背包_不可丢弃的被拒且一件不少_空格没东西可丢()
    {
        var bag = FiveKinds();

        Assert.Equal(BagDiscardResult.Discarded, bag.TryDiscardAt(1));
        Assert.Null(bag.Slots[1]);

        Assert.Equal(BagDiscardResult.NotDiscardable, bag.TryDiscardAt(4));
        Assert.Equal(Letter, bag.Slots[4]?.ItemId);

        Assert.Equal(BagDiscardResult.NothingThere, bag.TryDiscardAt(1));
        Assert.Throws<ArgumentOutOfRangeException>(() => bag.TryDiscardAt(8));
    }

    [Fact]
    public void 筛选态下挪格与丢弃的结果与不筛时一样_碰被筛掉的那一格当场抛()
    {
        var filtered = FiveKinds();
        var plain = FiveKinds();
        var herbs = new BagControls();
        herbs.NextCategory(Items);
        herbs.NextCategory(Items);
        Assert.Equal("herb", herbs.Filter.CategoryId);
        var all = new BagControls();

        // 第 1 格与第 3 格是两堆芜菁，第 5 格空着：筛着芜菁时它们都看得见。
        herbs.Move(filtered, 1, 5);
        all.Move(plain, 1, 5);
        Assert.Equal(Layout(plain), Layout(filtered));

        Assert.Equal(all.Discard(plain, 3), herbs.Discard(filtered, 3));
        Assert.Equal(Layout(plain), Layout(filtered));

        var unchanged = Layout(filtered);
        Assert.Throws<InvalidOperationException>(() => herbs.Move(filtered, 0, 6));
        Assert.Throws<InvalidOperationException>(() => herbs.Discard(filtered, 2));
        Assert.Equal(unchanged, Layout(filtered));
    }

    [Fact]
    public void 当前排序键是上一次按的那个_一键整理按它排_进来时是按类别()
    {
        var controls = new BagControls();
        Assert.Equal(BagSortKey.Category, controls.SortKey);
        Assert.Equal(BagFilter.All, controls.Filter);

        var bag = FiveKinds();
        controls.SortBy(bag, BagSortKey.Recent);
        Assert.Equal(BagSortKey.Recent, controls.SortKey);

        bag.Move(0, 7);
        controls.Tidy(bag);
        Assert.Equal("letter:-:1 turnip:mutated:4 iron:fine:2 turnip:common:3 seed:-:5 _ _ _", Layout(bag));
    }

    [Fact]
    public void 两种筛选按顺序换档_到头回到全部_换一种不动另一种()
    {
        Assert.Equal([null, "material", "herb", "ore", "quest", "seed"], BagControls.CategoryChoices(Items));
        Assert.Equal([null, Common, Fine, Mutated], BagControls.QualityChoices(Items));

        var controls = new BagControls();
        controls.NextQuality(Items);
        Assert.Equal(new BagFilter(null, Common), controls.Filter);

        foreach (var expected in (string?[])["material", "herb", "ore", "quest", "seed", null])
        {
            controls.NextCategory(Items);
            Assert.Equal(new BagFilter(expected, Common), controls.Filter);
        }
    }

    /// <summary>页上的号先随身栏那一排、再网格；点页上的格挪的、丢的就是背包里那一处。越界当场抛。</summary>
    [Fact]
    public void 页上的号先随身栏再网格_越界当场抛()
    {
        var bag = WithCarry([Stack(Seed, null, 5, 1), null], [null, Stack(Iron, Fine, 2, 2), null]);

        Assert.Equal(BagPlace.Carry(0), BagControls.PlaceOf(bag, 0));
        Assert.Equal(BagPlace.Carry(1), BagControls.PlaceOf(bag, 1));
        Assert.Equal(BagPlace.Grid(0), BagControls.PlaceOf(bag, 2));
        Assert.Equal(BagPlace.Grid(2), BagControls.PlaceOf(bag, 4));
        Assert.Throws<ArgumentOutOfRangeException>(() => BagControls.PlaceOf(bag, 5));
        Assert.Throws<ArgumentOutOfRangeException>(() => BagControls.PlaceOf(bag, -1));

        var controls = new BagControls();
        Assert.Equal(BagMoveResult.Moved, controls.Move(bag, 0, 2));
        Assert.Equal("_ _", CarryLayout(bag));
        Assert.Equal("seed:-:5 iron:fine:2 _", Layout(bag));

        Assert.Equal(BagMoveResult.Moved, controls.Move(bag, 3, 1));
        Assert.Equal("_ iron:fine:2", CarryLayout(bag));
        Assert.Equal(BagDiscardResult.Discarded, controls.Discard(bag, 1));
        Assert.Equal("_ _", CarryLayout(bag));
    }

    private static ItemCategory Category(string id, string? parent) =>
        new() { Id = id, NameKey = $"item_category.{id}.name", ParentId = parent };

    private static ItemDefinition Item(string id, string category, bool hasQuality, int stack, bool discardable = true) => new()
    {
        Id = id,
        NameKey = $"item.{id}.name",
        DescriptionKey = $"item.{id}.description",
        CategoryId = category,
        Footprint = new() { Width = 1, Height = 1 },
        RarityId = "low",
        HasQuality = hasQuality,
        StackLimit = stack,
        BasePriceCopper = null,
        Discardable = discardable,
        Sellable = true,
        UseEffectId = null,
        ConfirmBeforeUse = false,
    };
}
