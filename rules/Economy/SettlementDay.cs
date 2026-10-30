namespace Tinderhearth.Rules.Economy;

/// <summary>一天怎么结束的：主动睡，或者到点强制结束。</summary>
/// <remarks>
/// 两个来源之后走同一个入口、同一串步序。差别只在触发那一下的表现与随之算出的熬夜档位，而熬夜档位还没有，
/// 归设计仓台账 GP-156。依据在设计仓 design/每日结算系统.md 的「一个入口，两个来源」一节。
/// </remarks>
public enum DayEndSource
{
    /// <summary>玩家在床前睡下（调试键 N 也算这一种）。</summary>
    Sleep,

    /// <summary>时间走到那个硬边界，替玩家结束这一天。</summary>
    ForcedEnd,
}

/// <summary>这一夜里一格地上的变化：哪一格、哪种作物、变成了什么。</summary>
/// <param name="Cell">哪一格。</param>
/// <param name="CropId">那一格里那种作物的标识。</param>
/// <param name="Kind">枯死还是变异。</param>
public readonly record struct CropChange(GridCell Cell, string CropId, CropChangeKind Kind);

/// <summary>一格地这一夜可能出的两种事。</summary>
public enum CropChangeKind
{
    /// <summary>换季那天，新季节不在这种作物的季节列表里。</summary>
    Withered,

    /// <summary>长成那一下掷中了变异。</summary>
    Mutated,
}

/// <summary>结算的这一天：被推进的那份世界副本，以及各步记下的结果。</summary>
/// <remarks>
/// 每一步只读写这里。各步记下的结果是摘要的来源：摘要从这里算，不另存一份账
/// （设计仓 design/每日结算系统.md 的「摘要：简版是弹窗，完整明细是一个读口」一节）。
/// 只有结算驱动造得出它，也只有驱动在自动存档写成之后提交它。
/// </remarks>
public sealed class SettlementDay
{
    private readonly List<CropChange> _cropChanges = [];
    private readonly CurrentWorld _current;

    internal SettlementDay(CurrentWorld current, DayEndSource source)
    {
        _current = current;
        World = current.State.Clone();
        Source = source;
        SettledDate = World.Date;
    }

    /// <summary>被推进的那份世界：当前那一份的副本，提交之前改它不碰当前那一份。</summary>
    public WorldState World { get; }

    /// <summary>这一天是怎么结束的。</summary>
    public DayEndSource Source { get; }

    /// <summary>结算的是哪一天：推进之前的那一天。</summary>
    public GameDate SettledDate { get; }

    /// <summary>今天换季了吗。「季节更替判定」那一步写它，之后的步读它（考试只在换季那天跑）。</summary>
    public bool SeasonChanged { get; internal set; }

    /// <summary>这一夜各格地上的变化，按记下的先后。</summary>
    public IReadOnlyList<CropChange> CropChanges => _cropChanges;

    /// <summary>这一天的摘要；「生成结算摘要」那一步之前为 <c>null</c>。</summary>
    public SettlementSummary? Summary { get; private set; }

    /// <summary>提交了没有：自动存档写成、副本已经换成当前那一份。</summary>
    public bool Committed { get; private set; }

    internal void Record(CropChange change) => _cropChanges.Add(change);

    /// <summary>从这一天记下的变化算出摘要。</summary>
    internal void ProduceSummary() => Summary = new SettlementSummary(SettledDate, [.. _cropChanges]);

    /// <summary>提交：把副本换成当前那一份。只在自动存档那一步正常走完之后调一次。</summary>
    internal void Commit()
    {
        if (Committed)
        {
            throw new InvalidOperationException("这一天已经提交过一次了");
        }

        _current.Replace(World);
        Committed = true;
    }
}
