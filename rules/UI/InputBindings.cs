namespace Tinderhearth.Rules.UI;

/// <summary>输入设备族。按族分而不是按具体设备：键盘和鼠标一起用，提示图标也一起换。</summary>
public enum InputDeviceKind
{
    /// <summary>键盘与鼠标。</summary>
    KeyboardMouse,

    /// <summary>手柄。</summary>
    Gamepad,
}

/// <summary>一个可绑定的物理位。规则层不认引擎的枚举值，只认这些名字。</summary>
/// <remarks>
/// 不直接存 Godot 的枚举整数，因为那些数字写错不报错，只表现为「绑到了别的键」。而规则层不许
/// 引用 GodotSharp，抄一份整数进来等于把编译器的帮助换成一串魔法数。用符号之后，翻译成引擎
/// 事件那一步在引擎层做，拼错当场编译失败。
///
/// 面键用引擎自己那套与布局无关的叫法（引擎自报的是 Bottom、Right、Left、Top Action），不用
/// A、B、X、Y。任天堂手柄的 A 和 B 位置与 Xbox 相反，按字母命名迟早绑反。
/// </remarks>
public enum InputSymbol
{
    // ── 键盘。取物理键位，见 InputBindings 的说明 ──
    KeyW, KeyA, KeyS, KeyD,
    KeyQ, KeyE, KeyF,
    KeyJ, KeyK,
    KeySpace, KeyShift,

    // 技能键。这里的顺序就是技能位的编号，而编号按键盘从左到右排：
    //
    //     上排          Y   U   I   O
    //     中排        H   J   K   L
    //                     ↑   ↑
    //                   轻击 重击
    //
    // 编号跟着键盘走而不是跟着「哪个键好按」走，这样「哪个位好按」这个判断留给玩家：技能位只是
    // 一个格子，他会自己把最常用的技能装到手顺的那一格。界面上技能栏从左到右、键也从左到右，
    // 看一眼就知道按哪个。按「哪个键好按」重排编号等于先替他假设了「第一个技能位最常用」。
    KeyH, KeyY, KeyU, KeyI, KeyO, KeyL,

    // 数字键那一排，随身栏每格一个。
    //
    // 这一排是技能位挪到右手时腾出来的，当时就留着给一条快捷道具栏，随身栏就是那条道具栏。
    // 「要抬手才够得到」这个缺点在这里不成立：那是对技能说的，技能每几秒就要按一次、还要在
    // 按住 WASD 的时候按；换随身的那一格是低频动作，抬一次手付得起。
    Digit1, Digit2, Digit3, Digit4, Digit5, Digit6,

    // 鼠标左键：经营侧那些逐格动作用它执行（锄、播、浇、收，将来还有建造摆位）。当前操作格在
    // 键鼠上本来就取指针所在那一格，指针已经在指格子，执行也用鼠标才不用两只手换位。
    //
    // 侧视战斗仍然不用鼠标：那边没有瞄准，鼠标提供不了它擅长的精确指向，却要占住右手不让它分担
    // 按键，所以轻重攻击照旧取 J 和 K。经营侧没有连段，不存在这个顾虑。
    MouseLeft,

    // 鼠标右键：点一下建筑的门，进或出它的室内。
    //
    // 它不跟左键挤在一个动作里，因为左键已经是逐格动作的执行位，塞进同一次点击就要回答「这一格
    // 既是门又是一格地时点的是哪个」。于是经营侧两件最常做的事各占一个鼠标键：左键对格子、
    // 右键对门，两只手一次都不用换位。
    MouseRight,

    // ── 手柄按钮 ──
    PadFaceBottom, PadFaceRight, PadFaceLeft, PadFaceTop,
    PadShoulderLeft, PadShoulderRight,
    PadStickLeftClick,

    // D-pad 左右：挪随身栏的选中位。
    //
    // 这一对和一条还没落地的预定重叠：D-pad 原本留给队友的三条指令（集火、撤退、待命）。那三条
    // 现在一个动作名都没有、一条绑定都没有，所以今天不冲突；但它们落地时，四个方向装不下「三条
    // 指令加两个挪位」，必须有人让一步。那一天不会静默过去，绑定表有一条测试钉着「同一个物理位
    // 不被两个动作抢」。
    //
    // 上下两向不加符号：随身栏只要两个方向，加了就是留两个看起来可绑的位。
    PadDpadLeft, PadDpadRight,

    // ── 手柄轴。左摇杆四向 + 两个扳机 ──
    PadStickLeftXMinus, PadStickLeftXPlus,
    PadStickLeftYMinus, PadStickLeftYPlus,
    PadTriggerLeft, PadTriggerRight,
}

/// <summary>
/// 一条默认绑定。
/// </summary>
/// <param name="Symbol">绑到哪个物理位。</param>
/// <param name="EngineText">
/// 引擎为这个位自报的名字全文，例如 <c>Joypad Button 10 (Right Shoulder, Sony R1, Xbox RB)</c>。
/// </param>
/// <remarks>
/// <paramref name="EngineText"/> 是一把对照用的尺：拿引擎读回的名字与这里逐条全等比对，就能查出
/// 「符号翻译成了错的引擎枚举」这种错。光看代码看不出 <c>PadTriggerRight</c> 成了轴 4 还是轴 5。
/// 现在没有自动核对它的入口，所以按键绑错的验法是作者实机按一遍，按下去没反应或触发了别的动作
/// 是立刻能发现的。
///
/// 要全文而不是片段，因为片段太弱：「A」这种片段会在同一个动作的另一条事件文本里蒙对
/// （<c>Left Stick X-Axis</c> 里就有 A），绑错了也照样通过。全等比对还顺带钉住两件事：键盘绑的是
/// 物理键位（文本带 <c>- Physical</c>），以及摇杆的方向（文本带 <c>Value -1.00</c>）。
///
/// 这一列是从 Godot 4.7.2 自报的名字抄回来的，不是照文档写的。升引擎时引擎若改了措辞，这一列
/// 就会与实际对不上，而现在没有东西会提醒，所以升引擎之后请按一遍手柄。
/// </remarks>
public sealed record InputBinding(InputSymbol Symbol, string EngineText)
{
    /// <summary>这条绑定属于哪个设备族。由符号推出，不单独存，存了就可能与符号对不上。</summary>
    public InputDeviceKind Device => Symbol < InputSymbol.PadFaceBottom
        ? InputDeviceKind.KeyboardMouse
        : InputDeviceKind.Gamepad;
}

/// <summary>默认输入绑定表。这里是唯一的来源，引擎层照它灌输入映射。</summary>
/// <remarks>
/// 它不写进 <c>project.godot</c> 的 <c>[input]</c> 段，理由有两条：两份来源必然漂移；手柄的修饰键
/// 组合本来就写不进那一段（实测 <c>InputEventJoypadButton</c> 没有修饰键字段）。代价是编辑器的
/// Input Map 面板会是空的，而现在没有东西拦着往那里手加动作，所以这里剩一条纪律：不要在那个面板
/// 里加动作，那会造出第二份来源。
///
/// 键盘绑的是物理键位而不是字符键位。AZERTY 键盘上 WASD 的物理位置是 ZQSD，按字符绑会让那批
/// 玩家的移动键散开；物理位在任何布局下都是同四个键。
///
/// 将来玩家改键覆盖的就是本表。这里只有默认值、没有持久化：改过的键是一项跨存档位的玩家偏好
/// （设计仓 design/存档系统.md 的「玩家级偏好：第三样东西，不是分片」一节），现在定持久化格式就是
/// 猜一套将来要改的格式。
/// </remarks>
public static class InputBindings
{
    /// <summary>扳机当修饰键时的死区，取行程的一半。必须显式设，不能用引擎默认值。</summary>
    /// <remarks>
    /// 引擎默认 0.2（实测过），意味着扳机压下两成就算按住，手指搭在上面就可能误触发，而玩家看不见
    /// 自己压了多深。取一半行程，让「按住修饰键」是个有意的动作。实测：死区设 0.5 时，注入 0.6
    /// 判按下、注入 0.2 判未按下。
    /// </remarks>
    public const float TriggerDeadzone = 0.5f;

    // 摇杆两轴的自报名前缀，四个方向共用，抄四遍必然有一遍抄错。
    private const string StickLeftX = "Joypad Motion on Axis 0 (Left Stick X-Axis, Joystick 0 X-Axis)";
    private const string StickLeftY = "Joypad Motion on Axis 1 (Left Stick Y-Axis, Joystick 0 Y-Axis)";

    /// <summary>默认绑定。一个动作可以有多条，同一动作的多条必须属于不同设备族或不同物理位。</summary>
    public static readonly IReadOnlyDictionary<string, IReadOnlyList<InputBinding>> Table =
        new Dictionary<string, IReadOnlyList<InputBinding>>
        {
            // ── 移动。手柄只绑左摇杆：D-pad 要留给正典的队友三指令（集火／撤退／待命）──
            [InputActions.MoveLeft] =
            [
                new(InputSymbol.KeyA, "A - Physical"),
                new(InputSymbol.PadStickLeftXMinus, StickLeftX + " with Value -1.00"),
            ],
            [InputActions.MoveRight] =
            [
                new(InputSymbol.KeyD, "D - Physical"),
                new(InputSymbol.PadStickLeftXPlus, StickLeftX + " with Value 1.00"),
            ],
            [InputActions.MoveUp] =
            [
                new(InputSymbol.KeyW, "W - Physical"),
                new(InputSymbol.PadStickLeftYMinus, StickLeftY + " with Value -1.00"),
            ],
            [InputActions.MoveDown] =
            [
                new(InputSymbol.KeyS, "S - Physical"),
                new(InputSymbol.PadStickLeftYPlus, StickLeftY + " with Value 1.00"),
            ],

            // 冲刺：键鼠取 Shift（PC 上「按住加速」的通用位），手柄取左摇杆按压（业界的
            // 疾跑位，且它在本方案里空着）。两个设备都有绑定，不留「一边绑不到」的例外。
            // Shift 的自报名刻意没有 - Physical 后缀，修饰键在引擎里就是这么打的。
            [InputActions.Run] =
            [
                new(InputSymbol.KeyShift, "Shift"),
                new(InputSymbol.PadStickLeftClick, "Joypad Button 7 (Left Stick, Sony L3, Xbox L/LS)"),
            ],

            // ── 战斗六动作 ──
            // 轻重攻击给 J 与 K，不用鼠标左右键。两条理由：侧视战斗没有
            // 瞄准，鼠标的长处用不上却占着右手；而 J 与 K 相邻且都是手指静止位，满足「连段要求
            // 轻重攻击相邻且都快」。这是 2D 横版动作游戏的通行布局，玩这类游戏的人有肌肉记忆。
            [InputActions.AttackLight] =
            [
                new(InputSymbol.KeyJ, "J - Physical"),
                new(InputSymbol.PadFaceLeft,
                    "Joypad Button 2 (Left Action, Sony Square, Xbox X, Nintendo Y)"),
            ],
            [InputActions.AttackHeavy] =
            [
                new(InputSymbol.KeyK, "K - Physical"),
                new(InputSymbol.PadFaceTop,
                    "Joypad Button 3 (Top Action, Sony Triangle, Xbox Y, Nintendo X)"),
            ],
            // 防御给数字肩键而不是扳机：正典的精准防御要求帧级准确，扳机有行程，
            // 触发点不一致。这正是修饰键让给扳机的原因。
            [InputActions.Guard] =
            [
                new(InputSymbol.KeyQ, "Q - Physical"),
                new(InputSymbol.PadShoulderLeft,
                    "Joypad Button 9 (Left Shoulder, Sony L1, Xbox LB)"),
            ],
            // 闪避占第四个面键，不被修饰键遮。键鼠给 E：WASD 手位下它是食指最快能到的键。
            [InputActions.Dodge] =
            [
                new(InputSymbol.KeyE, "E - Physical"),
                new(InputSymbol.PadFaceRight,
                    "Joypad Button 1 (Right Action, Sony Circle, Xbox B, Nintendo A)"),
            ],
            [InputActions.Jump] =
            [
                new(InputSymbol.KeySpace, "Space - Physical"),
                new(InputSymbol.PadFaceBottom,
                    "Joypad Button 0 (Bottom Action, Sony Cross, Xbox A, Nintendo B)"),
            ],
            // 交互以鼠标左键为主，所以它排在前面，按键提示优先显示它；键盘 F 仍然可用。
            // 两个键绑同一个动作不是冗余：经营侧逐格动作用鼠标最顺，而战斗侧的采集、开箱与片段
            // 转场在键盘上更顺手，那边右手在 J 和 K 上，腾不出来去点鼠标。
            [InputActions.Interact] =
            [
                new(InputSymbol.MouseLeft, "Left Mouse Button"),
                new(InputSymbol.KeyF, "F - Physical"),
                new(InputSymbol.PadShoulderRight,
                    "Joypad Button 10 (Right Shoulder, Sony R1, Xbox RB)"),
            ],
            // 进出建筑室内：键鼠上只有鼠标右键。手柄那一侧故意不绑，理由在下面的豁免登记里，
            // 它没有指针，一个交互键就够，对象靠站位与朝向区分。
            [InputActions.EnterInterior] =
            [
                new(InputSymbol.MouseRight, "Right Mouse Button"),
            ],

            // ── 两个修饰键。只有手柄需要 ──
            // 注意扳机的自报名里带着 Joystick 2 X／Y-Axis —— 引擎把两个扳机当成第三根摇杆的
            // 两个轴报，这正是「扳机是轴不是按钮」的原话，别以为是打错了。
            [InputActions.SkillGroupLeft] =
            [
                new(InputSymbol.PadTriggerLeft,
                    "Joypad Motion on Axis 4 (Joystick 2 X-Axis, Left Trigger, Sony L2, Xbox LT)"
                    + " with Value 1.00"),
            ],
            [InputActions.SkillGroupRight] =
            [
                new(InputSymbol.PadTriggerRight,
                    "Joypad Motion on Axis 5 (Joystick 2 Y-Axis, Right Trigger, Sony R2, Xbox RT)"
                    + " with Value 1.00"),
            ],

            // ── 随身栏那几格。键鼠是数字键一一对应；手柄靠下面两个挪位键，见豁免登记 ──
            //
            // 数字键这一排是技能位挪到右手之后腾出来的，当时就留着给一条快捷道具栏。
            [InputActions.CarrySlots[0]] = [new(InputSymbol.Digit1, "1 - Physical")],
            [InputActions.CarrySlots[1]] = [new(InputSymbol.Digit2, "2 - Physical")],
            [InputActions.CarrySlots[2]] = [new(InputSymbol.Digit3, "3 - Physical")],
            [InputActions.CarrySlots[3]] = [new(InputSymbol.Digit4, "4 - Physical")],
            [InputActions.CarrySlots[4]] = [new(InputSymbol.Digit5, "5 - Physical")],
            [InputActions.CarrySlots[5]] = [new(InputSymbol.Digit6, "6 - Physical")],

            // 挪选中位：只有手柄需要，键鼠那边数字键直接对应。走到头绕回而不是钳住，所以两个键
            // 就够遍历全部格子（规则在 <c>CarrySlotBar.SelectNext</c>）。
            [InputActions.CarryPrev] =
            [
                new(InputSymbol.PadDpadLeft, "Joypad Button 13 (D-pad Left)"),
            ],
            [InputActions.CarryNext] =
            [
                new(InputSymbol.PadDpadRight, "Joypad Button 14 (D-pad Right)"),
            ],

            // ── 技能位。键鼠上每个键对一个槽位、没有修饰键这一层；手柄由组合解算发出，
            // 见下面的豁免登记 ──
            //
            // 这些键全在 J（轻击）和 K（重击）一手之内，右手不离位就按得到。挪过来的理由是原先
            // 那一排数字键在 WASD 手位下要抬手：左手五个指头全占在移动和奔跑上，要放技能就得让
            // 食指或中指离开 WASD，而战斗里那一瞬间正是要躲的时候。
            //
            // 代价是食指要同时管轻击 J 和三个技能键（H、Y、U），所以连段里插技能会抢食指；中指
            // （K 和 I）与无名指（L 和 O）各只管两个，压力小得多。这一条几何上看得出来、手感上
            // 看不出来，所以它是作者实机按那一遍的重点。
            [InputActions.Skills[0]] = [new(InputSymbol.KeyH, "H - Physical")],
            [InputActions.Skills[1]] = [new(InputSymbol.KeyY, "Y - Physical")],
            [InputActions.Skills[2]] = [new(InputSymbol.KeyU, "U - Physical")],
            [InputActions.Skills[3]] = [new(InputSymbol.KeyI, "I - Physical")],
            [InputActions.Skills[4]] = [new(InputSymbol.KeyO, "O - Physical")],
            [InputActions.Skills[5]] = [new(InputSymbol.KeyL, "L - Physical")],
        };

    /// <summary>补给引擎内置界面动作的手柄绑定。不是重定义，是补齐引擎默认值缺的那一半。</summary>
    /// <remarks>
    /// 实测 Godot 4.7.2 的默认输入映射：<c>ui_up</c> 这四个方向各有两条手柄事件（D-pad 加左摇杆），
    /// 但 <c>ui_accept</c> 只有回车和空格、<c>ui_cancel</c> 只有 Escape，手柄上一条都没有。后果很
    /// 具体：手柄能把焦点挪来挪去，却按不下去也退不出来。
    ///
    /// 取下面键确认、右面键返回，是三大平台手柄的共同约定（引擎自报名里 0 号是 Bottom Action、
    /// 1 号是 Right Action，正是与布局无关的那两个位）。
    ///
    /// 已知重叠：下面键同时是跳跃、右面键同时是闪避。面板里由拿到焦点的控件消费 <c>ui_accept</c>，
    /// 但轮询不受消费影响（见 <see cref="SkillModifierState.ShouldSuppress"/> 里那条实测），所以
    /// 「不暂停的面板打开时该不该屏蔽玩法动作」是个还没定的真问题。
    /// </remarks>
    public static readonly IReadOnlyDictionary<string, IReadOnlyList<InputBinding>> BuiltinUIPatches =
        new Dictionary<string, IReadOnlyList<InputBinding>>
        {
            ["ui_accept"] =
            [
                new(InputSymbol.PadFaceBottom,
                    "Joypad Button 0 (Bottom Action, Sony Cross, Xbox A, Nintendo B)"),
            ],
            ["ui_cancel"] =
            [
                new(InputSymbol.PadFaceRight,
                    "Joypad Button 1 (Right Action, Sony Circle, Xbox B, Nintendo A)"),
            ],
        };

    /// <summary>需要非默认死区的动作。</summary>
    public static readonly IReadOnlyDictionary<string, float> Deadzones =
        new Dictionary<string, float>
        {
            [InputActions.SkillGroupLeft] = TriggerDeadzone,
            [InputActions.SkillGroupRight] = TriggerDeadzone,
        };

    /// <summary>故意没有某个设备族绑定的动作，连理由一起登记。</summary>
    /// <remarks>
    /// 不登记的话，「忘了绑手柄」和「手柄上故意不绑」长得一模一样，而前者是缺陷、后者是设计。
    /// 有了它，缺绑定就是失败，故意不绑要写出理由，这条由测试盯着。
    /// </remarks>
    public static readonly IReadOnlyDictionary<(string Action, InputDeviceKind Device), string> Exemptions =
        new Dictionary<(string, InputDeviceKind), string>
        {
            [(InputActions.Skills[0], InputDeviceKind.Gamepad)] = "手柄上由 LT + 面键组合解算发出",
            [(InputActions.Skills[1], InputDeviceKind.Gamepad)] = "手柄上由 LT + 面键组合解算发出",
            [(InputActions.Skills[2], InputDeviceKind.Gamepad)] = "手柄上由 LT + 面键组合解算发出",
            [(InputActions.Skills[3], InputDeviceKind.Gamepad)] = "手柄上由 RT + 面键组合解算发出",
            [(InputActions.Skills[4], InputDeviceKind.Gamepad)] = "手柄上由 RT + 面键组合解算发出",
            [(InputActions.Skills[5], InputDeviceKind.Gamepad)] = "手柄上由 RT + 面键组合解算发出",
            // 键鼠上每个技能位各有自己的键（H Y U I O L），所以不需要修饰键这一层。
            // 手柄上要修饰键，是因为面键只有四个、铺不下那么多位。
            [(InputActions.SkillGroupLeft, InputDeviceKind.KeyboardMouse)] =
                "键鼠上六个技能键一一对应六个技能位，不需要修饰键",
            [(InputActions.SkillGroupRight, InputDeviceKind.KeyboardMouse)] =
                "键鼠上六个技能键一一对应六个技能位，不需要修饰键",
            [(InputActions.EnterInterior, InputDeviceKind.Gamepad)] =
                "手柄没有指针，进门与其余交互点共用交互键，对象靠站位与朝向区分",

            // 随身栏两侧各缺一半，理由相反：键鼠有一排空键所以直选，手柄没有所以挪位。
            [(InputActions.CarrySlots[0], InputDeviceKind.Gamepad)] = CarryPadWhy,
            [(InputActions.CarrySlots[1], InputDeviceKind.Gamepad)] = CarryPadWhy,
            [(InputActions.CarrySlots[2], InputDeviceKind.Gamepad)] = CarryPadWhy,
            [(InputActions.CarrySlots[3], InputDeviceKind.Gamepad)] = CarryPadWhy,
            [(InputActions.CarrySlots[4], InputDeviceKind.Gamepad)] = CarryPadWhy,
            [(InputActions.CarrySlots[5], InputDeviceKind.Gamepad)] = CarryPadWhy,
            [(InputActions.CarryPrev, InputDeviceKind.KeyboardMouse)] = CarryKeyWhy,
            [(InputActions.CarryNext, InputDeviceKind.KeyboardMouse)] = CarryKeyWhy,
        };

    /// <summary>手柄上随身栏各格为什么没有直选键。</summary>
    private const string CarryPadWhy =
        "手柄上凑不出那么多空位（扳机当修饰键、四个面键与两个肩键都有归属），改用 D-pad 左右挪选中位";

    /// <summary>键鼠上为什么不需要挪位键。</summary>
    private const string CarryKeyWhy = "键鼠上数字键直接对应每一格，不需要挪";

    /// <summary>取某个动作在某个设备族上的绑定。</summary>
    public static IReadOnlyList<InputBinding> For(string action, InputDeviceKind device) =>
        Table.TryGetValue(action, out var list)
            ? [.. list.Where(b => b.Device == device)]
            : throw new KeyNotFoundException($"绑定表里没有这个动作：{action}");

    /// <summary>取某个动作的死区，没登记就返回 <c>null</c>（用引擎默认值）。</summary>
    public static float? DeadzoneFor(string action) =>
        Deadzones.TryGetValue(action, out var v) ? v : null;
}
