namespace Tinderhearth.Rules.Ui;

/// <summary>
/// 输入动作名（`UI-7`）。**与设备无关** —— 玩法代码只认这些名字，不认按键。
/// </summary>
/// <remarks>
/// 为什么动作名是常量而不是各处写字符串字面量：拼错一个动作名不会报错，只会表现为「这个键
/// 没反应」，而排查时你分不清是没绑、绑错还是名字打错。常量让拼错变成编译失败。
///
/// 清单来自[战斗与关卡 · 按键与连击]：轻攻击、重攻击、防御、闪避、冲刺、跳跃、交互，加 6 个
/// 技能位。**闪避与冲刺是两个动作**（作者 2026-08-30 定，正典同日已改）—— 正典的 SP 表本来就
/// 把两者列成独立消耗，合并按键是那份文档里不自洽的一处。
///
/// 移动不在正典的按键清单里，但显然要绑，所以在这里补齐四向：俯视的基地与城区要四向，侧视
/// 关卡只用左右。四个动作而不是一个二维量，是因为 `InputMap` 的单位就是动作。
///
/// **界面动作刻意不在这里。** 返回、确认与焦点移动用引擎内置的 `ui_*`（`UiRoot` 已经在用
/// `ui_cancel`），重复定义一套只会多一份要维护的东西。打开手环／背包的动作也不在本条范围。
/// </remarks>
public static class InputActions
{
    /// <summary>四向移动。侧视关卡只用左右，俯视场景四向都用。</summary>
    public const string MoveLeft = "move_left";
    public const string MoveRight = "move_right";
    public const string MoveUp = "move_up";
    public const string MoveDown = "move_down";

    /// <summary>
    /// 冲刺：**按住期间的移动即为冲刺**，不是一个独立的位移键。
    /// </summary>
    /// <remarks>
    /// 正典要求它是显式输入。理由是冲刺扣 SP，而 SP 耗尽的后果是禁止防御并进入失衡 ——
    /// 「按住方向键自动进入冲刺」会让玩家赶路时不知不觉花掉保命的 SP，那与正典给失衡设逃生
    /// 窗口要避免的是同一件事：被压制成为必然而玩家看不出原因。
    ///
    /// 「按住持续高速移动」还是「单次突进」尚未裁定（归战斗系统）。**两种读法的绑定完全相同**，
    /// 所以本条不受阻塞。
    /// </remarks>
    public const string Sprint = "sprint";

    /// <summary>正典点名的六个战斗动作（冲刺另计，见上）。</summary>
    public const string AttackLight = "attack_light";
    public const string AttackHeavy = "attack_heavy";
    public const string Guard = "guard";
    public const string Dodge = "dodge";
    public const string Jump = "jump";
    public const string Interact = "interact";

    /// <summary>技能位的两个修饰键。手柄上是左右扳机，键鼠上不用（数字键直接对应）。</summary>
    public const string SkillGroupLeft = "skill_group_left";
    public const string SkillGroupRight = "skill_group_right";

    /// <summary>6 个技能位，顺序即编号。</summary>
    public static readonly IReadOnlyList<string> Skills =
        ["skill_1", "skill_2", "skill_3", "skill_4", "skill_5", "skill_6"];

    /// <summary>一组修饰键覆盖几个技能位。3 面键 × 2 修饰键 ＝ 6，正好铺满。</summary>
    public const int SkillsPerGroup = 3;

    /// <summary>
    /// 修饰键按住时被**遮住**的动作，顺序即它们在一组里对应第几个技能位。
    /// </summary>
    /// <remarks>
    /// 这一份清单同时是两件事的唯一来源：哪些动作在修饰键按住时不许触发，以及
    /// 「修饰键 + 第 N 个面键」对应哪个技能位。写成两份必然漂移。
    ///
    /// 顺序对应手柄面键 X → Y → A（轻攻击、重攻击、跳跃）。**闪避刻意不在其中** ——
    /// 它占第四个面键并且永远可用：正典把它定为唯一带无敌帧的脱身手段，还专门为失衡结束设了
    /// 逃生窗口，所以它是最不能在任何窗口里失效的动作。跳跃与轻重攻击在按住修饰键的那零点
    /// 几秒里失效，是修饰键方案本身的代价（作者 2026-08-30 接受）。
    /// </remarks>
    public static readonly IReadOnlyList<string> ShadowedByModifier =
        [AttackLight, AttackHeavy, Jump];

    /// <summary>本作自己定义的全部动作。引擎层照它灌 `InputMap`，守卫照它核对。</summary>
    public static readonly IReadOnlyList<string> All =
    [
        MoveLeft, MoveRight, MoveUp, MoveDown,
        Sprint,
        AttackLight, AttackHeavy, Guard, Dodge, Jump, Interact,
        SkillGroupLeft, SkillGroupRight,
        .. Skills,
    ];

    /// <summary>某个动作是不是技能位。</summary>
    public static bool IsSkill(string action) => Skills.Contains(action);

    /// <summary>某个动作会不会被修饰键遮住。</summary>
    public static bool IsShadowedByModifier(string action) => ShadowedByModifier.Contains(action);
}
