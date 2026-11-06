using Tinderhearth.Rules.Economy;
using Tinderhearth.Rules.Progression;
using Xunit;

namespace Tinderhearth.Rules.Tests.Economy;

/// <summary>物品图标那一次核对，与画一堆东西时挑哪一张、图标应当多大。</summary>
/// <remarks>
/// 这几样失效全都不报错：缺图标的那一格空着，玩家分不清「没东西」与「有东西没图」；复制出来忘了改标识，后一份顶掉
/// 前一份，表现只是另一样东西缺图标；变异那一格没写，变了的那一堆与没变的画得一模一样；图标画得与占地不一样大，
/// 背包页里那一块画不满或者压出去。
/// </remarks>
public class ItemIconCheckTests
{
    /// <summary>一格 16 像素：设计仓 canon/gameplay/玩法定位.md 的「像素基准」。引擎层传的是界面那一侧的基础单位。</summary>
    private const int CellPixels = 16;

    private const string Gem = "item_gem";

    private static readonly IconSize OneCell = new(Width: 16, Height: 16);

    private static readonly IconSize TwoByTwo = new(Width: 32, Height: 32);

    /// <summary>测试那份小表加一颗占 2×2、有品质的宝石。</summary>
    private static readonly ItemCatalog WithGem = ItemCatalog.Build(
        [
            .. TestBags.Items.Items.Values,
            TestBags.Items.Items[TestBags.Turnip] with { Id = Gem, Footprint = new() { Width = 2, Height = 2 } },
        ],
        TestBags.Items.Categories.Values,
        TestBags.Items.Rarities,
        TestBags.Items.Qualities,
        name => name.Split(' ')[0]);

    private static ItemIconEntry Seed() => new(TestBags.TurnipSeed, Icon: OneCell, MutatedIcon: null, MutatedSameAsIcon: false);

    private static ItemIconEntry Turnip(bool mutatedIcon = true, bool sameAsIcon = false) =>
        new(TestBags.Turnip, Icon: OneCell, MutatedIcon: mutatedIcon ? OneCell : null, MutatedSameAsIcon: sameAsIcon);

    private static ItemIconEntry GemIcon(IconSize icon) => new(Gem, Icon: icon, MutatedIcon: null, MutatedSameAsIcon: true);

    private static IReadOnlyList<string> Problems(ItemCatalog items, IReadOnlyList<ItemIconEntry?> icons) =>
        ItemIconCheck.Problems(items, icons, CellPixels);

    [Fact]
    public void 每一样都配对了时没有问题()
    {
        Assert.Empty(Problems(TestBags.Items, [Seed(), Turnip()]));
        Assert.Empty(Problems(TestBags.Items, [Turnip(mutatedIcon: false, sameAsIcon: true), Seed()]));
    }

    [Fact]
    public void 在册物品缺图标_一种一行()
    {
        var problems = Problems(TestBags.Items, []);

        Assert.Equal(2, problems.Count);
        Assert.Contains(problems, p => p.Contains(TestBags.Turnip) && p.Contains("没有图标"));
        Assert.Contains(problems, p => p.Contains(TestBags.TurnipSeed) && p.Contains("没有图标"));
    }

    [Fact]
    public void 有品质却没写变异那一张_报一行()
    {
        var problems = Problems(TestBags.Items, [Seed(), Turnip(mutatedIcon: false, sameAsIcon: false)]);

        var problem = Assert.Single(problems);
        Assert.Contains(TestBags.Turnip, problem);
        Assert.Contains("Mutated Same As Icon", problem);
    }

    [Fact]
    public void 变异那一张配了又勾了同原图_报一行()
    {
        var problem = Assert.Single(Problems(TestBags.Items, [Seed(), Turnip(mutatedIcon: true, sameAsIcon: true)]));

        Assert.Contains("只留一样", problem);
    }

    [Fact]
    public void 无品质的东西配了变异那一格_报一行()
    {
        var seed = Seed() with { MutatedSameAsIcon = true };

        var problem = Assert.Single(Problems(TestBags.Items, [seed, Turnip()]));

        Assert.Contains(TestBags.TurnipSeed, problem);
        Assert.Contains("无品质", problem);
    }

    [Fact]
    public void 两份配给同一个物品_点名是哪两项()
    {
        // 照指南配好一份再复制、忘了改标识：后一份顶掉前一份，而芜菁就成了「在册但没有图标」。
        var problems = Problems(TestBags.Items, [Seed(), Seed()]);

        Assert.Contains(problems, p => p.Contains("第 0 项与第 1 项") && p.Contains(TestBags.TurnipSeed));
        Assert.Contains(problems, p => p.Contains(TestBags.Turnip) && p.Contains("没有图标"));
    }

    [Fact]
    public void 图标那一张空着_不在册的物品_空着的一项_各报一行且一次全报()
    {
        var problems = Problems(TestBags.Items,
        [
            Seed() with { Icon = null },
            new ItemIconEntry("item_turnp", Icon: OneCell, MutatedIcon: null, MutatedSameAsIcon: false),
            null,
            Turnip(),
        ]);

        Assert.Equal(3, problems.Count);
        Assert.Contains(problems, p => p.Contains(TestBags.TurnipSeed) && p.Contains("（Icon）是空的"));
        Assert.Contains(problems, p => p.Contains("item_turnp") && p.Contains("不在册"));
        Assert.Contains(problems, p => p.Contains("第 2 项") && p.Contains("空的"));
    }

    [Theory]
    [InlineData(1, 1, 16, 16)]
    [InlineData(2, 2, 32, 32)]
    [InlineData(1, 3, 16, 48)]
    [InlineData(3, 2, 48, 32)]
    public void 图标应当多大_占地的宽高各乘一格的像素数(int width, int height, int expectedWidth, int expectedHeight)
    {
        var footprint = new ItemFootprint { Width = width, Height = height };

        Assert.Equal(new IconSize(Width: expectedWidth, Height: expectedHeight), ItemIconCheck.IconSizeFor(footprint, CellPixels));
    }

    [Fact]
    public void 大东西的图标正好是它那一块那么大时没有问题()
    {
        Assert.Empty(Problems(WithGem, [Seed(), Turnip(), GemIcon(TwoByTwo)]));
    }

    [Fact]
    public void 图标尺寸与占地对不上_一行写清应当多大与实际多大()
    {
        var problem = Assert.Single(Problems(TestBags.Items, [Seed() with { Icon = TwoByTwo }, Turnip()]));

        Assert.Contains(TestBags.TurnipSeed, problem);
        Assert.Contains("占地 1×1", problem);
        Assert.Contains("应当正好 16×16", problem);
        Assert.Contains("Icon 是 32×32", problem);
    }

    [Fact]
    public void 大东西画成一格那么大_报的是它那一块的大小()
    {
        var problem = Assert.Single(Problems(WithGem, [Seed(), Turnip(), GemIcon(OneCell)]));

        Assert.Contains(Gem, problem);
        Assert.Contains("占地 2×2", problem);
        Assert.Contains("应当正好 32×32", problem);
        Assert.Contains("Icon 是 16×16", problem);
    }

    [Fact]
    public void 变异那一张的尺寸也核_两张都不对时同一件只报一行()
    {
        var wrongMutated = Turnip() with { MutatedIcon = new IconSize(Width: 32, Height: 16) };
        var problem = Assert.Single(Problems(TestBags.Items, [Seed(), wrongMutated]));
        Assert.Contains("Mutated Icon 是 32×16", problem);
        Assert.DoesNotContain("Icon 是 16×16", problem);

        var bothWrong = wrongMutated with { Icon = TwoByTwo };
        var both = Assert.Single(Problems(TestBags.Items, [Seed(), bothWrong]));
        Assert.Contains("Icon 是 32×32", both);
        Assert.Contains("Mutated Icon 是 32×16", both);
    }

    [Fact]
    public void 无品质的东西不核变异那一张的尺寸_只报用不上()
    {
        var seed = Seed() with { MutatedIcon = TwoByTwo };

        var problem = Assert.Single(Problems(TestBags.Items, [seed, Turnip()]));

        Assert.Contains("用不上", problem);
    }

    [Fact]
    public void 只有变异那一档画变异那一张()
    {
        Assert.True(ItemIconCheck.ShowsMutated(TestBags.Mutated));
        Assert.False(ItemIconCheck.ShowsMutated(TestBags.Common));
        Assert.False(ItemIconCheck.ShowsMutated(null));

        // 收获给的那一档与这里换图的那一档是同一个标识。
        Assert.True(ItemIconCheck.ShowsMutated(Harvesting.MutatedQualityId));
    }
}
