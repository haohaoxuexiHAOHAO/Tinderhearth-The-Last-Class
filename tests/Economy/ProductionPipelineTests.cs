using Tinderhearth.Rules.Economy;
using Xunit;

namespace Tinderhearth.Rules.Tests.Economy;

/// <summary>生产那条管道：产出 → 归属 → 付酬 → 入库，现在走得到的是主角亲手那一支。</summary>
/// <remarks>
/// 归属与付酬那两格现在只有一个答案，钉住它们是为了派工与设备加进来时，主角亲手那一支照旧是这个答案。
/// </remarks>
public class ProductionPipelineTests
{
    private static readonly ProducedGoods TenTurnips = new(TestBags.Turnip, TestBags.Common, 10);

    [Fact]
    public void 主角亲手的产出归主角_不付酬_进主角的背包()
    {
        var bag = TestBags.Empty();

        var outcome = ProductionPipeline.Deliver(Producer.Protagonist, TenTurnips, bag);

        Assert.Equal(TenTurnips, outcome.Goods);
        Assert.Equal(GoodsOwner.Protagonist, outcome.Owner);
        Assert.False(outcome.Paid);
        Assert.True(outcome.Stored);
        Assert.Equal(new ItemStack(TestBags.Turnip, TestBags.Common, 10, 1), bag.Slots[0]);
    }

    [Fact]
    public void 放不下就整份拒收_背包一件不变()
    {
        // 只有一格、已经是一满堆种子：芜菁合不进去，也没有空格。
        var bag = Bag.Restore([new ItemStack(TestBags.TurnipSeed, null, 20, 1)], TestBags.Items);
        var before = bag.Slots.ToList();

        var outcome = ProductionPipeline.Deliver(Producer.Protagonist, TenTurnips, bag);

        Assert.False(outcome.Stored);
        Assert.Equal(TenTurnips, outcome.Goods);
        Assert.Equal(before, bag.Slots);
    }

    [Fact]
    public void 一份只放得下一半也整份拒收()
    {
        // 一格、里面已经有 15 个寻常芜菁：再来 10 个只合得进 5 个，又没有空格放剩下那 5 个。
        var bag = Bag.Restore([new ItemStack(TestBags.Turnip, TestBags.Common, 15, 1)], TestBags.Items);

        var outcome = ProductionPipeline.Deliver(Producer.Protagonist, TenTurnips, bag);

        Assert.False(outcome.Stored);
        Assert.Equal(new ItemStack(TestBags.Turnip, TestBags.Common, 15, 1), bag.Slots[0]);
    }
}
