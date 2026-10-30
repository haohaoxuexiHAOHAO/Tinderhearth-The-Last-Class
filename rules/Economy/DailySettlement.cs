namespace Tinderhearth.Rules.Economy;

/// <summary>结算那串步序里的一步：在这一天上改世界、记结果。</summary>
public delegate void SettlementStep(SettlementDay day);

/// <summary>每日结算的驱动：推进一天的唯一入口，按步名把正典那串逐步调一次。</summary>
/// <remarks>
/// 它只驱动、不持有任何一步的规则：每一步由构造时按名交进来，名字与顺序以 <see cref="SettlementSteps.Order"/> 为准。
/// 少一步、多一个表里没有的名字、同一步给两次，构造当场被拒，所以一份驱动要么跑全串、要么建不出来。
/// 结构在设计仓 design/每日结算系统.md。
/// </remarks>
public sealed class DailySettlement
{
    private readonly IReadOnlyList<SettlementStep> _ordered;
    private readonly SettlementStep _askEvents;

    /// <param name="steps">每一步的名字与做法，正典那串里每个名字恰好一次，先后不限。</param>
    /// <param name="askEvents">整串跑完之后询问一次事件。它不占步序里的任何一步。</param>
    public DailySettlement(IEnumerable<(string Name, SettlementStep Run)> steps, SettlementStep askEvents)
    {
        ArgumentNullException.ThrowIfNull(steps);
        ArgumentNullException.ThrowIfNull(askEvents);

        var given = steps.ToList();
        var problems = new List<string>();

        problems.AddRange(given.GroupBy(s => s.Name).Where(g => g.Count() > 1).Select(g => $"「{g.Key}」给了 {g.Count()} 次"));
        problems.AddRange(given.Select(s => s.Name).Distinct().Where(n => !SettlementSteps.Order.Contains(n)).Select(n => $"「{n}」不在步序里"));
        problems.AddRange(SettlementSteps.Order.Where(n => given.All(s => s.Name != n)).Select(n => $"缺「{n}」"));
        problems.AddRange(given.Where(s => s.Run is null).Select(s => $"「{s.Name}」没有做法"));

        if (problems.Count > 0)
        {
            throw new ArgumentException(
                "每日结算的步序对不上（设计仓 canon/gameplay/时间与经营.md 的「结算顺序（固定，不得改动）」一节）：" + string.Join("；", problems),
                nameof(steps));
        }

        _ordered = [.. SettlementSteps.Order.Select(name => given.First(s => s.Name == name).Run)];
        _askEvents = askEvents;
    }

    /// <summary>推进一天：按步序把每一步各调一次，整串跑完之后问一次事件。</summary>
    /// <returns>这一天各步记下的结果。</returns>
    public SettlementDay Run(WorldState world, DayEndSource source)
    {
        var day = new SettlementDay(world, source);
        foreach (var step in _ordered)
        {
            step(day);
        }

        _askEvents(day);
        return day;
    }
}
