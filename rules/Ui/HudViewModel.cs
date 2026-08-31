namespace Tinderhearth.Rules.Ui;

/// <summary>哪一条资源。**只用来决定配色**，量与上限由视图模型带进来。</summary>
public enum HudGaugeKind
{
    /// <summary>HP。正典：生命。</summary>
    Health,

    /// <summary>SP。正典：冲刺、闪避、跳跃与普通防御的消耗，关卡内自回。</summary>
    Stamina,

    /// <summary>MP。正典：施放技能，普攻命中回复。</summary>
    Mana,

    /// <summary>日体力。经营侧的量，关卡内不变 —— 摆在这里是为了让玩家出征前后都看得见同一个数。</summary>
    DailyVigor,
}

/// <summary>
/// 一条资源条。
/// </summary>
/// <param name="Kind">哪一条，决定配色。</param>
/// <param name="Label">显示的标签。**已经取过文本表的成品字符串**，界面不做查表。</param>
/// <param name="Current">当前值。</param>
/// <param name="Max">上限。</param>
/// <remarks>
/// <paramref name="Current"/> 与 <paramref name="Max"/> 是**玩法数值**，一律由调用方传入 ——
/// `UI-8` 不读也不搬 `design/数值模型.md` 的参数表（PRD 第 8 节）。测试里注入假值，实机演示
/// 用一份明确标为演示的数据。
/// </remarks>
public sealed record HudGauge(HudGaugeKind Kind, string Label, int Current, int Max)
{
    /// <summary>填充比例，钳在 0–1。上限为 0 时算 0 —— 不除零，也不显示成满格。</summary>
    public double Ratio => Max <= 0 ? 0.0 : Math.Clamp((double)Current / Max, 0.0, 1.0);
}

/// <summary>
/// 一个技能位。
/// </summary>
/// <param name="Action">对应的输入动作名（<see cref="InputActions.Skills"/> 里的一个）。</param>
/// <param name="Label">显示的名字，未解锁时可为空串。</param>
/// <param name="Unlocked">解锁了没有。没解锁的位显示空框而不是隐藏 —— 位置固定才有肌肉记忆。</param>
/// <param name="CooldownRemaining">冷却剩余比例，1 ＝ 刚放完，0 ＝ 可用。</param>
/// <remarks>
/// **冷却时长不在这里**，只有比例。时长归 `design/数值模型.md`，本条只做表现形式
/// （从下往上退去的暗色遮罩 + 冷却中不显示按键提示）。
/// </remarks>
public sealed record HudSkillSlot(string Action, string Label, bool Unlocked,
                                 double CooldownRemaining)
{
    /// <summary>现在能不能放。</summary>
    public bool Ready => Unlocked && CooldownRemaining <= 0.0;

    /// <summary>冷却遮罩该盖住多少，钳在 0–1。</summary>
    public double MaskRatio => Math.Clamp(CooldownRemaining, 0.0, 1.0);
}

/// <summary>
/// 目标进度。
/// </summary>
/// <param name="Label">目标名，例如「素材」「来源点」。</param>
/// <param name="Done">已完成数量。</param>
/// <param name="Total">需要的数量。</param>
/// <param name="DoneMessage">达成后显示的那句话。</param>
/// <remarks>
/// **为 0 或已达成时仍然显示**，不隐藏（[战斗与关卡]：目标进度必须始终可见 —— 没有目标就没有
/// 推进感，而进度看不见等于没有目标）。达成后显示的是「返回入口点撤离」而不是「已完成」，因为
/// 正典明确达成目标后不自动结束关卡，玩家还得走回去。
/// </remarks>
public sealed record HudObjective(string Label, int Done, int Total, string DoneMessage)
{
    /// <summary>达成了没有。**总数为 0 也算达成** —— 没有要采的东西就等于不用采。</summary>
    public bool Complete => Total <= 0 || Done >= Total;

    /// <summary>还差多少。达成后是 0，不会是负数。</summary>
    public int Remaining => Math.Max(Total - Done, 0);
}

/// <summary>
/// 一名队友的状态。
/// </summary>
/// <param name="Current">当前 HP。</param>
/// <param name="Max">HP 上限。</param>
/// <param name="Down">倒地了没有。倒地不等于 HP 为 0 —— 正典有「去扶倒地的同伴」这条行为。</param>
/// <remarks>
/// **刻意没有名字这一项。** 队友格只有 20px 宽（头像框的尺寸），12px 汉字放不下两个字，而
/// [ADR-0008] 已排除更小的字号。识别靠头像本身 —— 那正是[人物 · 形象记忆点设计]要求每个角色
/// 有独立剪影与签名色的用处，占位件看不出区别，换成正式头像就看得出。
///
/// 于是「倒地」这个状态不能只靠颜色区分（[像素绘制原则 §4]：相反语义要有不同符号），
/// 界面另加一个记号盖在头像上。
/// </remarks>
public sealed record HudTeammate(int Current, int Max, bool Down)
{
    /// <summary>血量比例，钳在 0–1。</summary>
    public double Ratio => Max <= 0 ? 0.0 : Math.Clamp((double)Current / Max, 0.0, 1.0);
}

/// <summary>
/// 关卡 HUD 要显示的全部东西（`UI-8`）。**界面只渲染它，不去别处取数。**
/// </summary>
/// <remarks>
/// 为什么要有这么一层：PRD 第 8 节写明「HUD 显示的数值从视图模型传入，`UI-1` 不读也不搬 `GP-2`
/// 的参数表」。没有这一层的话，界面代码会顺手写一个 100 当 HP 上限 —— 那个数不会报错，只会在
/// 数值模型真接进来的那天变成两份互相矛盾的事实。
///
/// 构造时就校验条数（资源 4 条、技能 6 位、队友不超过编队上限），**对不上直接抛**。静默少画一格
/// 的后果是界面看起来正常而信息缺了一条，那种缺陷没人会发现。
/// </remarks>
public sealed class HudViewModel
{
    /// <summary>装一份视图模型。**条数对不上直接抛**，不静默少画一格。</summary>
    public HudViewModel(IReadOnlyList<HudGauge> gauges, IReadOnlyList<HudSkillSlot> skills,
                        HudObjective objective, IReadOnlyList<HudTeammate> teammates)
    {
        Gauges = Require(gauges, HudLayout.GaugeCount, "资源条");
        Skills = Require(skills, InputActions.Skills.Count, "技能位");
        Objective = objective;
        Teammates = teammates.Count <= HudLayout.MaxTeammates
            ? teammates
            : throw new ArgumentOutOfRangeException(nameof(teammates),
                $"队友 {teammates.Count} 名超过编队上限 {HudLayout.MaxTeammates}");
    }

    /// <summary>四条资源，顺序即显示顺序。</summary>
    public IReadOnlyList<HudGauge> Gauges { get; }

    /// <summary>6 个技能位，顺序即编号。</summary>
    public IReadOnlyList<HudSkillSlot> Skills { get; }

    /// <summary>目标进度。</summary>
    public HudObjective Objective { get; }

    /// <summary>队友，0 至编队上限。</summary>
    public IReadOnlyList<HudTeammate> Teammates { get; }

    /// <summary>
    /// 队友区显不显示。**为 0 时收起整块，而不是留 4 个空槽。**
    /// </summary>
    /// <remarks>
    /// 空槽会让「单人采集」看起来像「三个队友没加载出来」。收起是正确的表达：这一趟就是一个人去。
    /// </remarks>
    public bool ShowTeammates => Teammates.Count > 0;

    /// <summary>不用类做整体替换时按块换：只换资源，其余照旧。</summary>
    public HudViewModel WithGauges(IReadOnlyList<HudGauge> gauges) =>
        new(gauges, Skills, Objective, Teammates);

    /// <summary>只换技能位。</summary>
    public HudViewModel WithSkills(IReadOnlyList<HudSkillSlot> skills) =>
        new(Gauges, skills, Objective, Teammates);

    /// <summary>只换目标进度。</summary>
    public HudViewModel WithObjective(HudObjective objective) =>
        new(Gauges, Skills, objective, Teammates);

    /// <summary>只换队友。</summary>
    public HudViewModel WithTeammates(IReadOnlyList<HudTeammate> teammates) =>
        new(Gauges, Skills, Objective, teammates);

    private static IReadOnlyList<T> Require<T>(IReadOnlyList<T> items, int expected, string what) =>
        items.Count == expected
            ? items
            : throw new ArgumentException($"{what}应有 {expected} 条，实际 {items.Count} 条");
}
