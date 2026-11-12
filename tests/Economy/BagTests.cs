using Tinderhearth.Rules.Economy;
using Tinderhearth.Rules.Foundation.Config;
using Tinderhearth.Rules.Progression;
using Tinderhearth.Rules.UI;
using Xunit;
using Xunit.Sdk;

namespace Tinderhearth.Rules.Tests.Economy;

/// <summary>
/// 主角的背包是一张网格加随身栏那一排：同种同品质合堆、堆满再放下一块、同种不同品质分块、无品质只按种类分；新的一块从上到下、
/// 从左到右找第一块放得下它整块占地的空地，占 1 格的先找随身栏那一排；满载的两个条件，放不下整份拒收，被拒分「满了」与
/// 「放不下这么大的」；大东西进不了随身栏那一排；网格多大从配置读。
/// </summary>
/// <remarks>
/// 规则在设计仓 design/背包系统.md 的「一堆占一块，堆的标识是「种类 ＋ 品质」」「一堆放在哪：先合堆，再找第一块够大的空地，
/// 不能旋转」与「随身栏是背包上单独一排，只放占 1 格的东西」三节。物品用一份测试自己的小表，堆上限与网格大小是随手挑的、不是数值。
/// </remarks>
public class BagTests
{
    private const string Turnip = "turnip";
    private const string Seed = "turnip_seed";
    private const string Stone = "stone";
    private const string Shield = "shield";
    private const string Common = "common";
    private const string Mutated = "mutated";

    /// <summary>
    /// 芜菁有品质、一堆 20；种子无品质、一堆 20；石头无品质、一堆 5 —— 两种堆上限，好看出它按种类读。
    /// 盾牌占 2×2、一堆 1 件：大东西一块占四格。
    /// </summary>
    private static readonly ItemCatalog Items = ItemCatalog.Build(
        [
            Item(Turnip, hasQuality: true, stackLimit: 20),
            Item(Seed, hasQuality: false, stackLimit: 20),
            Item(Stone, hasQuality: false, stackLimit: 5),
            Item(Shield, hasQuality: false, stackLimit: 1, width: 2, height: 2),
        ],
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
        var bag = new Bag(4, 1, Items);

        Assert.True(bag.TryAdd(Turnip, Common, 3));
        Assert.True(bag.TryAdd(Turnip, Common, 4));

        var stack = Assert.Single(Stacks(bag));
        Assert.Equal(7, stack.Count);
        Assert.Equal(3, bag.EmptySlotCount);
    }

    [Fact]
    public void 堆到那种物品的上限再放下一块()
    {
        var bag = new Bag(4, 1, Items);

        Assert.True(bag.TryAdd(Turnip, Common, 25));

        Assert.Equal([20, 5], Stacks(bag).Select(s => s.Count));
        Assert.Equal(Turnip, bag.Slots[0]?.ItemId);
        Assert.Equal(Turnip, bag.Slots[1]?.ItemId);
    }

    /// <summary>同种不同品质各占一块。后半是反证：断言「它们合成了一堆」必须失败，否则这条判不出分堆。</summary>
    [Fact]
    public void 同种不同品质各占一块()
    {
        var bag = new Bag(4, 1, Items);

        Assert.True(bag.TryAdd(Turnip, Common, 1));
        Assert.True(bag.TryAdd(Turnip, Mutated, 1));

        Assert.Equal([Common, Mutated], Stacks(bag).Select(s => s.QualityId));
        Assert.Throws<SingleException>(() => Assert.Single(Stacks(bag)));
    }

    [Fact]
    public void 无品质的只按种类分堆()
    {
        var bag = new Bag(4, 1, Items);

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
        var bag = new Bag(2, 1, Items);
        Assert.True(bag.TryAdd(Turnip, Common, 10));
        Assert.True(bag.TryAdd(Stone, null, 5));
        Assert.Equal(0, bag.EmptySlotCount);

        Assert.False(bag.IsFullFor(Turnip, Common));
        Assert.True(bag.TryAdd(Turnip, Common, 10));

        Assert.Equal([20, 5], Stacks(bag).Select(s => s.Count));
    }

    /// <summary>满载是两个条件都不成立：没有可合的同标识堆，也没有空地。一格空的都没有时，被拒的原因是满了。</summary>
    [Fact]
    public void 没有可合的同标识堆也没有空格就是满了()
    {
        var bag = new Bag(2, 1, Items);
        Assert.True(bag.TryAdd(Turnip, Common, 20));
        Assert.True(bag.TryAdd(Stone, null, 5));

        Assert.True(bag.IsFullFor(Turnip, Common));
        Assert.True(bag.IsFullFor(Turnip, Mutated));
        Assert.False(bag.TryAdd(Turnip, Common, 1));
        Assert.Equal(BagRefusal.Full, bag.RefusalFor(Turnip, Common, 1));
        Assert.Equal(BagRefusal.Full, bag.RefusalFor(Shield, null, 1));
    }

    /// <summary>一次放进来的那一份放不下，整份被拒：一件都不放，每一格都与之前一样。</summary>
    [Fact]
    public void 放不下的那一份整份被拒而背包一件不变()
    {
        var bag = new Bag(2, 1, Items);
        Assert.True(bag.TryAdd(Turnip, Common, 15));
        var before = bag.Slots.ToList();

        Assert.False(bag.CanAdd(Turnip, Common, 26));
        Assert.False(bag.TryAdd(Turnip, Common, 26));

        Assert.Equal(before, bag.Slots);
        Assert.Equal(1, bag.EmptySlotCount);
    }

    /// <summary>堆上限按种类读物品定义：同一份背包里，石头五件一堆、芜菁二十件一堆。</summary>
    [Fact]
    public void 堆上限按种类从物品定义读()
    {
        var bag = new Bag(4, 1, Items);

        Assert.True(bag.TryAdd(Stone, null, 7));
        Assert.True(bag.TryAdd(Turnip, Common, 7));

        Assert.Equal([5, 2, 7], Stacks(bag).Select(s => s.Count));
    }

    /// <summary>新的一块从上到下、从左到右找第一个放得下它整块占地的位置；那一块里的每一格都被它盖上。</summary>
    [Fact]
    public void 大东西放在从上到下从左到右第一块放得下它的地方()
    {
        var bag = new Bag(4, 3, Items);
        Assert.True(bag.TryAdd(Stone, null, 1));

        Assert.True(bag.TryAdd(Shield, null, 1));

        // 左上角那一格被石头占着，第一行里左上角落在第 1 格（从 0 数）的那一块就放得下：盖住 (1,0)(2,0)(1,1)(2,1)。
        Assert.Equal(Shield, bag.Slots[1]?.ItemId);
        Assert.Equal(12 - 1 - 4, bag.EmptySlotCount);
    }

    /// <summary>第一行凑不出一块时往下一行找，仍然从左边起。</summary>
    [Fact]
    public void 第一行凑不出就往下一行从左边起找()
    {
        var bag = new Bag(4, 3, Items);
        Assert.True(bag.TryAdd(Stone, null, 15));

        // 石头一堆 5 件，三堆占了第一行左边三格；第一行剩的那一格放不下 2×2，往下一行从左边起：左上角在 (0,1)，第 4 格。
        Assert.True(bag.TryAdd(Shield, null, 1));

        Assert.Equal(Shield, bag.Slots[4]?.ItemId);
    }

    /// <summary>那一块的每一格都要空着、都在网格里面：贴着右边或下边、出了网格的位置不算。</summary>
    [Fact]
    public void 那一块的每一格都要空着也都在网格里面()
    {
        // 3×2 的网格，第一行左边两格有东西：空着的四格是 (2,0)(0,1)(1,1)(2,1)，凑不出一块 2×2 ——
        // 左上角放 (1,0) 压着石头，放 (2,0) 出了右边，放在第二行出了下边。
        var bag = Bag.Restore(3, 2, [Stack(Stone, null, 1, 1), Stack(Stone, null, 1, 2), null, null, null, null], Items);

        Assert.Equal(4, bag.EmptySlotCount);
        Assert.False(bag.TryAdd(Shield, null, 1));
        Assert.Equal(BagRefusal.NoRoomForSize, bag.RefusalFor(Shield, null, 1));
        Assert.Equal(4, bag.EmptySlotCount);
    }

    /// <summary>空格的数目够、却凑不出一块够大的空地时被拒；挪开挡着的那一堆之后，同一件放得进去。</summary>
    [Fact]
    public void 空格够数却凑不出一块够大的空地时被拒_挪开挡着的那一堆之后放得进去()
    {
        // 3×2 的网格，第一行正中间有一块石头：五格空着，可每一块 2×2 都压着它或出了网格。
        var bag = Bag.Restore(3, 2, [null, Stack(Stone, null, 1, 1), null, null, null, null], Items);
        Assert.Equal(5, bag.EmptySlotCount);

        Assert.False(bag.TryAdd(Shield, null, 1));
        Assert.Equal(BagRefusal.NoRoomForSize, bag.RefusalFor(Shield, null, 1));

        Assert.True(bag.TryRemoveAt(1, 1));
        Assert.True(bag.TryAdd(Shield, null, 1));
        Assert.Equal(Shield, bag.Slots[0]?.ItemId);
    }

    /// <summary>一份要分几块时一块一块找地方；有一块找不到，整份被拒、一件不放，原因按卡住的那一刻算。</summary>
    [Fact]
    public void 好几块的那一份有一块找不到地方就整份被拒()
    {
        var bag = new Bag(3, 2, Items);

        Assert.False(bag.TryAdd(Shield, null, 2));
        Assert.Equal(BagRefusal.NoRoomForSize, bag.RefusalFor(Shield, null, 2));
        Assert.Equal(6, bag.EmptySlotCount);
        Assert.Empty(Stacks(bag));

        // 同一块地方，只放一块就放得下。
        Assert.True(bag.TryAdd(Shield, null, 1));
        Assert.Equal(2, bag.EmptySlotCount);

        // 剩下那两格只放得下小东西：一格空的都没有之前，被拒的原因都是「放不下这么大的」。
        Assert.Equal(BagRefusal.NoRoomForSize, bag.RefusalFor(Shield, null, 1));
        Assert.True(bag.TryAdd(Stone, null, 10));
        Assert.Equal(BagRefusal.Full, bag.RefusalFor(Shield, null, 1));
        Assert.Equal(BagRefusal.Full, bag.RefusalFor(Stone, null, 1));
    }

    [Fact]
    public void 放得下时问不出被拒的原因()
    {
        var bag = new Bag(2, 2, Items);

        Assert.Null(bag.RefusalFor(Shield, null, 1));
        Assert.Null(bag.RefusalFor(Turnip, Common, 80));
        Assert.Equal(BagRefusal.Full, bag.RefusalFor(Turnip, Common, 81));
    }

    /// <summary>读回来的那几堆：出了网格、或者两堆压在一起，载入被拒并说清是哪一格。</summary>
    [Fact]
    public void 读回来的那几堆出了网格或压在一起就被拒()
    {
        var outside = Assert.Throws<InvalidDataException>(
            () => Bag.Restore(3, 2, [null, null, Stack(Shield, null, 1, 1), null, null, null], Items));
        Assert.Contains("第 3 格", outside.Message);
        Assert.Contains("出了 3×2 的网格", outside.Message);

        var overlap = Assert.Throws<InvalidDataException>(
            () => Bag.Restore(3, 2, [Stack(Shield, null, 1, 1), Stack(Shield, null, 1, 2), null, null, null, null], Items));
        Assert.Contains("第 2 格", overlap.Message);
        Assert.Contains("与第 1 格那一堆压在一起", overlap.Message);
    }

    /// <summary>开新档时的网格按背包等级从配置读，等级是第一级；配置里没有的那一级当场抛。随身栏那一排几格由调用方给。</summary>
    [Fact]
    public void 网格多大按背包等级从配置读()
    {
        var config = Config("""[{ "width": 6, "height": 2 }, { "width": 5, "height": 4 }]""");

        var bag = Bag.NewGame(config, Items, carryCells: 3);
        Assert.Equal(6, bag.GridWidth);
        Assert.Equal(2, bag.GridHeight);
        Assert.Equal(12, bag.SlotCount);
        Assert.Equal(3, bag.CarryCount);
        Assert.Equal(15, bag.EmptySlotCount);
        Assert.True(bag.IsEmpty);
        Assert.Equal(new GridSize(5, 4), config.BackpackGridAt(2));
        Assert.Throws<ArgumentOutOfRangeException>(() => config.BackpackGridAt(3));
        Assert.Empty(Stacks(bag));
    }

    // ── 随身栏那一排 ──────────────────────────────────────────────────

    /// <summary>主角的背包上随身栏那一排与技能位同数：农场照这一格建背包，屏幕上的随身栏照它画。</summary>
    [Fact]
    public void 主角背包上随身栏那一排与技能位同数()
    {
        var bag = Bag.NewGame(Config("""[{ "width": 6, "height": 2 }]"""), Items, CarrySlotBar.SlotCount);

        Assert.Equal(InputActions.Skills.Count, bag.CarryCount);
        Assert.Equal(bag.CarryCount, bag.Carry.Count);
        Assert.All(bag.Carry, s => Assert.Null(s));
    }

    /// <summary>占 1 格的新东西先找随身栏那一排的空格，那一排满了才进网格；先进的在前。</summary>
    [Fact]
    public void 占一格的新东西先进随身栏那一排_满了才进网格()
    {
        var bag = new Bag(3, 1, carryCells: 2, Items);

        Assert.True(bag.TryAdd(Stone, null, 12));

        Assert.Equal([5, 5], bag.Carry.Select(s => s?.Count));
        Assert.Equal([2, null, null], bag.Slots.Select(s => s?.Count));
    }

    /// <summary>合堆先找随身栏那一排、再找网格；合满了还剩的才放新的一块。</summary>
    [Fact]
    public void 合堆先找随身栏那一排再找网格()
    {
        var bag = Bag.Restore(
            [Stack(Seed, null, 15, 1), null], 3, 1, [Stack(Seed, null, 10, 2), null, null], Items);

        Assert.True(bag.TryAdd(Seed, null, 8));
        Assert.Equal(20, bag.Carry[0]?.Count);
        Assert.Equal(13, bag.Slots[0]?.Count);

        Assert.True(bag.TryAdd(Seed, null, 10));
        Assert.Equal(20, bag.Slots[0]?.Count);
        Assert.Equal(3, bag.Carry[1]?.Count);
    }

    /// <summary>大东西不进随身栏那一排：那一排空着也直接找网格；网格放不下就被拒，原因是放不下这么大的。</summary>
    [Fact]
    public void 大东西直接找网格_随身栏那一排空着也不进去()
    {
        var bag = new Bag(2, 2, carryCells: 2, Items);

        Assert.True(bag.TryAdd(Shield, null, 1));
        Assert.All(bag.Carry, s => Assert.Null(s));
        Assert.Equal(Shield, bag.Slots[0]?.ItemId);

        Assert.False(bag.TryAdd(Shield, null, 1));
        Assert.Equal(BagRefusal.NoRoomForSize, bag.RefusalFor(Shield, null, 1));
        Assert.All(bag.Carry, s => Assert.Null(s));

        // 随身栏那一排也装满之后，一格空的都没有了：被拒的原因是满了。
        Assert.True(bag.TryAdd(Stone, null, 10));
        Assert.Equal(BagRefusal.Full, bag.RefusalFor(Shield, null, 1));
        Assert.Equal(BagRefusal.Full, bag.RefusalFor(Stone, null, 1));
    }

    /// <summary>占不止一格的东西挪不进随身栏那一排，说得出是太大；那一堆留在原处，随身栏那一格也不动。</summary>
    [Fact]
    public void 大东西挪进随身栏被拒_那一堆留在原处()
    {
        var bag = Bag.Restore([Stack(Stone, null, 2, 1), null], 2, 2, [Stack(Shield, null, 1, 2), null, null, null], Items);
        var carry = bag.Carry.ToList();
        var grid = bag.Slots.ToList();

        Assert.Equal(BagMoveResult.TooBigForCarry, bag.Move(BagPlace.Grid(0), BagPlace.Carry(1)));
        Assert.Equal(BagMoveResult.TooBigForCarry, bag.Move(BagPlace.Carry(0), BagPlace.Grid(0)));

        Assert.Equal(carry, bag.Carry);
        Assert.Equal(grid, bag.Slots);
    }

    /// <summary>占 1 格的东西在随身栏那一排与网格之间挪得来挪得去，目标有东西就两边互换。</summary>
    [Fact]
    public void 占一格的东西在随身栏与网格之间挪_有东西就互换()
    {
        var bag = Bag.Restore([Stack(Stone, null, 2, 1), null], 2, 1, [Stack(Seed, null, 3, 2), null], Items);

        Assert.Equal(BagMoveResult.Moved, bag.Move(BagPlace.Carry(0), BagPlace.Grid(1)));
        Assert.Equal([null, null], bag.Carry.Select(s => s?.ItemId));
        Assert.Equal([Seed, Stone], bag.Slots.Select(s => s?.ItemId));

        Assert.Equal(BagMoveResult.Moved, bag.Move(BagPlace.Grid(0), BagPlace.Carry(1)));
        Assert.Equal([null, Seed], bag.Carry.Select(s => s?.ItemId));

        Assert.Equal(BagMoveResult.Moved, bag.Move(BagPlace.Carry(1), BagPlace.Grid(1)));
        Assert.Equal([null, Stone], bag.Carry.Select(s => s?.ItemId));
        Assert.Equal([null, Seed], bag.Slots.Select(s => s?.ItemId));
    }

    /// <summary>读回来的随身栏那一排里放着占不止一格的东西，载入被拒并说清是哪一格。</summary>
    [Fact]
    public void 读回来的随身栏那一排里有大东西就被拒()
    {
        var e = Assert.Throws<InvalidDataException>(
            () => Bag.Restore([null, Stack(Shield, null, 1, 1)], 2, 2, [null, null, null, null], Items));

        Assert.Contains("随身栏第 2 格", e.Message);
        Assert.Contains("只放占 1 格的东西", e.Message);
    }

    /// <summary>配置里没写背包网格、写了空表、某一级的宽或高不为正或干脆没写，读配置时当场报错 —— 没有能用的默认值。</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("[]")]
    [InlineData("""[{ "width": 6, "height": 2 }, { "width": 6, "height": 0 }]""")]
    [InlineData("""[{ "width": 6 }]""")]
    public void 背包网格没配好就当场报错(string? grid)
    {
        var json = grid is null
            ? """{ "rosterCapacity": 9, "buildableWidthCells": 80, "buildableHeightCells": 60 }"""
            : $$"""{ "rosterCapacity": 9, "buildableWidthCells": 80, "buildableHeightCells": 60, "backpackGridByLevel": {{grid}} }""";

        var error = Assert.ThrowsAny<ArgumentException>(() => GameConfig.Parse(json));

        Assert.Contains("BackpackGridByLevel", error.Message);
    }

    /// <summary>物品不在册、品质那一位与物品的声明对不上、件数不为正，是调用方的错：当场抛，不当成放不下。</summary>
    [Fact]
    public void 调用方给错的那一份当场抛()
    {
        var bag = new Bag(4, 1, Items);

        Assert.Throws<ArgumentException>(() => bag.TryAdd("ghost", null, 1));
        Assert.Throws<ArgumentException>(() => bag.TryAdd(Turnip, null, 1));
        Assert.Throws<ArgumentException>(() => bag.TryAdd(Seed, Common, 1));
        Assert.Throws<ArgumentException>(() => bag.TryAdd(Turnip, "legendary", 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => bag.TryAdd(Turnip, Common, 0));
        Assert.Throws<ArgumentException>(() => bag.RefusalFor("ghost", null, 1));
        Assert.Empty(Stacks(bag));
    }

    /// <summary>每一堆记着自己最近一次进东西的先后序号：后进的更大，合进旧的那一堆也算一次新的。</summary>
    [Fact]
    public void 最近获得跟着每一次进东西走()
    {
        var bag = new Bag(4, 1, Items);
        Assert.True(bag.TryAdd(Turnip, Common, 1));
        Assert.True(bag.TryAdd(Stone, null, 1));
        Assert.True(bag.Slots[1]!.Value.AcquiredSequence > bag.Slots[0]!.Value.AcquiredSequence);

        Assert.True(bag.TryAdd(Turnip, Common, 1));

        Assert.True(bag.Slots[0]!.Value.AcquiredSequence > bag.Slots[1]!.Value.AcquiredSequence);
    }

    /// <summary>复制出来的那一份与原件不共用会变的东西：往副本里放一块大东西，原件一格不变。</summary>
    [Fact]
    public void 复制出来的那一份与原件互不影响()
    {
        var bag = new Bag(3, 2, Items);
        var copy = bag.Clone();

        Assert.True(copy.TryAdd(Shield, null, 1));

        Assert.Equal(6, bag.EmptySlotCount);
        Assert.Equal(2, copy.EmptySlotCount);
        Assert.True(bag.CanAdd(Shield, null, 1));
    }

    private static List<ItemStack> Stacks(Bag bag) => [.. bag.Slots.Where(s => s is not null).Select(s => s!.Value)];

    private static ItemStack Stack(string item, string? quality, int count, int sequence) => new(item, quality, count, sequence);

    private static GameConfig Config(string backpackGrid) => GameConfig.Parse(
        $$"""{ "rosterCapacity": 9, "buildableWidthCells": 80, "buildableHeightCells": 60, "backpackGridByLevel": {{backpackGrid}} }""");

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
