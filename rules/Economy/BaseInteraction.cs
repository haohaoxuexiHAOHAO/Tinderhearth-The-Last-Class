namespace Tinderhearth.Rules.Economy;

/// <summary>经营侧按一下交互键，作用在什么上。</summary>
public enum BaseInteractTarget
{
    /// <summary>当前操作格那一格：按它的状态派发清理、锄、播、浇、收或清枯株。</summary>
    CurrentCell = 1,

    /// <summary>床：睡，推进一天。</summary>
    Bed,
}

/// <summary>经营侧按一下交互键时挑哪一个。挑法写死在这里，不看引擎给的先后。</summary>
/// <remarks>
/// 站在床的范围里就是睡，当前操作格那一格这一下不动。床的范围是作者画出来的一块，站进去就是冲着床来的；
/// 当前操作格走到哪儿都有，让它优先的话，床边只要有一格够得着的地，这张床就睡不了。所以床要摆得离开那片地。
/// 几样同时够得着时由规则层挑一个、判据写明，不留给引擎的返回顺序：那个顺序不保证稳定，表现是同一个位置
/// 有时用这个、有时用那个，而那不报错。挑法是规则，所以放在这里让单元测试盯着。
/// </remarks>
public static class BaseInteraction
{
    /// <summary>挑这一下作用在什么上。</summary>
    /// <param name="bedInReach">角色这一刻在不在某一张床的范围里。</param>
    public static BaseInteractTarget Pick(bool bedInReach) =>
        bedInReach ? BaseInteractTarget.Bed : BaseInteractTarget.CurrentCell;
}
