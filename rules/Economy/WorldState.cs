namespace Tinderhearth.Rules.Economy;

/// <summary>每日结算推进的那一份世界：今天是哪一天、那片地，加上主角的背包。</summary>
/// <remarks>
/// 结算在它的一份副本上跑、写档成功才换上去（设计仓 design/每日结算系统.md 的「整串加自动存档是一次事务」一节），
/// 所以它必须整份复制得出来：<see cref="Clone"/> 出来的副本与原件不共用任何会变的东西。往这里加一样东西时，
/// <see cref="Clone"/> 要跟着复制它，复制那几条测试会提醒。
/// </remarks>
public sealed class WorldState
{
    public WorldState(GameDate date, PlotGrid plots, Bag bag)
    {
        ArgumentNullException.ThrowIfNull(plots);
        ArgumentNullException.ThrowIfNull(bag);

        Date = date;
        Plots = plots;
        Bag = bag;
    }

    /// <summary>今天是哪一天。</summary>
    public GameDate Date { get; private set; }

    /// <summary>那片地。</summary>
    public PlotGrid Plots { get; }

    /// <summary>主角的背包。</summary>
    public Bag Bag { get; }

    /// <summary>开新档：日期是开局那一天，地是场景里那片地的起始状态，背包是空的。</summary>
    /// <remarks>
    /// 开局时背包里什么都没有：起始资金只够买第一批种子（设计仓 canon/gameplay/玩法定位.md 的「开局」一节）。
    /// 交进来的背包装着东西就抛。
    /// </remarks>
    public static WorldState NewGame(PlotGrid plots, Bag bag)
    {
        ArgumentNullException.ThrowIfNull(bag);
        return bag.EmptySlotCount == bag.SlotCount
            ? new(Calendar.StartDate, plots, bag)
            : throw new ArgumentException("开新档时背包是空的，交进来的那一份装着东西", nameof(bag));
    }

    /// <summary>日期往前推一天。只有每日结算的「日期推进」那一步调它。</summary>
    public void AdvanceDate() => Date = Date.Next();

    /// <summary>整份复制。</summary>
    public WorldState Clone() => new(Date, Plots.Clone(), Bag.Clone());
}
