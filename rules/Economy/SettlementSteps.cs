namespace Tinderhearth.Rules.Economy;

/// <summary>每日结算那串步序的步名，顺序与设计仓正典一致。</summary>
/// <remarks>
/// 步序的家在设计仓 canon/gameplay/时间与经营.md 的「结算顺序（固定，不得改动）」一节，这里一步不增、一步不减。
/// 每个名字取正典那一条去掉加粗之后的开头，设计仓 tools/check_docs.py 逐项比对项数、顺序与开头，
/// 任何一边改了而另一边没跟上就判失败。引用某一步一律按名，不按它排第几：插一步会让全部序号静默平移。
/// </remarks>
public static class SettlementSteps
{
    public const string DateAdvance = "日期推进";
    public const string SeasonChange = "季节更替判定";
    public const string CropGrowth = "作物生长与枯死结算";
    public const string AssignmentOutput = "学生派工产出入库";
    public const string ProcessingProgress = "加工进度推进";
    public const string ConstructionProgress = "建造进度推进";
    public const string ReadingProgress = "书籍阅读进度推进";

    /// <summary>这一步还包括全部按天状态推进与知识吸收效率回升；名字只取正典那一条的开头。</summary>
    public const string StudentRecovery = "学生康复";

    public const string ExamSettlement = "考试结算";
    public const string DebtInterest = "债务计息";
    public const string QuestExpiry = "委托超期作废";
    public const string QuestBoardRefill = "委托板补满";
    public const string EnergyCapRecompute = "精力上限计算";
    public const string EnergyRestore = "全体角色精力回复至上限";
    public const string Summary = "生成结算摘要";
    public const string AutoSave = "自动存档";
    public const string Wake = "次日 6:30 醒来";

    /// <summary>步数。落地前后完全一致，有测试钉住它等于 <see cref="Order"/> 的项数。</summary>
    /// <remarks>
    /// 它与 <see cref="Order"/> 是同一件事写两次，这是刻意的：改那张表时必须同时改这个数，误增一步或误删一步当场就有一条测试失败。
    /// </remarks>
    public const int Count = 17;

    /// <summary>正典那串的顺序。</summary>
    public static readonly IReadOnlyList<string> Order =
    [
        DateAdvance,
        SeasonChange,
        CropGrowth,
        AssignmentOutput,
        ProcessingProgress,
        ConstructionProgress,
        ReadingProgress,
        StudentRecovery,
        ExamSettlement,
        DebtInterest,
        QuestExpiry,
        QuestBoardRefill,
        EnergyCapRecompute,
        EnergyRestore,
        Summary,
        AutoSave,
        Wake,
    ];
}
