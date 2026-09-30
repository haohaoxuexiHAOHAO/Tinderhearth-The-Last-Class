namespace Tinderhearth.Rules.Ui;

/// <summary>输入设备族。**按族而不是按具体设备** —— 键盘与鼠标一起用，提示图标也一起换。</summary>
public enum InputDeviceKind
{
    /// <summary>键盘与鼠标。</summary>
    KeyboardMouse,

    /// <summary>手柄。</summary>
    Gamepad,
}

/// <summary>
/// 一个可绑定的物理位。**规则层不认引擎的枚举值，只认这些名字。**
/// </summary>
/// <remarks>
/// 为什么不直接存 Godot 的枚举整数：那些数字写错不会报错，只会表现为「绑到了别的键」。规则层
/// 又不许引用 GodotSharp（[ADR-0007] 的程序集边界），抄一份整数进来等于把编译器的帮助换成
/// 一串魔法数。改成符号之后，翻译成引擎事件那一步在引擎层做，拼错当场编译失败。
///
/// 面键刻意用引擎自己的**布局中立**叫法（`as_text()` 自报 `Bottom/Right/Left/Top Action`），
/// 不用 A／B／X／Y —— 任天堂手柄的 A 与 B 位置与 Xbox 相反，按字母命名迟早绑反。
/// </remarks>
public enum InputSymbol
{
    // ── 键盘。取物理键位，见 InputBindings 的说明 ──
    KeyW, KeyA, KeyS, KeyD,
    KeyQ, KeyE, KeyF,
    KeyJ, KeyK,
    KeySpace, KeyShift,

    // 六个技能键。**顺序即技能位编号**（第 1 个到第 6 个），而编号按**键盘从左到右**排：
    //
    //     上排          Y   U   I   O
    //     中排        H   J   K   L
    //                     ↑   ↑
    //                   轻击 重击
    //
    // 编号跟着键盘走而不是跟着「哪个键好按」走，理由是它把「哪个位好按」这个判断留给玩家 ——
    // 技能位只是一个格子，玩家会自己把最常用的技能装到手顺的那一格。界面上技能栏从左到右、
    // 键也从左到右，看一眼就知道按哪个，零记忆负担。按「哪个键好按」重排编号等于先替他假设了
    // 「技能位 1 最常用」，而那条假设没有依据。
    //
    KeyH, KeyY, KeyU, KeyI, KeyO, KeyL,

    // 数字键那一排，六个随身栏格子各一个（`UI-34`）。
    //
    // **它们是先被删掉、再回来的。** 技能位挪到右手时（`UI-14`）这六个符号连同绑定一起删了，
    // 理由是「留着等于留六个看起来可绑的位」；而那份提案同时写明这一排**刻意空着、留给将来真要
    // 一条快捷道具栏**。随身栏就是那条道具栏，所以它们按那句话回来了。
    //
    // **它们在这里不再是「要抬手才够得到」那个缺点。** 那个缺点是对技能说的 —— 技能每几秒就要
    // 按一次，还要在按住 WASD 的时候按。换随身的那一格是低频动作，抬一次手是它付得起的价。
    Digit1, Digit2, Digit3, Digit4, Digit5, Digit6,

    // 鼠标左键：**经营侧那些逐格动作用它执行**（锄、播、浇、收，将来还有建造摆位）。
    // 理由是当前操作格在键鼠上本来就取「指针所在那一格」——指针已经在指格子，执行也用鼠标
    // 才不用两只手换位。
    //
    // **侧视战斗仍然不用鼠标**：那边没有瞄准，鼠标提供不了它擅长的精确指向，却要占住右手
    // 不让它分担按键，所以轻重攻击照旧取 J／K。经营侧不存在这个顾虑——那里没有连段。
    MouseLeft,

    // 鼠标右键：**点一下建筑的门，进或出它的室内**（`ADR-0027`）。
    // 它为什么不跟左键挤在一个动作里：左键已经是逐格动作的执行位，把门塞进同一次点击就要回答
    // 「这一格既是门又是一格地时点的是哪个」，而那个问题没有好答案。**于是经营侧两件最常做的事
    // 各占一个鼠标键**：左键对格子、右键对门，两只手一次都不用换位。
    MouseRight,

    // ── 手柄按钮 ──
    PadFaceBottom, PadFaceRight, PadFaceLeft, PadFaceTop,
    PadShoulderLeft, PadShoulderRight,
    PadStickLeftClick,

    // D-pad 左右：**挪随身栏的选中位**（`UI-34`）。
    //
    // ⚠️ **这一对与一条还没落地的预定重叠**：移动那一段的注释与那份键位提案都写着「D-pad 留给
    // 正典的队友三指令（集火／撤退／待命）」。那三条指令现在**一个动作名都没有、一条绑定都没有**，
    // 所以今天不冲突；但它们落地时（`GP-36`）四个方向装不下「三条指令加两个挪位」，必须有人让一步。
    // **那一天不会静默过去**：绑定表有一条测试钉着「同一个物理位不被两个动作抢」。
    // 记账见待办台账 `UI-36`。
    //
    // **上下两向刻意不加符号**：随身栏只要两个方向，加了就是留两个看起来可绑的位（同数字键那条理由）。
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
/// 引擎为这个位自报的名字**全文**，例如 <c>Joypad Button 10 (Right Shoulder, Sony R1, Xbox RB)</c>。
/// </param>
/// <remarks>
/// <paramref name="EngineText"/> 是**第二个量具**：拿引擎读回的 `as_text()` 与这里逐条**全等**比对，
/// 「符号翻译成了错的引擎枚举」这种错才有人管 —— 光看代码是看不出 `PadTriggerRight` 成了轴 4 还是
/// 轴 5 的。原先由 `check_input_map.py` 起真引擎做这件事，那个守卫随 `ADR-0009` 删除；**现在按键
/// 绑错的验法是作者实机按一遍**，按下去没反应或触发了别的动作是立刻能发现的。
///
/// 取全文而不是片段是有意的：片段太弱。`"A"` 这种片段会在同一个动作的另一条事件文本里蒙对
/// （`Left Stick X-Axis` 里就有 A），于是绑错了也照样通过。全等比对还顺带钉住两件事：键盘绑的是
/// **物理键位**（文本带 <c>- Physical</c>），以及摇杆的**方向**（文本带 <c>Value -1.00</c>）。
///
/// 这些取值由 Godot 4.7.2 自报得到，不是照文档抄的。升引擎时引擎若改了措辞，这一列
/// 就会与实际对不上 —— 以前那会让守卫当场失败，现在没有东西会提醒，所以**升引擎后请按一遍手柄**。
/// </remarks>
public sealed record InputBinding(InputSymbol Symbol, string EngineText)
{
    /// <summary>这条绑定属于哪个设备族。**由符号推出，不单独存** —— 存了就可能与符号对不上。</summary>
    public InputDeviceKind Device => Symbol < InputSymbol.PadFaceBottom
        ? InputDeviceKind.KeyboardMouse
        : InputDeviceKind.Gamepad;
}

/// <summary>
/// 默认输入绑定表（`UI-7`）。**这里是唯一权威源**，引擎层照它灌 `InputMap`。
/// </summary>
/// <remarks>
/// **不写进 `project.godot` 的 `[input]` 段**，理由两条：两份来源必然漂移；而手柄的修饰键组合
/// 本来就写不进那一段（实测：`InputEventJoypadButton` 没有修饰键字段）。代价是编辑器的 Input Map
/// 面板会是空的 —— 原先由 `check_input_map.py` 补执行体（核对实际 `InputMap` 与本表一致、并在
/// `project.godot` 冒出 `[input]` 段时判失败），那个守卫随 `ADR-0009` 删除。**于是这里剩一条纪律**：
/// 不要在编辑器的 Input Map 面板里手加动作，那会造出第二份来源，而两份来源必然漂移。
///
/// **键盘取物理键位**（`physical_keycode`）而不是字符键位：AZERTY 键盘上 WASD 的物理位置是
/// ZQSD，按字符绑会让那批玩家的移动键散开。物理位在任何布局下都是同四个键。
///
/// 改键将来覆盖的就是本表（正典设置里那一组）。**只有默认值，没有持久化** —— 改过的键是一项
/// **玩家级偏好**（设计仓 `design/存档系统.md` 的「玩家级偏好：第三样东西，不是分片」），它跨存档位、
/// 不进任何存档分片，与本表走的是同一条读写路；现在定持久化格式就是猜一套将来要改的格式。
/// </remarks>
public static class InputBindings
{
    /// <summary>
    /// 扳机当修饰键时的死区。**必须显式设，不能用引擎默认的 0.2**（已实测默认值）。
    /// </summary>
    /// <remarks>
    /// 0.2 意味着扳机压下两成就算按住 —— 手指搭在扳机上就可能误触发，而玩家看不见自己压了多深。
    /// 取一半行程，让「按住修饰键」是个有意的动作。实测：死区 0.5 时注入 0.6 判按下、0.2 判未按下。
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
            // 闪避占第四个面键，**不被修饰键遮**。键鼠给 E：WASD 手位下它是食指最快能到的键。
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
            // 交互：**经营侧以鼠标左键为主**（所以它排在前面，提示优先显示它），键盘 F 仍然可用。
            // 两个键同一个动作不是冗余：经营侧逐格动作用鼠标最顺，而战斗侧的采集、开箱与片段
            // 转场在键盘上更顺手——那边右手在 J／K 上，腾不出来去点鼠标。
            [InputActions.Interact] =
            [
                new(InputSymbol.MouseLeft, "Left Mouse Button"),
                new(InputSymbol.KeyF, "F - Physical"),
                new(InputSymbol.PadShoulderRight,
                    "Joypad Button 10 (Right Shoulder, Sony R1, Xbox RB)"),
            ],
            // 进出建筑室内：**键鼠上只有鼠标右键**（`ADR-0027`）。手柄那一侧故意不绑，理由在下面
            // 的豁免登记 —— 它没有指针，一个交互键就够，对象靠站位与朝向消歧。
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

            // ── 随身栏那六格。键鼠是数字键 1–6 一一对应；手柄靠下面两个挪位键，见豁免登记 ──
            //
            // 数字键这一排是 `UI-14` 把技能位挪到右手之后腾出来的，而那份提案写明它「刻意不补，
            // 留给将来真要一条快捷道具栏」—— 这就是那条道具栏。
            [InputActions.CarrySlots[0]] = [new(InputSymbol.Digit1, "1 - Physical")],
            [InputActions.CarrySlots[1]] = [new(InputSymbol.Digit2, "2 - Physical")],
            [InputActions.CarrySlots[2]] = [new(InputSymbol.Digit3, "3 - Physical")],
            [InputActions.CarrySlots[3]] = [new(InputSymbol.Digit4, "4 - Physical")],
            [InputActions.CarrySlots[4]] = [new(InputSymbol.Digit5, "5 - Physical")],
            [InputActions.CarrySlots[5]] = [new(InputSymbol.Digit6, "6 - Physical")],

            // 挪选中位：**只有手柄需要**。键鼠那边六个数字键直接对应，不用挪。
            // 绕回而不是钳住，所以两个键就够遍历六格（规则在 `CarrySlotBar.SelectNext`）。
            [InputActions.CarryPrev] =
            [
                new(InputSymbol.PadDpadLeft, "Joypad Button 13 (D-pad Left)"),
            ],
            [InputActions.CarryNext] =
            [
                new(InputSymbol.PadDpadRight, "Joypad Button 14 (D-pad Right)"),
            ],

            // ── 6 个技能位。**键鼠上六个键与六个槽位从左到右一一对应，没有修饰键这一层**；
            // 手柄由组合解算发出，见下面的豁免登记 ──
            //
            // 六个键全在 J（轻击）与 K（重击）**一手之内**，右手不离位就按得到。挪过来的理由是
            // 原先那一排数字键在 WASD 手位下要抬手：左手五个指头全占在移动与冲刺上，要放技能就得
            // 让食指或中指离开 WASD，而战斗里那一瞬间正是要躲的时候。
            //
            // ⚠️ **食指要同时管轻击 J 与三个技能键**（H、Y、U），所以连段里插技能会抢食指 ——
            // 中指（K 与 I）与无名指（L 与 O）各只管两个，压力小得多。这一条几何上看得出来，
            // 手感上看不出来，所以它是作者实机按那一遍的重点。
            [InputActions.Skills[0]] = [new(InputSymbol.KeyH, "H - Physical")],
            [InputActions.Skills[1]] = [new(InputSymbol.KeyY, "Y - Physical")],
            [InputActions.Skills[2]] = [new(InputSymbol.KeyU, "U - Physical")],
            [InputActions.Skills[3]] = [new(InputSymbol.KeyI, "I - Physical")],
            [InputActions.Skills[4]] = [new(InputSymbol.KeyO, "O - Physical")],
            [InputActions.Skills[5]] = [new(InputSymbol.KeyL, "L - Physical")],
        };

    /// <summary>
    /// 补给引擎内置界面动作的手柄绑定。**不是重定义，是补齐引擎默认值缺的那一半。**
    /// </summary>
    /// <remarks>
    /// 从引擎自报的清单实测：Godot 4.7.2 的默认 `InputMap` 给
    /// `ui_up`／`ui_down`／`ui_left`／`ui_right` **各有两条手柄事件**（D-pad 加左摇杆），但
    /// `ui_accept` 只有 Enter／小键盘 Enter／Space，`ui_cancel` 只有 Escape —— **手柄上一条都没有**。
    ///
    /// 后果是具体的：手柄能把焦点挪来挪去，却**按不下去也退不出来**，而 `UiRoot` 的返回键正是
    /// `ui_cancel`。所以「面板导航在手柄上可用」这条验收不补这两条就不成立。这不是猜的引擎行为，
    /// 是守卫先判失败才发现的。
    ///
    /// 取下面键确认、右面键返回，是三大平台手柄的共同约定（引擎自报名里 0 号是 `Bottom Action`、
    /// 1 号是 `Right Action`，正是布局中立的那两个位）。
    ///
    /// **已知重叠**：下面键同时是跳跃、右面键同时是闪避。面板里由拿到焦点的控件消费 `ui_accept`，
    /// 但**轮询不受消费影响**（见 <see cref="SkillModifierState.ShouldSuppress"/> 的实测），所以
    /// 「不暂停的面板打开时该不该屏蔽玩法动作」是个真问题。它跨 `UI-6` 的导航栈与本条，且正确行为
    /// 不显然（背包不暂停世界，那时跳跃该不该还能按？），所以**不在本条顺手定**，已记 `UI-11`。
    /// </remarks>
    public static readonly IReadOnlyDictionary<string, IReadOnlyList<InputBinding>> BuiltinUiPatches =
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

    /// <summary>
    /// **故意**没有某个设备族绑定的动作，连理由一起登记。
    /// </summary>
    /// <remarks>
    /// 为什么要有这份登记：不登记的话「忘了绑手柄」与「手柄上故意不绑」长得一模一样，而前者是
    /// 缺陷、后者是设计。有了它，缺绑定就是失败，故意不绑要写出理由 —— 这条由测试盯着。
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
            // 键鼠上六个技能位各有自己的键（H Y U I O L），所以不需要修饰键这一层。
            // 手柄上要修饰键是因为面键只有四个、铺不下六个位。
            [(InputActions.SkillGroupLeft, InputDeviceKind.KeyboardMouse)] =
                "键鼠上六个技能键一一对应六个技能位，不需要修饰键",
            [(InputActions.SkillGroupRight, InputDeviceKind.KeyboardMouse)] =
                "键鼠上六个技能键一一对应六个技能位，不需要修饰键",
            [(InputActions.EnterInterior, InputDeviceKind.Gamepad)] =
                "手柄没有指针，进门与其余交互点共用交互键，对象靠站位与朝向消歧（ADR-0027）",

            // 随身栏两侧各缺一半，理由相反：键鼠有六个空键所以直选，手柄没有所以挪位。
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
        "手柄上凑不出六个空位（扳机当修饰键、四个面键与两个肩键都有归属），改用 D-pad 左右挪选中位";

    /// <summary>键鼠上为什么不需要挪位键。</summary>
    private const string CarryKeyWhy = "键鼠上六个数字键直接对应六格，不需要挪";

    /// <summary>取某个动作在某个设备族上的绑定。</summary>
    public static IReadOnlyList<InputBinding> For(string action, InputDeviceKind device) =>
        Table.TryGetValue(action, out var list)
            ? [.. list.Where(b => b.Device == device)]
            : throw new KeyNotFoundException($"绑定表里没有这个动作：{action}");

    /// <summary>取某个动作的死区，没登记就返回 <c>null</c>（用引擎默认值）。</summary>
    public static float? DeadzoneFor(string action) =>
        Deadzones.TryGetValue(action, out var v) ? v : null;
}
