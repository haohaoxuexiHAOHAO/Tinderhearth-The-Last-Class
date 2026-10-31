using Tinderhearth.Rules.Economy;
using Xunit;

namespace Tinderhearth.Rules.Tests.Economy;

/// <summary>物品图标那一次核对，与画一堆东西时挑哪一张。</summary>
/// <remarks>
/// 这几样失效全都不报错：缺图标的那一格空着，玩家分不清「没东西」与「有东西没图」；复制出来忘了改标识，后一份顶掉
/// 前一份，表现只是另一样东西缺图标；变异那一格没写，变了的那一堆与没变的画得一模一样。
/// </remarks>
public class ItemIconCheckTests
{
    private static ItemIconEntry Seed() => new(TestBags.TurnipSeed, HasIcon: true, HasMutatedIcon: false, MutatedSameAsIcon: false);

    private static ItemIconEntry Turnip(bool mutatedIcon = true, bool sameAsIcon = false) =>
        new(TestBags.Turnip, HasIcon: true, HasMutatedIcon: mutatedIcon, MutatedSameAsIcon: sameAsIcon);

    [Fact]
    public void 每一样都配对了时没有问题()
    {
        Assert.Empty(ItemIconCheck.Problems(TestBags.Items, [Seed(), Turnip()]));
        Assert.Empty(ItemIconCheck.Problems(TestBags.Items, [Turnip(mutatedIcon: false, sameAsIcon: true), Seed()]));
    }

    [Fact]
    public void 在册物品缺图标_一种一行()
    {
        var problems = ItemIconCheck.Problems(TestBags.Items, []);

        Assert.Equal(2, problems.Count);
        Assert.Contains(problems, p => p.Contains(TestBags.Turnip) && p.Contains("没有图标"));
        Assert.Contains(problems, p => p.Contains(TestBags.TurnipSeed) && p.Contains("没有图标"));
    }

    [Fact]
    public void 有品质却没写变异那一张_报一行()
    {
        var problems = ItemIconCheck.Problems(TestBags.Items, [Seed(), Turnip(mutatedIcon: false, sameAsIcon: false)]);

        var problem = Assert.Single(problems);
        Assert.Contains(TestBags.Turnip, problem);
        Assert.Contains("Mutated Same As Icon", problem);
    }

    [Fact]
    public void 变异那一张配了又勾了同原图_报一行()
    {
        var problem = Assert.Single(
            ItemIconCheck.Problems(TestBags.Items, [Seed(), Turnip(mutatedIcon: true, sameAsIcon: true)]));

        Assert.Contains("只留一样", problem);
    }

    [Fact]
    public void 无品质的东西配了变异那一格_报一行()
    {
        var seed = Seed() with { MutatedSameAsIcon = true };

        var problem = Assert.Single(ItemIconCheck.Problems(TestBags.Items, [seed, Turnip()]));

        Assert.Contains(TestBags.TurnipSeed, problem);
        Assert.Contains("无品质", problem);
    }

    [Fact]
    public void 两份配给同一个物品_点名是哪两项()
    {
        // 照指南配好一份再复制、忘了改标识：后一份顶掉前一份，而芜菁就成了「在册但没有图标」。
        var problems = ItemIconCheck.Problems(TestBags.Items, [Seed(), Seed()]);

        Assert.Contains(problems, p => p.Contains("第 0 项与第 1 项") && p.Contains(TestBags.TurnipSeed));
        Assert.Contains(problems, p => p.Contains(TestBags.Turnip) && p.Contains("没有图标"));
    }

    [Fact]
    public void 图标那一张空着_不在册的物品_空着的一项_各报一行且一次全报()
    {
        var problems = ItemIconCheck.Problems(TestBags.Items,
        [
            Seed() with { HasIcon = false },
            new ItemIconEntry("item_turnp", HasIcon: true, HasMutatedIcon: false, MutatedSameAsIcon: false),
            null,
            Turnip(),
        ]);

        Assert.Equal(3, problems.Count);
        Assert.Contains(problems, p => p.Contains(TestBags.TurnipSeed) && p.Contains("（Icon）是空的"));
        Assert.Contains(problems, p => p.Contains("item_turnp") && p.Contains("不在册"));
        Assert.Contains(problems, p => p.Contains("第 2 项") && p.Contains("空的"));
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
