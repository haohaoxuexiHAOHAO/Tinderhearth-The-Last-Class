namespace Tinderhearth.Rules.UI;

/// <summary>哪一条资源。它只用来决定配色，量与上限由视图模型带进来。</summary>
public enum HudGaugeKind
{
    /// <summary>生命。</summary>
    Health,

    /// <summary>
    /// 体力（缩写 SP）：奔跑、闪避、跳跃与普通防御的消耗，关卡内自回。它与
    /// <see cref="DailyVigor"/> 是两条不共用预算的资源。
    /// </summary>
    Stamina,

    /// <summary>法力：施放技能要扣，普攻命中回一点。</summary>
    Mana,

    /// <summary>
    /// 精力：经营侧当日一切行动的共同预算，关卡内不变。摆在 HUD 上是为了让玩家出征前后看到的
    /// 是同一个数。
    /// </summary>
    DailyVigor,
}

/// <summary>
/// 一条资源条。
/// </summary>
/// <param name="Kind">哪一条，决定配色。</param>
/// <param name="Label">显示的标签。已经查过文本表的成品字符串，界面不再查表。</param>
/// <param name="Current">当前值。</param>
/// <param name="Max">上限。</param>
/// <remarks>
/// <paramref name="Current"/> 和 <paramref name="Max"/> 是玩法数值，一律由调用方传入，界面层
/// 不去读设计仓 design/数值模型.md 的参数表。测试里注入假值，实机演示用一份明确标为演示的数据。
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
/// 这里只有比例，没有冷却时长。时长归设计仓 design/数值模型.md，界面只管表现形式：
/// 一层从下往上退去的暗色遮罩，冷却中不显示按键提示。
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
/// 进度为 0 或者已达成时照样显示，不隐藏：进度看不见等于没有目标。达成后显示的是「返回入口点
/// 撤离」而不是「已完成」，因为达成目标不会自动结束关卡，玩家还得走回去。详见设计仓
/// canon/gameplay/战斗与关卡.md。
/// </remarks>
public sealed record HudObjective(string Label, int Done, int Total, string DoneMessage)
{
    /// <summary>达成了没有。总数为 0 也算达成：没有要采的东西就等于不用采。</summary>
    public bool Complete => Total <= 0 || Done >= Total;

    /// <summary>还差多少。达成后是 0，不会是负数。</summary>
    public int Remaining => Math.Max(Total - Done, 0);
}

/// <summary>
/// 一名队友的状态。
/// </summary>
/// <param name="Current">当前 HP。</param>
/// <param name="Max">HP 上限。</param>
/// <param name="Down">倒地了没有。倒地不等于 HP 为 0，玩家可以去扶倒地的同伴。</param>
/// <remarks>
/// 没有名字这一项。队友格就是头像框那么宽，汉字放不下两个，而更小的字号已经被排除。识别靠头像
/// 本身，所以每个角色要有独立的剪影与签名色（设计仓 production/像素绘制原则.md）。占位头像看不
/// 出区别，换成正式头像就看得出。
///
/// 于是「倒地」不能只靠颜色区分，界面另加一个记号盖在头像上。相反语义要有不同符号，不能只换色。
/// </remarks>
public sealed record HudTeammate(int Current, int Max, bool Down)
{
    /// <summary>血量比例，钳在 0–1。</summary>
    public double Ratio => Max <= 0 ? 0.0 : Math.Clamp((double)Current / Max, 0.0, 1.0);
}

/// <summary>关卡 HUD 要显示的全部东西。界面只渲染它，不去别处取数。</summary>
/// <remarks>
/// 要这么一层是因为没有它的话，界面代码会顺手写一个数当 HP 上限。那个数不报错，只会在真玩法
/// 数值接进来的那天变成两份互相矛盾的事实。
///
/// 构造时就校验条数：资源、技能位、队友各自该有几条，对不上直接抛。静默少画一格的后果是界面
/// 看起来正常而信息缺了一条，那种缺陷没人会发现。
/// </remarks>
public sealed class HudViewModel
{
    /// <summary>装一份视图模型。条数对不上直接抛，不静默少画一格。</summary>
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

    /// <summary>各条资源，顺序就是显示顺序。</summary>
    public IReadOnlyList<HudGauge> Gauges { get; }

    /// <summary>各个技能位，顺序就是编号。</summary>
    public IReadOnlyList<HudSkillSlot> Skills { get; }

    /// <summary>目标进度。</summary>
    public HudObjective Objective { get; }

    /// <summary>队友，从没有到编队上限。</summary>
    public IReadOnlyList<HudTeammate> Teammates { get; }

    /// <summary>队友区显不显示。一个队友都没有时收起整块，而不是留一排空槽。</summary>
    /// <remarks>
    /// 空槽会让「单人采集」看起来像「队友没加载出来」。收起才是正确的表达：这一趟就是一个人去。
    /// </remarks>
    public bool ShowTeammates => Teammates.Count > 0;

    /// <summary>按块替换：只换资源，其余照旧。</summary>
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
