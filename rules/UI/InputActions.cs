namespace Tinderhearth.Rules.UI;

/// <summary>输入动作名。玩法代码只认这些名字，不认具体按键，所以它与设备无关。</summary>
/// <remarks>
/// 写成常量而不是各处写字符串字面量，是因为拼错一个动作名不报错，只表现为「这个键没反应」，
/// 排查时分不清是没绑、绑错还是名字打错。常量让拼错变成编译失败。
///
/// 战斗那几个动作（轻攻击、重攻击、防御、闪避、奔跑、跳跃、交互，加六个技能位）出自设计仓
/// canon/gameplay/战斗与关卡.md。移动那四向不在那份清单里但显然要绑：俯视的基地和城区要四向，
/// 侧视关卡只用左右。拆成四个动作而不是一个二维量，因为引擎输入映射的单位就是动作。
///
/// 界面动作不在这里。返回、确认与焦点移动用引擎内置的 <c>ui_*</c>，再定义一套只会多一份要维护
/// 的东西。
/// </remarks>
public static class InputActions
{
    /// <summary>四向移动。侧视关卡只用左右，俯视场景四向都用。</summary>
    public const string MoveLeft = "move_left";
    public const string MoveRight = "move_right";
    public const string MoveUp = "move_up";
    public const string MoveDown = "move_down";

    /// <summary>奔跑：按住它期间的移动就是奔跑，不是一个独立的位移键。</summary>
    /// <remarks>
    /// 它必须是玩家显式按下的，不能「按住方向键自动进入」。因为奔跑扣 SP，而 SP 耗尽会禁止防御
    /// 并进入失衡，自动进入会让玩家赶路时不知不觉花掉保命的 SP，而他看不出原因。
    ///
    /// 全库统一叫 Run，输入侧与运动侧同名。不叫 Dash 是语义问题：本作这个机制是按住键持续加速
    /// 移动，那是奔跑；dash 在动作游戏里通常指一次性的短距突进。所以地面移动只有行走和奔跑两档，
    /// 没有第三个状态。
    /// </remarks>
    public const string Run = "run";

    /// <summary>设计里点名的六个战斗动作，奔跑另算（见上）。</summary>
    public const string AttackLight = "attack_light";
    public const string AttackHeavy = "attack_heavy";
    public const string Guard = "guard";
    public const string Dodge = "dodge";
    public const string Jump = "jump";
    public const string Interact = "interact";

    /// <summary>点一下建筑的门，进或出它的室内场景。它不带读条。</summary>
    /// <remarks>
    /// 进和出是同一个动作，方向由「现在人在里面还是外面」推出来。拆成两个动作就要回答「两者
    /// 不一致时听谁的」。
    ///
    /// 它不复用 <see cref="Interact"/>，因为键鼠上 Interact 的第一条绑定是鼠标左键，而左键是
    /// 经营侧那些逐格动作的执行位。塞进同一次点击就要回答「这一格既是门又是一格地时点的是哪个」。
    /// 所以键鼠上门另给鼠标右键。
    ///
    /// 手柄上它没有绑定，那是登记过的豁免、不是漏绑（见 <see cref="InputBindings.Exemptions"/>）。
    /// 手柄没有指针，靠站位和朝向决定对象，一个交互键就够。
    /// </remarks>
    public const string EnterInterior = "enter_interior";

    /// <summary>技能位的两个修饰键。手柄上是左右扳机，键鼠上不用（六个键一一对应）。</summary>
    public const string SkillGroupLeft = "skill_group_left";
    public const string SkillGroupRight = "skill_group_right";

    /// <summary>技能位，顺序就是编号。</summary>
    public static readonly IReadOnlyList<string> Skills =
        ["skill_1", "skill_2", "skill_3", "skill_4", "skill_5", "skill_6"];

    /// <summary>
    /// 随身栏那几格，顺序就是编号。格数与技能位同一个数，理由在
    /// <see cref="CarrySlotBar.SlotCount"/>。
    /// </summary>
    /// <remarks>
    /// 键鼠上它们就是那一排数字键。那一排是技能位挪到修饰键方案之后腾出来的，当时就留着给一条
    /// 快捷道具栏，这就是那条道具栏。
    ///
    /// 手柄上它们一个都没有绑定，那是登记过的豁免（见 <see cref="InputBindings.Exemptions"/>）。
    /// 手柄上的键已经很紧，改用 <see cref="CarryPrev"/> 和 <see cref="CarryNext"/> 挪选中位。
    /// </remarks>
    public static readonly IReadOnlyList<string> CarrySlots =
        ["carry_1", "carry_2", "carry_3", "carry_4", "carry_5", "carry_6"];

    /// <summary>把随身栏的选中位往前、往后挪一格。手柄那一侧用这两个。</summary>
    /// <remarks>
    /// 手柄上没给每格各一个键，因为凑不出那么多空位：两个扳机当了技能位的修饰键，四个面键、
    /// 两个肩键和左摇杆按压也都有归属。挪选中位只要两个键，代价是切到最远那一格要按几下
    /// （会绕一圈，见 <see cref="CarrySlotBar.SelectNext"/>），而经营侧不赶时间。
    ///
    /// 被放弃的两种做法：按住某个键加右摇杆直选（格数映射八方向会留下空位，而空位按下去没反应）、
    /// 加第三个扳机态（「我在哪一态」要常驻提示，又占屏）。
    ///
    /// 键鼠上这两个动作没有绑定，那是登记过的豁免：那边数字键直接对应，不需要挪。
    /// </remarks>
    public const string CarryPrev = "carry_prev";
    public const string CarryNext = "carry_next";

    /// <summary>一组修饰键覆盖几个技能位。三个面键乘两个修饰键，正好铺满全部技能位。</summary>
    public const int SkillsPerGroup = 3;

    /// <summary>修饰键按住时被遮住的动作，顺序就是它们在一组里对应第几个技能位。</summary>
    /// <remarks>
    /// 这一份清单同时是两件事的唯一来源：哪些动作在修饰键按住时不许触发，以及「修饰键加第 N 个
    /// 面键」对应哪个技能位。写成两份必然漂移。
    ///
    /// 顺序对应手柄面键 X、Y、A，也就是轻攻击、重攻击、跳跃。闪避不在其中：它占第四个面键并且
    /// 永远可用，因为它是唯一带无敌帧的脱身手段，最不能在任何窗口里失效。跳跃和轻重攻击在按住
    /// 修饰键的那零点几秒里失效，是这个方案本身的代价。
    /// </remarks>
    public static readonly IReadOnlyList<string> ShadowedByModifier =
        [AttackLight, AttackHeavy, Jump];

    /// <summary>本作自己定义的全部动作。引擎层照它灌输入映射。</summary>
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
