namespace Tinderhearth.Rules.Economy;

/// <summary>结算那串步序里的一步：在这一天上改世界副本、记结果。</summary>
public delegate void SettlementStep(SettlementDay day);

/// <summary>每日结算的驱动：推进一天的唯一入口，按步名把正典那串逐步调一次，整串是一次事务。</summary>
/// <remarks>
/// 它只驱动、不持有任何一步的规则：每一步由构造时按名交进来，名字与顺序以 <see cref="SettlementSteps.Order"/> 为准。
/// 少一步、多一个表里没有的名字、同一步给两次，构造当场被拒，所以一份驱动要么跑全串、要么建不出来。
///
/// 整串在世界的副本上跑，「自动存档」那一步正常走完那一刻就是提交点：副本换成当前那一份。在那之前任何一步出错，
/// 当前那一份一格不动，结果说清这一天没有推进、停在哪一步、为什么。结构在设计仓 design/每日结算系统.md。
/// </remarks>
public sealed class DailySettlement
{
    private readonly IReadOnlyList<(string Name, SettlementStep Run)> _ordered;
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

        _ordered = [.. SettlementSteps.Order.Select(name => (name, given.First(s => s.Name == name).Run))];
        _askEvents = askEvents;
    }

    /// <summary>推进一天：在副本上按步序把每一步各调一次，存档写成那一刻提交，整串跑完之后问一次事件。</summary>
    /// <param name="world">当前那一份世界。只有提交那一刻会换掉它。</param>
    /// <param name="source">这一天是怎么结束的。</param>
    /// <remarks>
    /// 提交之前出错一律接住、变成「这一天没有推进」：那时当前那一份还没动过。提交之后出错照常抛出去：世界已经换过、
    /// 存档已经写上，那是一个缺陷，不是一次回滚，接住它只会让人以为这一天没过。
    /// </remarks>
    public SettlementOutcome EndDay(CurrentWorld world, DayEndSource source)
    {
        ArgumentNullException.ThrowIfNull(world);

        var day = new SettlementDay(world, source);
        foreach (var (name, run) in _ordered)
        {
            if (day.Committed)
            {
                run(day);
                continue;
            }

            try
            {
                run(day);
            }
            catch (Exception e)
            {
                return SettlementOutcome.NotAdvanced(day.SettledDate, name, e.Message);
            }

            if (name == SettlementSteps.AutoSave)
            {
                day.Commit();
            }
        }

        _askEvents(day);
        return SettlementOutcome.Done(day.SettledDate, day.Summary);
    }
}
