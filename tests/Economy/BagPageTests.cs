using Tinderhearth.Rules.Economy;
using Tinderhearth.Rules.Foundation.Content;
using Tinderhearth.Rules.Foundation.Text;
using Xunit;

namespace Tinderhearth.Rules.Tests.Economy;

/// <summary>背包页上每一格画什么、选中那一格那一行写什么、还剩几个空格。</summary>
/// <remarks>
/// 措辞用一份假文本表，一眼认得出填了哪几个值；真文本表只核要的那几条一条不缺。颜色读的是测试物品表里那几档声明的
/// 显示色：芜菁的稀有度是 copper 3，寻常是 ash 6，变异是 spirit 3。
/// </remarks>
public class BagPageTests
{
    private static readonly TextCatalog Fake = new(new Dictionary<string, string>
    {
        [BagPage.FreeSlotsKey] = "空格 {0}",
        [BagPage.InfoKey] = "{0}|{1}|{2}",
        [BagPage.InfoNoQualityKey] = "{0}|{1}",
        [$"item.{TestBags.Turnip}.name"] = "芜菁",
        [$"item.{TestBags.TurnipSeed}.name"] = "芜菁种子",
        ["item_quality.mutated.name"] = "变异",
        ["item_quality.common.name"] = "寻常",
    });

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
    public void 那一行的占位写错了_报出是哪一条()
    {
        var broken = new TextCatalog(new Dictionary<string, string> { [BagPage.FreeSlotsKey] = "空格 {1}" });

        var e = Assert.Throws<InvalidDataException>(() => BagPage.FreeSlotsLine(TestBags.Empty(), broken));

        Assert.Contains(BagPage.FreeSlotsKey, e.Message);
    }
}
