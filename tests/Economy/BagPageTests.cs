using Tinderhearth.Rules.Economy;
using Tinderhearth.Rules.Foundation.Content;
using Tinderhearth.Rules.Foundation.Text;
using Tinderhearth.Rules.Progression;
using Xunit;

namespace Tinderhearth.Rules.Tests.Economy;

/// <summary>背包页上每一格画什么、那一格那一行写什么、还剩几个空格、筛选按钮上写什么、丢之前问什么、丢不掉时说什么。</summary>
/// <remarks>
/// 措辞用一份假文本表，一眼认得出填了哪几个值；真文本表只核要的那几条一条不缺。颜色读的是测试物品表里那几档声明的
/// 显示色：芜菁的稀有度是 copper 3，寻常是 ash 6，变异是 spirit 3。
/// </remarks>
public class BagPageTests
{
    private const string Letter = "item_letter";

    private static readonly TextCatalog Fake = new(new Dictionary<string, string>
    {
        [BagPage.FreeSlotsKey] = "空格 {0}",
        [BagPage.InfoKey] = "{0}|{1}|{2}",
        [BagPage.InfoNoQualityKey] = "{0}|{1}",
        [BagPage.AnyCategoryKey] = "全部类别",
        [BagPage.AnyQualityKey] = "全部品质",
        [BagPage.DiscardAskKey] = "丢 {0}|{1}|{2}",
        [BagPage.DiscardAskNoQualityKey] = "丢 {0}|{1}",
        [BagPage.DiscardRejectedKey] = "{0} 丢不了",
        [$"item.{TestBags.Turnip}.name"] = "芜菁",
        [$"item.{TestBags.TurnipSeed}.name"] = "芜菁种子",
        [$"item.{Letter}.name"] = "信",
        ["item_category.herb.name"] = "草药",
        ["item_quality.mutated.name"] = "变异",
        ["item_quality.common.name"] = "寻常",
    });

    /// <summary>测试物品表多一样声明了不可丢弃的信（任务物品那一类）。</summary>
    private static ItemCatalog WithLetter()
    {
        var seed = TestBags.Items.Items[TestBags.TurnipSeed];
        var letter = seed with
        {
            Id = Letter,
            NameKey = $"item.{Letter}.name",
            DescriptionKey = $"item.{Letter}.description",
            Discardable = false,
        };
        return ItemCatalog.Build(
            [.. TestBags.Items.Items.Values, letter],
            TestBags.Items.Categories.Values,
            TestBags.Items.Rarities,
            TestBags.Items.Qualities,
            name => name.Split(' ')[0]);
    }

    [Fact]
    public void 文本表里有这一页要的每一条()
    {
        var path = Path.Combine(RepoFiles.Root(), "data", "text", "zh-CN.json");
        var table = new TextCatalog(ContentJson.Parse<Dictionary<string, string>>(File.ReadAllText(path), path));

        Assert.Empty(table.MissingKeys(BagPage.TextKeys));
    }

    [Fact]
    public void 空格只画格子_有东西的格画种类品质与件数()
    {
        var bag = TestBags.Empty(slots: 4);
        bag.TryAdd(TestBags.Turnip, TestBags.Mutated, 7);
        bag.TryAdd(TestBags.TurnipSeed, null, 1);

        var cells = BagPage.CellsOf(bag);

        Assert.Equal(4, cells.Count);
        Assert.Equal(new BagCell(TestBags.Turnip, TestBags.Mutated, 7, "copper 3", "spirit 3"), cells[0]);
        Assert.True(cells[0]!.ShowsCount);

        // 种子无品质：不画品质点；只有一件：不写件数。
        Assert.Equal(new BagCell(TestBags.TurnipSeed, null, 1, "copper 3", null), cells[1]);
        Assert.False(cells[1]!.ShowsCount);

        Assert.Null(cells[2]);
        Assert.Null(cells[3]);
    }

    [Fact]
    public void 变异与没变的是两堆_各画各的品质点()
    {
        var bag = TestBags.Empty(slots: 4);
        bag.TryAdd(TestBags.Turnip, TestBags.Common, 3);
        bag.TryAdd(TestBags.Turnip, TestBags.Mutated, 2);

        var cells = BagPage.CellsOf(bag);

        Assert.Equal("ash 6", cells[0]?.QualityColor);
        Assert.Equal("spirit 3", cells[1]?.QualityColor);
        Assert.Equal(cells[0]?.RarityColor, cells[1]?.RarityColor);
    }

    [Fact]
    public void 写着还剩几个空格()
    {
        var bag = TestBags.Empty(slots: 4);
        Assert.Equal("空格 4", BagPage.FreeSlotsLine(bag, Fake));

        bag.TryAdd(TestBags.TurnipSeed, null, 3);
        Assert.Equal("空格 3", BagPage.FreeSlotsLine(bag, Fake));
    }

    [Fact]
    public void 选中那一格那一行写名称品质与件数_无品质的不写品质()
    {
        var bag = TestBags.Empty(slots: 4);
        bag.TryAdd(TestBags.Turnip, TestBags.Mutated, 7);
        bag.TryAdd(TestBags.TurnipSeed, null, 1);
        var cells = BagPage.CellsOf(bag);

        Assert.Equal("芜菁|变异|7", BagPage.InfoLine(cells[0]!, bag.Items, Fake));
        Assert.Equal("芜菁种子|1", BagPage.InfoLine(cells[1]!, bag.Items, Fake));
    }

    [Fact]
    public void 每个排序键的按钮上都有字()
    {
        // 这一页按排序键逐个搭按钮、按键取字：加了一个键却没给字，开背包页那一下才抛。
        Assert.Equal(Enum.GetValues<BagSortKey>().Order(), BagPage.SortKeyKeys.Keys.Order());
    }

    [Fact]
    public void 筛选按钮上写当前那一档_不筛时写全部()
    {
        var items = TestBags.Items;
        Assert.Equal("全部类别", BagPage.CategoryChoiceLabel(BagFilter.All, items, Fake));
        Assert.Equal("全部品质", BagPage.QualityChoiceLabel(BagFilter.All, items, Fake));

        var filter = new BagFilter("herb", TestBags.Mutated);
        Assert.Equal("草药", BagPage.CategoryChoiceLabel(filter, items, Fake));
        Assert.Equal("变异", BagPage.QualityChoiceLabel(filter, items, Fake));
    }

    [Fact]
    public void 丢之前那一问写明哪一样什么品质几件_无品质的不写品质()
    {
        var bag = TestBags.Empty(slots: 4);
        bag.TryAdd(TestBags.Turnip, TestBags.Mutated, 7);
        bag.TryAdd(TestBags.TurnipSeed, null, 3);
        var cells = BagPage.CellsOf(bag);

        Assert.Equal("丢 芜菁|变异|7", BagPage.DiscardAsk(cells[0]!, bag.Items, Fake));
        Assert.Equal("丢 芜菁种子|3", BagPage.DiscardAsk(cells[1]!, bag.Items, Fake));
    }

    [Fact]
    public void 丢不掉的那一样说清是哪一种_丢得掉的不拦()
    {
        var items = WithLetter();
        var bag = new Bag(4, items);
        bag.TryAdd(Letter, null, 1);
        bag.TryAdd(TestBags.TurnipSeed, null, 1);
        var cells = BagPage.CellsOf(bag);

        Assert.Equal("信 丢不了", BagPage.DiscardRejection(cells[0]!, items, Fake));
        Assert.Null(BagPage.DiscardRejection(cells[1]!, items, Fake));
    }

    [Fact]
    public void 那一行的占位写错了_报出是哪一条()
    {
        var broken = new TextCatalog(new Dictionary<string, string> { [BagPage.FreeSlotsKey] = "空格 {1}" });

        var e = Assert.Throws<InvalidDataException>(() => BagPage.FreeSlotsLine(TestBags.Empty(), broken));

        Assert.Contains(BagPage.FreeSlotsKey, e.Message);
    }
}
