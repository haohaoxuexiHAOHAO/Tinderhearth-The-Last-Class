using Tinderhearth.Rules.Economy;
using Tinderhearth.Rules.Progression;
using Tinderhearth.Rules.UI;
using Xunit;

namespace Tinderhearth.Rules.Tests.Economy;

/// <summary>
/// 背包页上按方向键：一堆占几格都只停一下，走进一块落在对着原来那一行（列）的那一格；被筛掉的格跳过去、不斜着拐，这一行上
/// 没有了才去那一边最近的那一样；拿着一堆时，拿起来的那一块里一格一格走。每一堆、每一格空地都走得到，被筛掉的走不到。
/// </summary>
/// <remarks>
/// 物品用一份测试自己的小表：盾占 2×2，石头、种子、芜菁各占 1 格；只有芜菁有品质，所以按品质筛「寻常」时只剩芜菁与空格。
/// </remarks>
public class BagPageFocusTests
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
    /// 随身栏 4 格（第 0 格一堆种子），下面 4×3 的网格：盾在网格 (0, 0)、石头在 (3, 0)。页上的号：
    /// 第 0 行是随身栏 0–3；第 1 行 4（盾）5（盾）6 7（石头）；第 2 行 8（盾）9（盾）10 11；第 3 行 12–15。
    /// </summary>
    private static Bag Sample() => Bag.Restore(
        [Stack(Seed, null, 3, 1), null, null, null],
        4, 3,
        [Stack(Shield, null, 1, 2), null, null, Stack(Stone, null, 2, 3), null, null, null, null, null, null, null, null],
        Items);

    [Fact]
    public void 一堆占几格都只停一下_按一下就出了那一块()
    {
        var focus = BagPageFocus.Of(Sample(), new BagControls(), held: null);

        Assert.Equal(5, focus.Next(6, FocusDirection.Left));
        Assert.Equal(6, focus.Next(5, FocusDirection.Right));
        Assert.Equal(13, focus.Next(5, FocusDirection.Down));
        Assert.Equal(1, focus.Next(9, FocusDirection.Up));
    }

    [Fact]
    public void 走进一块_落在对着原来那一行或那一列的那一格()
    {
        var focus = BagPageFocus.Of(Sample(), new BagControls(), held: null);

        // 从盾右边、第 2 行那一格往左：落在盾的第 2 行，不是它左上角那一格（页上第 4 格）。
        Assert.Equal(9, focus.Next(10, FocusDirection.Left));
        // 从盾下面、第 1 列那一格往上：落在盾的第 1 列。
        Assert.Equal(9, focus.Next(13, FocusDirection.Up));
    }

    [Fact]
    public void 那一边一样都没有_就是走出网格()
    {
        var focus = BagPageFocus.Of(Sample(), new BagControls(), held: null);

        Assert.Null(focus.Next(0, FocusDirection.Up));
        Assert.Null(focus.Next(7, FocusDirection.Right));
        Assert.Null(focus.Next(12, FocusDirection.Down));
        Assert.Null(focus.Next(12, FocusDirection.Left));
        // 盾贴着左边：从它右边那一列往左，越过整块之后什么都没有。
        Assert.Null(focus.Next(5, FocusDirection.Left));
    }

    [Fact]
    public void 被筛掉的格跳过去接着找_不斜着拐()
    {
        // 一行四格：芜菁、两堆石头、一格空地；下面一行全空。只看寻常，两堆石头被筛掉。
        var bag = Bag.Restore(
            4, 2, [Stack(Turnip, Common, 1, 1), Stack(Stone, null, 1, 2), Stack(Stone, null, 1, 3), null, null, null, null, null], Items);
        var controls = new BagControls();
        controls.NextQuality(Items);
        var focus = BagPageFocus.Of(bag, controls, held: null);

        // 斜下方那一格（第 5 格）挨着芜菁的角，引擎自带的找法会走过去；这里照同一行接着找。
        Assert.Equal(3, focus.Next(0, FocusDirection.Right));
        Assert.Equal(0, focus.Next(3, FocusDirection.Left));
    }

    [Fact]
    public void 这一行上没有了_才去那一边最近的那一样()
    {
        // 芜菁右边三堆石头都被筛掉：同一行上没有了，去右边最近的那一格空地，下一列、下一行的那一格。
        var bag = Bag.Restore(
            4, 2, [Stack(Turnip, Common, 1, 1), Stack(Stone, null, 1, 2), Stack(Stone, null, 1, 3), Stack(Stone, null, 1, 4), null, null, null, null],
            Items);
        var controls = new BagControls();
        controls.NextQuality(Items);
        var focus = BagPageFocus.Of(bag, controls, held: null);

        Assert.Equal(5, focus.Next(0, FocusDirection.Right));
        Assert.Null(focus.Next(7, FocusDirection.Right));
    }

    [Fact]
    public void 拿着一堆时_拿起来的那一块里一格一格走_别的那几块照旧一下越过()
    {
        var holdingShield = BagPageFocus.Of(Sample(), new BagControls(), held: 4);

        Assert.Equal(5, holdingShield.Next(4, FocusDirection.Right));
        Assert.Equal(8, holdingShield.Next(4, FocusDirection.Down));
        Assert.Equal(6, holdingShield.Next(5, FocusDirection.Right));

        var holdingSeed = BagPageFocus.Of(Sample(), new BagControls(), held: 0);

        Assert.Equal(13, holdingSeed.Next(5, FocusDirection.Down));
    }

    [Fact]
    public void 往网格外面找时_从现在这一样朝那一边的边上对着这一行的那一格找()
    {
        var focus = BagPageFocus.Of(Sample(), new BagControls(), held: null);

        // 盾占页上 4、5、8、9：从它左下那一格往右，边上那一格是右下；从右上那一格往左，是左上。
        Assert.Equal(9, focus.EdgeToward(8, FocusDirection.Right));
        Assert.Equal(4, focus.EdgeToward(5, FocusDirection.Left));
        Assert.Equal(9, focus.EdgeToward(5, FocusDirection.Down));
        Assert.Equal(5, focus.EdgeToward(9, FocusDirection.Up));
        // 一格大的就是它自己；拿着盾时，盾的每一格也各是一样。
        Assert.Equal(6, focus.EdgeToward(6, FocusDirection.Right));
        Assert.Equal(4, BagPageFocus.Of(Sample(), new BagControls(), held: 4).EdgeToward(4, FocusDirection.Right));
    }

    [Fact]
    public void 随身栏那一排不满一行时_往上走到最近的那一格()
    {
        // 随身栏 3 格、网格 4 宽：网格第一行最右那一格（页上第 6 格）上面没有格。
        var bag = Bag.Restore([null, null, null], 4, 2, new ItemStack?[8], Items);
        var focus = BagPageFocus.Of(bag, new BagControls(), held: null);

        Assert.Equal(2, focus.Next(6, FocusDirection.Up));
    }

    [Fact]
    public void 每一堆每一格空地都走得到_被筛掉的那几格一格都走不到()
    {
        AssertAllReachable(Sample(), new BagControls());

        // 3×3 塞满，只看寻常时剩三堆芜菁，两两不同行也不同列：同一行、同一列上都没有，只能去那一边最近的那一样。
        var full = Bag.Restore(
            3, 3,
            [
                Stack(Turnip, Common, 1, 1), Stack(Stone, null, 1, 2), Stack(Stone, null, 1, 3),
                Stack(Stone, null, 1, 4), Stack(Stone, null, 1, 5), Stack(Turnip, Common, 1, 6),
                Stack(Stone, null, 1, 7), Stack(Turnip, Common, 1, 8), Stack(Stone, null, 1, 9),
            ],
            Items);
        var controls = new BagControls();
        controls.NextQuality(Items);
        AssertAllReachable(full, controls);
    }

    [Fact]
    public void 从被筛掉的格出发_或者说手上拿着一格空地_当场报错()
    {
        var bag = Bag.Restore(2, 1, [Stack(Turnip, Common, 1, 1), Stack(Stone, null, 1, 2)], Items);
        var controls = new BagControls();
        controls.NextQuality(Items);

        Assert.Throws<InvalidOperationException>(() => BagPageFocus.Of(bag, controls, held: null).Next(1, FocusDirection.Left));
        Assert.Throws<ArgumentException>(() => BagPageFocus.Of(Sample(), new BagControls(), held: 6));
    }

    /// <summary>从每一格没被筛掉的格出发，一直按方向键，走得到每一样；走到的格一格都没被筛掉。</summary>
    private static void AssertAllReachable(Bag bag, BagControls controls)
    {
        var focus = BagPageFocus.Of(bag, controls, held: null);
        var shown = Enumerable.Range(0, bag.CarryCount + bag.SlotCount).Where(i => controls.Shows(bag, i)).ToList();
        var everything = shown.Select(i => ThingAt(bag, i)).ToHashSet();

        foreach (var start in shown)
        {
            var reached = new HashSet<int> { start };
            var queue = new Queue<int>([start]);
            while (queue.TryDequeue(out var at))
            {
                foreach (var direction in Enum.GetValues<FocusDirection>())
                {
                    if (focus.Next(at, direction) is not { } next)
                    {
                        continue;
                    }

                    Assert.True(controls.Shows(bag, next), $"从页上第 {at} 格往 {direction} 走到了被筛掉的第 {next} 格");
                    if (reached.Add(next))
                    {
                        queue.Enqueue(next);
                    }
                }
            }

            var missed = everything.Except(reached.Select(i => ThingAt(bag, i))).ToList();
            Assert.True(missed.Count == 0, $"从页上第 {start} 格出发走不到：{string.Join("、", missed)}");
        }
    }

    /// <summary>一格算哪一样：被一堆盖着的算那一堆（记它左上角那一格的号），空格算它自己。</summary>
    private static int ThingAt(Bag bag, int index) => BagPage.AnchorIndexOf(bag, index) ?? index;

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
