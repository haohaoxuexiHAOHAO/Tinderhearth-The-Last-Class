namespace Tinderhearth.Rules.UI;

/// <summary>输入门面上的那道闸：谁关上的谁打开；有一方还关着，玩法就读不到任何动作。</summary>
/// <remarks>
/// 关闸用在世界没有暂停、却不许玩家动的那几段，比如睡下到醒来那一段转场（设计仓 design/每日结算系统.md 的
/// 「结算期间没有输入入口」一节）。弹出界面不用它：那时世界暂停，玩法节点本来就不在跑。
///
/// 按关闸的一方记，不记成一个开关：两方各关各的时候，先办完的那一方一打开，另一方还没办完的那一段就漏进了输入。
/// 次序错了当场抛，因为那是接线的缺陷：悄悄认下来的话，闸可能永远关着，而表现只是「按什么都没反应」。
/// </remarks>
public sealed class InputGate
{
    private readonly HashSet<string> _holders = new(StringComparer.Ordinal);

    /// <summary>没有谁关着它。</summary>
    public bool IsOpen => _holders.Count == 0;

    /// <summary>现在是哪几方关着它，排缺陷时打出来看。</summary>
    public IReadOnlyCollection<string> Holders => _holders;

    /// <summary>某一方把闸关上。</summary>
    /// <param name="holder">关闸的是谁，打开时要报同一个名字。</param>
    public void Close(string holder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(holder);
        if (!_holders.Add(holder))
        {
            throw new InvalidOperationException($"「{holder}」已经关着输入，又关了一次：它上一次关的那一段还没交回");
        }
    }

    /// <summary>某一方把自己关上的闸打开。别人还关着时，门面照样关着。</summary>
    public void Reopen(string holder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(holder);
        if (!_holders.Remove(holder))
        {
            throw new InvalidOperationException($"「{holder}」没关过输入，却要打开它");
        }
    }
}
