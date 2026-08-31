using Tinderhearth.Rules.Foundation.Text;
using Tinderhearth.Rules.Ui;

namespace Tinderhearth.World;

/// <summary>
/// HUD 的**演示数据**（`UI-8`）。**这不是玩法数值，也不是它的落点。**
/// </summary>
/// <remarks>
/// 为什么单独一个文件而不是塞进 <see cref="Tinderhearth.UI.LevelHud"/>：PRD 第 8 节要求
/// 「HUD 显示的数值全部由视图模型传入，`UI-1` 不读也不搬 `GP-2` 的参数表」。真界面里一个数字
/// 都不该有，于是**唯一有数字的地方必须在界面之外，并且看得出是演示** —— `tools/check_hud.py`
/// 扫界面代码的数字字面量，正是靠这个分工才扫得干净。
///
/// 里面那几个数是当场编的，用来把四种呈现都摆出来：一条满、一条半、一条快空、一位倒地、
/// 一个技能在冷却、两个技能未解锁。真正的上限、回复与冷却时长在设计仓 `design/数值模型.md`，
/// 搬进规则层属各玩法实现需求。
///
/// 技能名取正典点名的四个主角辅助技（标记、屏障、急救、控场），不是编的 —— 编名字会在叙事侧
/// 定名时留下一批要清的假事实。剩下两个位故意留成未解锁。
///
/// 与 <see cref="CameraHarness"/> 同性质：`UI-10` 的端到端测试会用自己准备的数据替换它。
/// </remarks>
public static class HudDemoModel
{
    // 演示用的量。**编的**，见类注释。
    private const int HpMax = 34;
    private const int HpNow = 21;
    private const int SpMax = 20;
    private const int SpNow = 17;
    private const int MpMax = 24;
    private const int MpNow = 6;
    private const int VigorMax = 12;
    private const int VigorNow = 9;
    private const int MateHpMax = 26;
    private const double DemoCooldown = 0.55;

    /// <summary>目标进度的三种态，用来实机看「为 0 与已达成时仍然显示」。</summary>
    public enum ObjectiveState
    {
        /// <summary>进行中。</summary>
        InProgress,

        /// <summary>已达成。</summary>
        Achieved,

        /// <summary>总数为 0 —— 也该显示成已达成。</summary>
        Empty,
    }

    /// <summary>文本键。真文本在 `data/config` 之外的 `data/text/zh-CN.json`（`ENG-5`）。</summary>
    public static readonly IReadOnlyList<string> GaugeKeys =
        ["hud.gauge.hp", "hud.gauge.sp", "hud.gauge.mp", "hud.gauge.vigor"];

    /// <summary>技能名的文本键。后两个是空串 —— 那两个位未解锁。</summary>
    public static readonly IReadOnlyList<string> SkillKeys =
        ["hud.skill.mark", "hud.skill.barrier", "hud.skill.firstAid", "hud.skill.control"];

    /// <summary>本 HUD 用到的全部文本键。启动时用它核一遍有没有漏（`TextCatalog.MissingKeys`）。</summary>
    public static IReadOnlyList<string> RequiredKeys =>
        [.. GaugeKeys, .. SkillKeys, "hud.objective.material", "hud.objective.done"];

    /// <summary>造一份演示视图模型。</summary>
    /// <param name="text">
    /// 文本表。缺键会显成 <c>◆缺文本:键◆</c>（<see cref="TextCatalog"/> 的有意设计：漏翻必须
    /// 看得见才会被修），所以启动时另用 <see cref="RequiredKeys"/> 报一次缺了几条。
    /// </param>
    /// <param name="teammates">队友数，0 到编队上限。取 0 用来看「该区收起而不是留空槽」。</param>
    /// <param name="objective">目标进度取哪一态。</param>
    public static HudViewModel Build(TextCatalog text, int teammates,
                                    ObjectiveState objective = ObjectiveState.InProgress)
    {
        var kinds = Enum.GetValues<HudGaugeKind>();
        var values = new[] { (HpNow, HpMax), (SpNow, SpMax), (MpNow, MpMax), (VigorNow, VigorMax) };
        var gauges = kinds
            .Select((kind, i) => new HudGauge(kind, text[GaugeKeys[i]], values[i].Item1, values[i].Item2))
            .ToList();

        var skills = InputActions.Skills
            .Select((action, i) => new HudSkillSlot(
                Action: action,
                Label: i < SkillKeys.Count ? text[SkillKeys[i]] : string.Empty,
                Unlocked: i < SkillKeys.Count,
                // 第二个位在冷却中间，好让冷却表现在存图里看得见。
                CooldownRemaining: i == 1 ? DemoCooldown : 0.0))
            .ToList();

        var mates = Enumerable.Range(0, teammates)
            // 第三位倒地，其余按名次掉一点血 —— 四格不能长得一样，否则看不出这块在表达什么。
            .Select(i => new HudTeammate(MateHpMax - (i * i * 3), MateHpMax, Down: i == 2))
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
