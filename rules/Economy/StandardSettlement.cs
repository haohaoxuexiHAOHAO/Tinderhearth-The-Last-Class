namespace Tinderhearth.Rules.Economy;

/// <summary>游戏里用的那一份每日结算：正典那串步序，每一步接到它的持有者上。</summary>
/// <remarks>
/// 还没有持有者的步是空步，注释写明归哪一条；持有者落地时只填自己那一步，不动驱动本身。
/// 每一步的规则都不在这里，各归各的系统；这里只把它们按名接起来。
/// </remarks>
public static class StandardSettlement
{
    /// <summary>建一份完整的每日结算。</summary>
    /// <param name="crops">按标识找作物定义。地里那种作物找不到时，推进那一格当场报错。</param>
    /// <param name="fallowRevertDays">已锄的空格荒置几天退回可耕，至少 1。这个天数还没定、要实机试，本层不带默认值。</param>
    /// <param name="rollMutation">掷一次变异，中了返回真。只在一株长成的那一下调。</param>
    public static DailySettlement Create(
        IReadOnlyDictionary<string, CropDefinition> crops, int fallowRevertDays, Func<bool> rollMutation) =>
        new(Steps(crops, fallowRevertDays, rollMutation), AskEvents);

    /// <summary>那串步序的每一步，名字与做法。测试拿它替换其中几步。</summary>
    public static IReadOnlyList<(string Name, SettlementStep Run)> Steps(
        IReadOnlyDictionary<string, CropDefinition> crops, int fallowRevertDays, Func<bool> rollMutation)
    {
        ArgumentNullException.ThrowIfNull(crops);
        ArgumentNullException.ThrowIfNull(rollMutation);
        if (fallowRevertDays < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(fallowRevertDays), $"荒置退化天数必须至少 1，实际 {fallowRevertDays}（缺配置也会得到 0）");
        }

        return
        [
            (SettlementSteps.DateAdvance, static day => day.World.AdvanceDate()),
            (SettlementSteps.SeasonChange, day => SeasonChange(day, crops)),
            (SettlementSteps.CropGrowth, day => CropGrowth(day, crops, fallowRevertDays, rollMutation)),

            // 派工产出入库与付酬走生产那条管道，加工也是生产的一条渠道，都还没有，归设计仓台账 GP-28。
            (SettlementSteps.AssignmentOutput, Nothing),
            (SettlementSteps.ProcessingProgress, Nothing),

            // 施工还没有，归设计仓台账 GP-44。
            (SettlementSteps.ConstructionProgress, Nothing),

            // 阅读还没有，归设计仓台账 GP-21。
            (SettlementSteps.ReadingProgress, Nothing),

            // 按天状态要状态载体先有按天那一档，归设计仓台账 GP-18；知识吸收效率归设计仓台账 GP-21。
            (SettlementSteps.StudentRecovery, Nothing),

            // 考试还没有，归设计仓台账 GP-21。
            (SettlementSteps.ExamSettlement, Nothing),

            // 计息归设计仓台账 GP-43。
            (SettlementSteps.DebtInterest, Nothing),

            // 委托板那两步归设计仓台账 GP-41。
            (SettlementSteps.QuestExpiry, Nothing),
            (SettlementSteps.QuestBoardRefill, Nothing),

            // 精力还没有代码；上限是成长那张派生表里的一行，归设计仓台账 GP-30。
            (SettlementSteps.EnergyCapRecompute, Nothing),
            (SettlementSteps.EnergyRestore, Nothing),

            // 摘要、写档与醒来交出摘要归设计仓台账 GP-152。
            (SettlementSteps.Summary, Nothing),
            (SettlementSteps.AutoSave, Nothing),
            (SettlementSteps.Wake, Nothing),
        ];
    }

    /// <summary>还没有持有者的那一步。</summary>
    private static void Nothing(SettlementDay day)
    {
    }

    /// <summary>整串之后那一次事件询问。事件还没有，归设计仓台账 GP-52。</summary>
    private static void AskEvents(SettlementDay day)
    {
    }

    /// <summary>换季那天，新季节不在作物季节列表里的一律枯死；不换季的那天一格都不动。</summary>
    /// <remarks>它排在作物生长之前，否则换季那天的作物会先白长一天再枯死。</remarks>
    private static void SeasonChange(SettlementDay day, IReadOnlyDictionary<string, CropDefinition> crops)
    {
        day.SeasonChanged = day.World.Date.Season != day.SettledDate.Season;
        if (!day.SeasonChanged)
        {
            return;
        }

        foreach (var (cell, plot) in day.World.Plots.Cells)
        {
            var before = plot.State;
            plot.ApplySeasonChange(CropOf(plot, crops), day.World.Date.Season);
            if (plot.State == PlotState.Withered && before != PlotState.Withered)
            {
                day.Record(new CropChange(cell, plot.CropId!, CropChangeKind.Withered));
            }
        }
    }

    /// <summary>每一格过一夜：生长（长成那一下掷一次变异）、荒置计时、清掉当天的浇水标记。</summary>
    private static void CropGrowth(
        SettlementDay day, IReadOnlyDictionary<string, CropDefinition> crops, int fallowRevertDays, Func<bool> rollMutation)
    {
        foreach (var (cell, plot) in day.World.Plots.Cells)
        {
            var wasMutated = plot.Mutated;
            plot.AdvanceDay(CropOf(plot, crops), fallowRevertDays, rollMutation);
            if (plot.Mutated && !wasMutated)
            {
                day.Record(new CropChange(cell, plot.CropId!, CropChangeKind.Mutated));
            }
        }
    }

    private static CropDefinition? CropOf(Plot plot, IReadOnlyDictionary<string, CropDefinition> crops) =>
        plot.CropId is string id && crops.TryGetValue(id, out var crop) ? crop : null;
}
