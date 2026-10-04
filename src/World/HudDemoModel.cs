using Tinderhearth.Rules.Foundation.Text;
using Tinderhearth.Rules.UI;

namespace Tinderhearth.World;

/// <summary>
/// 给 HUD 看的一份演示数据。这里的数字不是玩法数值，真玩法接进来之后这个类就退场。
/// </summary>
/// <remarks>
/// 单独一个文件而不是塞进 <see cref="Tinderhearth.UI.LevelHud"/>，是为了让界面里一个数字都没有 ——
/// 唯一有数字的地方必须在界面之外，并且一眼看得出是演示。<c>CONVENTIONS.md</c> 那条「界面里除 0
/// 与 1 之外无数字字面量」做得到，靠的就是这个分工。
///
/// 里面的数都是当场编的，只为把各种呈现都摆出来一遍看看：一条资源满、一条半、一条快空，一位队友
/// 倒地，一个技能在冷却，剩下的技能位未解锁。真正的上限、回复与冷却时长在设计仓
/// design/数值模型.md 里，那些值将来搬进规则层，不经过这里。
///
/// 技能名取的是设计里已经定过的那几个主角辅助技（标记、屏障、急救、控场），这几个不是编的 ——
/// 现编名字会在叙事侧真定名的时候留下一批要清的假事实。
/// </remarks>
public static class HudDemoModel
{
    // 下面这些数都是编的，见类说明。
    private const int HPMax = 34;
    private const int HPNow = 21;
    private const int SPMax = 20;
    private const int SPNow = 17;
    private const int MPMax = 24;
    private const int MPNow = 6;
    private const int VigorMax = 12;
    private const int VigorNow = 9;
    private const int MateHPMax = 26;
    private const double DemoCooldown = 0.55;

    /// <summary>目标进度的三种样子，用来实机确认「进度为 0 和已达成的时候也照样显示」。</summary>
    public enum ObjectiveState
    {
        /// <summary>进行中。</summary>
        InProgress,

        /// <summary>已达成。</summary>
        Achieved,

        /// <summary>总数就是 0。这一种也该显示成已达成。</summary>
        Empty,
    }

    /// <summary>资源条标签的文本键。真文本在 <c>data/text/zh-CN.json</c> 里。</summary>
    public static readonly IReadOnlyList<string> GaugeKeys =
        ["hud.gauge.hp", "hud.gauge.sp", "hud.gauge.mp", "hud.gauge.vigor"];

    /// <summary>技能名的文本键，只给已解锁的那几个位。</summary>
    /// <remarks>
    /// 技能位比这个表长，多出来的那些位在 <see cref="Build"/> 里当成未解锁、名字留空，
    /// 好让实机能看见「未解锁的位长什么样」。
    /// </remarks>
    public static readonly IReadOnlyList<string> SkillKeys =
        ["hud.skill.mark", "hud.skill.barrier", "hud.skill.firstAid", "hud.skill.control"];

    /// <summary>这块 HUD 用到的全部文本键。启动时拿它核一遍文本表有没有漏条。</summary>
    public static IReadOnlyList<string> RequiredKeys =>
        [.. GaugeKeys, .. SkillKeys, "hud.objective.material", "hud.objective.done"];

    /// <summary>造一份演示用的视图模型。</summary>
    /// <param name="text">
    /// 文本表。缺键的时候 <see cref="TextCatalog"/> 会显成 <c>◆缺文本:键◆</c> —— 漏翻必须看得见
    /// 才会被修。启动时另外拿 <see cref="RequiredKeys"/> 报一次总共缺了几条。
    /// </param>
    /// <param name="teammates">队友数，从 0 到编队上限。取 0 是为了看「那一块该收起来，而不是留空槽」。</param>
    /// <param name="objective">目标进度摆成哪一种样子。</param>
    public static HudViewModel Build(TextCatalog text, int teammates,
                                    ObjectiveState objective = ObjectiveState.InProgress)
    {
        var kinds = Enum.GetValues<HudGaugeKind>();
        var values = new[] { (HPNow, HPMax), (SPNow, SPMax), (MPNow, MPMax), (VigorNow, VigorMax) };
        var gauges = kinds
            .Select((kind, i) => new HudGauge(kind, text[GaugeKeys[i]], values[i].Item1, values[i].Item2))
            .ToList();

        var skills = InputActions.Skills
            .Select((action, i) => new HudSkillSlot(
                Action: action,
                Label: i < SkillKeys.Count ? text[SkillKeys[i]] : string.Empty,
                Unlocked: i < SkillKeys.Count,
                // 挑一个位摆在冷却走到一半的样子，好让冷却的画法在截图里看得见。
                CooldownRemaining: i == 1 ? DemoCooldown : 0.0))
            .ToList();

        var mates = Enumerable.Range(0, teammates)
            // 挑一位摆成倒地，其余按次序各掉一点血 —— 几格长得一样就看不出这块在表达什么。
            .Select(i => new HudTeammate(MateHPMax - (i * i * 3), MateHPMax, Down: i == 2))
            .ToList();

        return new HudViewModel(gauges, skills, Objective(text, objective), mates);
    }

    private static HudObjective Objective(TextCatalog text, ObjectiveState state)
    {
        var label = text["hud.objective.material"];
        var done = text["hud.objective.done"];
        return state switch
        {
            ObjectiveState.InProgress => new HudObjective(label, Done: 2, Total: 5, done),
            ObjectiveState.Achieved => new HudObjective(label, Done: 5, Total: 5, done),
            ObjectiveState.Empty => new HudObjective(label, Done: 0, Total: 0, done),
            _ => throw new ArgumentOutOfRangeException(nameof(state)),
        };
    }
}
