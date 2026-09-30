namespace Tinderhearth.Rules.Ui;

/// <summary>
/// 输入动作名（`UI-7`）。**与设备无关** —— 玩法代码只认这些名字，不认按键。
/// </summary>
/// <remarks>
/// 为什么动作名是常量而不是各处写字符串字面量：拼错一个动作名不会报错，只会表现为「这个键
/// 没反应」，而排查时你分不清是没绑、绑错还是名字打错。常量让拼错变成编译失败。
///
/// 清单来自[战斗与关卡 · 按键与连击]：轻攻击、重攻击、防御、闪避、冲刺、跳跃、交互，加 6 个
/// 技能位。**闪避与奔跑是两个动作** —— 正典的 SP 表本来就
/// 把两者列成独立消耗，合并按键是那份文档里不自洽的一处。
///
/// 移动不在正典的按键清单里，但显然要绑，所以在这里补齐四向：俯视的基地与城区要四向，侧视
/// 关卡只用左右。四个动作而不是一个二维量，是因为 `InputMap` 的单位就是动作。
///
/// **<see cref="EnterInterior"/> 也不在那份战斗清单里** —— 它是经营侧的，来源是 [ADR-0027]
/// （进出建筑室内改成点一下门）。那份清单管的是侧视战斗，所以经营侧新增一个动作不算破例。
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
    /// 「按住方向键自动进入冲刺」会让玩家赶路时不知不觉花掉保命的 SP，那与正典设逃生窗口
    /// 要避免的是同一件事：被压制成为必然而玩家看不出原因。
    ///
    /// 「按住持续高速移动」还是「单次突进」尚未裁定（归战斗系统）。**两种读法的绑定完全相同**，
    /// 所以本条不受阻塞。
    /// </remarks>
    /// <remarks>
    /// **全库统一叫 `Run`**，输入侧与运动侧同名，不让同一件事跨层两个名字。
    /// **取 `Run` 而不是 `Dash` 是语义问题**：本作这个机制是「按住键持续加速移动」，那是奔跑；
    /// dash 在动作游戏里通常指一次性的短距突进（有固定距离与时长），与这里不是一回事。地面移动
    /// 因此只有两档 —— 行走与奔跑（＝设计文档里说的「冲刺」），**没有第三个状态**。
    /// </remarks>
    public const string Run = "run";

    /// <summary>正典点名的六个战斗动作（冲刺另计，见上）。</summary>
    public const string AttackLight = "attack_light";
    public const string AttackHeavy = "attack_heavy";
    public const string Guard = "guard";
    public const string Dodge = "dodge";
    public const string Jump = "jump";
    public const string Interact = "interact";

    /// <summary>点一下建筑的门，进或出它的室内场景。</summary>
    /// <remarks>
    /// **进与出是同一个动作**，方向由「现在人在里面还是外面」推出来，不存第二个动作 ——
    /// 存了就要回答「两者不一致时听谁的」。
    ///
    /// **为什么它不复用 <see cref="Interact"/>**（[ADR-0027]）：键鼠上 `Interact` 的第一条绑定是
    /// 鼠标左键，而左键是经营侧那些逐格动作的执行位。把门塞进同一次点击就要回答「这一格既是门
    /// 又是一格地时点的是哪个」，而那个问题没有好答案。所以键鼠上门另给一个键（鼠标右键）。
    ///
    /// **手柄上没有它的绑定，那是登记过的豁免不是漏绑**（见 <see cref="InputBindings.Exemptions"/>）：
    /// 手柄没有指针，它靠站位与朝向决定对象，所以一个交互键就够。键鼠要分两个键，是因为指针指
    /// 得到两种不同的东西。
    ///
    /// **它不带读条、不加动作机姿态。** 基地里没有打断源，读条在那里只剩纯等待。
    /// </remarks>
    public const string EnterInterior = "enter_interior";

    /// <summary>技能位的两个修饰键。手柄上是左右扳机，键鼠上不用（六个键一一对应）。</summary>
    public const string SkillGroupLeft = "skill_group_left";
    public const string SkillGroupRight = "skill_group_right";

    /// <summary>6 个技能位，顺序即编号。</summary>
    public static readonly IReadOnlyList<string> Skills =
        ["skill_1", "skill_2", "skill_3", "skill_4", "skill_5", "skill_6"];

    /// <summary>
    /// 随身栏那几格，顺序即编号（`UI-34`）。**格数与技能位同一个数**，理由在
    /// <see cref="CarrySlotBar.SlotCount"/>。
    /// </summary>
    /// <remarks>
    /// 键鼠上它们就是数字键 `1`–`6` —— 那一排正是 `UI-14` 把技能位挪走之后腾出来的，
    /// 而那份提案当时写明「刻意不补，留给将来真要一条快捷道具栏」。这就是那条道具栏。
    ///
    /// **手柄上它们一个都没有绑定**，那是登记过的豁免（见 <see cref="InputBindings.Exemptions"/>）：
    /// 手柄上的键已经很紧，改用 <see cref="CarryPrev"/> 与 <see cref="CarryNext"/> 挪选中位。
    /// </remarks>
    public static readonly IReadOnlyList<string> CarrySlots =
        ["carry_1", "carry_2", "carry_3", "carry_4", "carry_5", "carry_6"];

    /// <summary>
    /// 把随身栏的选中位往前、往后挪一格（`UI-34`）。**手柄那一侧用这两个。**
    /// </summary>
    /// <remarks>
    /// **为什么手柄不给六格各一个键**：两个扳机已经当了技能位的修饰键，四个面键、两个肩键与
    /// 左摇杆按压也都有归属 —— 手柄上凑不出六个空位。挪选中位只要两个键，代价是切到最远那一格
    /// 要按几下（绕一圈，见 <see cref="CarrySlotBar.SelectNext"/>），而经营侧不赶时间。
    ///
    /// **被放弃的两条**：按住某个键加右摇杆直选（六格映射八方向要留两个空位，而空位按下去没反应）、
    /// 加一个第三个扳机态（「我在哪一态」要常驻提示，又占屏）。
    ///
    /// **键鼠上这两个动作没有绑定**，那是登记过的豁免：那边六个数字键直接对应，不需要挪。
    /// </remarks>
    public const string CarryPrev = "carry_prev";
    public const string CarryNext = "carry_next";

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
    /// 它占第四个面键并且永远可用：正典把它定为唯一带无敌帧的脱身手段，还专门为被击倒后
    /// 起身设了逃生窗口，所以它是最不能在任何窗口里失效的动作。跳跃与轻重攻击在按住修饰键的那零点
    /// 几秒里失效，是修饰键方案本身的代价，已接受。
    /// </remarks>
    public static readonly IReadOnlyList<string> ShadowedByModifier =
        [AttackLight, AttackHeavy, Jump];

    /// <summary>本作自己定义的全部动作。引擎层照它灌 `InputMap`，守卫照它核对。</summary>
    public static readonly IReadOnlyList<string> All =
    [
        MoveLeft, MoveRight, MoveUp, MoveDown,
        Run,
        AttackLight, AttackHeavy, Guard, Dodge, Jump, Interact,
        EnterInterior,
        SkillGroupLeft, SkillGroupRight,
        .. Skills,
        .. CarrySlots,
        CarryPrev, CarryNext,
    ];

    /// <summary>某个动作是不是技能位。</summary>
    public static bool IsSkill(string action) => Skills.Contains(action);

    /// <summary>某个动作会不会被修饰键遮住。</summary>
    public static bool IsShadowedByModifier(string action) => ShadowedByModifier.Contains(action);
}
