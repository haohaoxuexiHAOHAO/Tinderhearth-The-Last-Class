using Godot;
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
/// 倒地，一个技能在冷却，剩下的技能位未解锁；随身栏里一格一件、一格一堆，其余空着。真正的上限、回复
/// 与冷却时长在设计仓 design/数值模型.md 里，那些值将来搬进规则层，不经过这里。
///
/// 技能名取的是设计里已经定过的那几个主角辅助技（标记、屏障、急救、控场），这几个不是编的 ——
/// 现编名字会在叙事侧真定名的时候留下一批要清的假事实。随身栏里的东西同理不起名字，标识只是演示用的
/// 记号，图标借的是占位的技能图标。
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
    private const int CarryStack = 12;
    private const int CarryFew = 3;

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

    /// <summary>技能名的文本键，只给已解锁的那几个位。</summary>
    /// <remarks>
    /// 技能位比这个表长，多出来的那些位在 <see cref="Build"/> 里当成未解锁、名字留空，
    /// 好让实机能看见「未解锁的位长什么样」。
    /// </remarks>
    public static readonly IReadOnlyList<string> SkillKeys =
        ["hud.skill.mark", "hud.skill.barrier", "hud.skill.firstAid", "hud.skill.control"];

    /// <summary>每条资源的文本键与演示用的当前值、上限。真文本在 <c>data/text/zh-CN.json</c> 里。</summary>
    private static readonly IReadOnlyDictionary<HudGaugeKind, (string Key, int Now, int Max)> Gauges =
        new Dictionary<HudGaugeKind, (string Key, int Now, int Max)>
        {
            [HudGaugeKind.Health] = ("hud.gauge.hp", HPNow, HPMax),
            [HudGaugeKind.Stamina] = ("hud.gauge.sp", SPNow, SPMax),
            [HudGaugeKind.Mana] = ("hud.gauge.mp", MPNow, MPMax),
            [HudGaugeKind.DailyVigor] = ("hud.gauge.vigor", VigorNow, VigorMax),
        };

    /// <summary>技能位演示用的图标。技能图标是内容、还没画，借占位的那几张；文件名里的编号从 1 起，与技能位同序。</summary>
    private const string SkillIconPathFormat = "res://assets/placeholder/ui/skill-icon-{0}.png";

    /// <summary>随身栏演示里每件东西的图标。物品图标还没画，这里只要「格里有一张图」，所以借占位的技能图标。</summary>
    private static readonly IReadOnlyDictionary<string, string> CarryIcons = new Dictionary<string, string>
    {
        ["demo.carry.single"] = "res://assets/placeholder/ui/skill-icon-1.png",
        ["demo.carry.stack"] = "res://assets/placeholder/ui/skill-icon-2.png",
        ["demo.carry.few"] = "res://assets/placeholder/ui/skill-icon-3.png",
    };

    /// <summary>造一份关卡 HUD 的演示视图模型。</summary>
    /// <param name="text">
    /// 文本表。缺键的时候 <see cref="TextCatalog"/> 会显成 <c>◆缺文本:键◆</c> —— 漏翻必须看得见才会被修。
    /// </param>
    /// <param name="teammates">队友数，从 0 到编队上限。取 0 是为了看「那一块该收起来，而不是留空槽」。</param>
    /// <param name="objective">目标进度摆成哪一种样子。</param>
    public static HudViewModel Build(TextCatalog text, int teammates,
                                    ObjectiveState objective = ObjectiveState.InProgress)
    {
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

        return new HudViewModel(GaugesIn(text, UIContext.Level), skills, Objective(text, objective), mates);
    }

    /// <summary>造一份基地 HUD 的演示视图模型：那支精力。</summary>
    /// <param name="text">文本表，同 <see cref="Build"/>。</param>
    public static BaseHudViewModel BuildBase(TextCatalog text) => new(GaugesIn(text, UIContext.Base));

    /// <summary>随身栏的演示内容：一格一件（不画件数）、一格一堆（画件数）、一格几件，其余空着（空格照画框）。</summary>
    public static IReadOnlyList<CarrySlot> CarrySlots() =>
        [.. Enumerable.Range(0, CarrySlotBar.SlotCount).Select(i => i switch
        {
            0 => new CarrySlot("demo.carry.single", 1),
            1 => new CarrySlot("demo.carry.stack", CarryStack),
            3 => new CarrySlot("demo.carry.few", CarryFew),
            _ => CarrySlot.Empty,
        })];

    /// <summary>技能位演示用的图标，给 <see cref="Tinderhearth.UI.LevelHud.SkillIconResolver"/> 用：第几位就借第几张。</summary>
    public static Texture2D? SkillIcon(HudSkillSlot slot)
    {
        var index = InputActions.Skills.ToList().IndexOf(slot.Action);
        if (index < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(slot), $"「{slot.Action}」不是技能位的动作");
        }

        var path = string.Format(SkillIconPathFormat, index + 1);
        return ResourceLoader.Exists(path)
            ? GD.Load<Texture2D>(path)
            : throw new FileNotFoundException($"技能位演示缺图标：{path}（在 Godot 里确认这个文件已导入）");
    }

    /// <summary>
    /// 随身栏演示那几件的图标，给 <see cref="Tinderhearth.UI.CarrySlotBarView.Icons"/> 用。
    /// </summary>
    /// <remarks>
    /// 不认识的标识当场抛、不返回空：返回空的意思是「这件东西没有图标」，而演示里的每一件都该有，
    /// 认不出只能是这一份写错了。
    /// </remarks>
    public static Texture2D? CarryIcon(string itemId)
    {
        if (!CarryIcons.TryGetValue(itemId, out var path))
        {
            throw new ArgumentOutOfRangeException(nameof(itemId), $"随身栏演示里没有「{itemId}」这件东西");
        }

        return ResourceLoader.Exists(path)
            ? GD.Load<Texture2D>(path)
            : throw new FileNotFoundException($"随身栏演示缺图标：{path}（在 Godot 里确认这个文件已导入）");
    }

    private static List<HudGauge> GaugesIn(TextCatalog text, UIContext context) =>
        [.. HudLayout.GaugesIn(context).Select(kind =>
        {
            var (key, now, max) = Gauges[kind];
            return new HudGauge(kind, text[key], now, max);
        })];

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
