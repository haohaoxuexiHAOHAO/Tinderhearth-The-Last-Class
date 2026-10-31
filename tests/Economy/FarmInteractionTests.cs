using Tinderhearth.Rules.Economy;
using Xunit;

namespace Tinderhearth.Rules.Tests.Economy;

/// <summary>经营侧按一下交互键：站在床的范围里是睡，不在范围里作用在当前操作格上。</summary>
public class FarmInteractionTests
{
    [Fact]
    public void 站在床的范围里按交互键是睡_当前操作格这一下不动()
    {
        Assert.Equal(FarmInteractTarget.Bed, FarmInteraction.Pick(bedInReach: true));
    }

    [Fact]
    public void 不在床的范围里照旧作用在当前操作格上()
    {
        Assert.Equal(FarmInteractTarget.CurrentCell, FarmInteraction.Pick(bedInReach: false));
    }
}
