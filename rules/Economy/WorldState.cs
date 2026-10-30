namespace Tinderhearth.Rules.Economy;

/// <summary>每日结算推进的那一份世界：今天是哪一天，加上那片地。</summary>
/// <remarks>
/// 结算在它的一份副本上跑、写档成功才换上去（设计仓 design/每日结算系统.md 的「整串加自动存档是一次事务」一节），
/// 所以它必须整份复制得出来：<see cref="Clone"/> 出来的副本与原件不共用任何会变的东西。往这里加一样东西时，
/// <see cref="Clone"/> 要跟着复制它，复制那几条测试会提醒。
/// </remarks>
public sealed class WorldState
{
    public WorldState(GameDate date, PlotGrid plots)
    {
        ArgumentNullException.ThrowIfNull(plots);

        Date = date;
        Plots = plots;
    }

    /// <summary>今天是哪一天。</summary>
    public GameDate Date { get; private set; }

    /// <summary>那片地。</summary>
    public PlotGrid Plots { get; }

    /// <summary>开新档：日期是开局那一天，地是场景里那片地的起始状态。</summary>
    public static WorldState NewGame(PlotGrid plots) => new(Calendar.StartDate, plots);

    /// <summary>日期往前推一天。只有每日结算的「日期推进」那一步调它。</summary>
    public void AdvanceDate() => Date = Date.Next();

    /// <summary>整份复制。</summary>
    public WorldState Clone() => new(Date, Plots.Clone());
}
