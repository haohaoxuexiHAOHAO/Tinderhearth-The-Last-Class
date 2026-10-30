namespace Tinderhearth.Rules.Economy;

/// <summary>当前算数的那一份世界。推进一天时，只有写档成功那一刻会换掉它。</summary>
/// <remarks>
/// 每日结算在它的副本上跑，自动存档写成之后才把副本换上来（设计仓 design/每日结算系统.md 的「整串加自动存档是一次事务」一节）。
/// 换的那一下只有结算驱动做得了，所以别处拿到的永远是一份完整的一天：要么睡前那一份，要么推进完的那一份。
/// </remarks>
public sealed class CurrentWorld
{
    public CurrentWorld(WorldState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        State = state;
    }

    /// <summary>现在算数的那一份。</summary>
    public WorldState State { get; private set; }

    /// <summary>换成推进完的那一份。只有结算驱动在提交那一刻调它。</summary>
    internal void Replace(WorldState next)
    {
        ArgumentNullException.ThrowIfNull(next);
        State = next;
    }
}
