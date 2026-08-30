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
    Digit1, Digit2, Digit3, Digit4, Digit5, Digit6,

    // 鼠标键刻意不在这里：本作侧视战斗**没有瞄准**，鼠标提供不了它擅长的精确指向，
    // 却要占住右手不让它分担按键（作者 2026-08-30 定，轻重攻击改 J／K）。
    // 将来建造界面用鼠标点格子、或者改键界面要给鼠标当选项时再加回来 —— 那时是加几行的事。

    // ── 手柄按钮 ──
    PadFaceBottom, PadFaceRight, PadFaceLeft, PadFaceTop,
    PadShoulderLeft, PadShoulderRight,
    PadStickLeftClick,

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
/// <paramref name="EngineText"/> 是给守卫用的**第二个量具**：`tools/check_input_map.py` 起真引擎、
/// 把绑定灌进 `InputMap`、再读回每条事件的 `as_text()` 与这里逐条**全等**比对。这样「符号翻译成了
/// 错的引擎枚举」这种错有人管 —— 光看代码是看不出 `PadTriggerRight` 成了轴 4 还是轴 5 的。
///
/// 取全文而不是片段是有意的：片段太弱。`"A"` 这种片段会在同一个动作的另一条事件文本里蒙对
/// （`Left Stick X-Axis` 里就有 A），于是绑错了也照样通过。全等比对还顺带钉住两件事：键盘绑的是
/// **物理键位**（文本带 <c>- Physical</c>），以及摇杆的**方向**（文本带 <c>Value -1.00</c>）。
///
/// 代价是升引擎时引擎改了措辞会让守卫失败。这个代价是**要的** —— 按键的显示名变了，本来就该有人
/// 看一眼；失败信息会把期望与实际并排打出来，改一行就完。这些取值 2026-08-30 由 Godot 4.7.2
/// 自报得到，不是照文档抄的。
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
/// 面板会是空的，由 `tools/check_input_map.py` 补执行体 —— 它核对实际 `InputMap` 与本表一致，
/// 并且**发现 `project.godot` 冒出 `[input]` 段就判失败**，免得有人在编辑器里手加动作造出第二份来源。
///
/// **键盘取物理键位**（`physical_keycode`）而不是字符键位：AZERTY 键盘上 WASD 的物理位置是
/// ZQSD，按字符绑会让那批玩家的移动键散开。物理位在任何布局下都是同四个键。
///
/// 改键将来覆盖的就是本表（正典设置里那一组）。**本轮只落默认值，不做持久化** —— 设置存储归
/// 设置系统，现在定格式就是猜一套将来要改的格式。
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
            [InputActions.Sprint] =
            [
                new(InputSymbol.KeyShift, "Shift"),
                new(InputSymbol.PadStickLeftClick, "Joypad Button 7 (Left Stick, Sony L3, Xbox L/LS)"),
            ],

            // ── 战斗六动作 ──
            // 轻重攻击给 J 与 K（作者 2026-08-30 定，原为鼠标左右键）。两条理由：侧视战斗没有
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
            // 触发点不一致。这正是修饰键让给扳机的原因（作者 2026-08-30 定）。
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
            [InputActions.Interact] =
            [
                new(InputSymbol.KeyF, "F - Physical"),
                new(InputSymbol.PadShoulderRight,
                    "Joypad Button 10 (Right Shoulder, Sony R1, Xbox RB)"),
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

            // ── 6 个技能位。键鼠数字键直接对应；手柄由组合解算发出，见下面的豁免登记 ──
            [InputActions.Skills[0]] = [new(InputSymbol.Digit1, "1 - Physical")],
            [InputActions.Skills[1]] = [new(InputSymbol.Digit2, "2 - Physical")],
            [InputActions.Skills[2]] = [new(InputSymbol.Digit3, "3 - Physical")],
            [InputActions.Skills[3]] = [new(InputSymbol.Digit4, "4 - Physical")],
            [InputActions.Skills[4]] = [new(InputSymbol.Digit5, "5 - Physical")],
            [InputActions.Skills[5]] = [new(InputSymbol.Digit6, "6 - Physical")],
        };

    /// <summary>
    /// 补给引擎内置界面动作的手柄绑定。**不是重定义，是补齐引擎默认值缺的那一半。**
    /// </summary>
    /// <remarks>
    /// 2026-08-30 从引擎自报的清单实测：Godot 4.7.2 的默认 `InputMap` 给
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
            [(InputActions.SkillGroupLeft, InputDeviceKind.KeyboardMouse)] =
                "键鼠上数字键直接对应六个技能位，不需要修饰键",
            [(InputActions.SkillGroupRight, InputDeviceKind.KeyboardMouse)] =
                "键鼠上数字键直接对应六个技能位，不需要修饰键",
        };

    /// <summary>取某个动作在某个设备族上的绑定。</summary>
    public static IReadOnlyList<InputBinding> For(string action, InputDeviceKind device) =>
        Table.TryGetValue(action, out var list)
            ? [.. list.Where(b => b.Device == device)]
            : throw new KeyNotFoundException($"绑定表里没有这个动作：{action}");

    /// <summary>取某个动作的死区，没登记就返回 <c>null</c>（用引擎默认值）。</summary>
    public static float? DeadzoneFor(string action) =>
        Deadzones.TryGetValue(action, out var v) ? v : null;
}
