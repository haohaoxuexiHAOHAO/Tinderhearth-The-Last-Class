using Godot;
using Tinderhearth.Rules.Ui;

namespace Tinderhearth.UI;

/// <summary>
/// 把规则层的默认绑定表灌进引擎的 `InputMap`（`UI-7`）。**符号翻译成引擎事件只在这一处发生。**
/// </summary>
/// <remarks>
/// 为什么默认值走代码而不是 `project.godot` 的 `[input]` 段：两份来源必然漂移，而且手柄的修饰键
/// 组合本来就写不进那一段 —— 2026-08-30 实测 `InputEventJoypadButton` 与 `InputEventJoypadMotion`
/// 直接继承 `InputEvent`，一个修饰键字段都没有（`InputEventKey` 有四个）。
///
/// 代价是编辑器的 Input Map 面板会是空的，执行体是 `tools/check_input_map.py`：它起真引擎核对
/// `InputMap` 实际内容与本表一致，并且**发现 `project.godot` 冒出 `[input]` 段就判失败** ——
/// 有人在编辑器里手加一个动作就是造出了第二份来源，而那种漂移不会报错。
///
/// <see cref="ToEvent"/> 是**编译期受保护的那一半**：符号是枚举，翻译写错枚举名编不过。它翻译得
/// 对不对（`PadTriggerRight` 到底成了轴 4 还是轴 5）编译器管不了，所以每条绑定带一个引擎自报名的
/// 片段，由守卫读回 `as_text()` 比对。
/// </remarks>
public static class InputMapInstaller
{
    /// <summary>
    /// 建立本作的全部动作与默认绑定。已存在的同名动作会被先擦掉再建。
    /// </summary>
    /// <remarks>
    /// 先擦再建而不是「有就跳过」：跳过的话第二次调用会静默保留上一次的绑定，将来改键做「恢复
    /// 默认」时就会得到两套叠在一起的绑定。**擦掉只擦本作自己的动作**，引擎内置的 `ui_*` 不动 ——
    /// 界面导航用的就是它们（`UiRoot` 在用 `ui_cancel`），重定义一套只会多一份要维护的东西。
    /// </remarks>
    public static void Install()
    {
        foreach (var action in InputActions.All)
        {
            if (InputMap.HasAction(action))
            {
                InputMap.EraseAction(action);
            }

            // 死区没登记就用引擎默认值，不在这里再写一个 0.2 出来。
            if (InputBindings.DeadzoneFor(action) is float deadzone)
            {
                InputMap.AddAction(action, deadzone);
            }
            else
            {
                InputMap.AddAction(action);
            }

            foreach (var binding in InputBindings.Table[action])
            {
                InputMap.ActionAddEvent(action, ToEvent(binding.Symbol));
            }
        }

        PatchBuiltinUiActions();
    }

    /// <summary>
    /// 给引擎内置的界面动作补上缺的手柄绑定。
    /// </summary>
    /// <remarks>
    /// **只追加，绝不先擦。** 擦掉会把 `ui_accept` 的 Enter／Space 与 `ui_cancel` 的 Escape 一起
    /// 带走，键鼠玩家就退不出面板了 —— 而那是「修好手柄反而弄坏键鼠」的典型形状。
    ///
    /// 为什么必须补：实测 Godot 4.7.2 的默认值里 `ui_accept` 与 `ui_cancel` **一条手柄事件都没有**
    /// （方向那四个有）。理由与已知重叠写在 <see cref="InputBindings.BuiltinUiPatches"/>。
    /// </remarks>
    private static void PatchBuiltinUiActions()
    {
        foreach (var (action, bindings) in InputBindings.BuiltinUiPatches)
        {
            if (!InputMap.HasAction(action))
            {
                throw new InvalidOperationException(
                    $"引擎里没有内置动作 {action} —— 补丁的前提不成立，别静默跳过");
            }

            foreach (var binding in bindings)
            {
                var e = ToEvent(binding.Symbol);
                if (!InputMap.ActionHasEvent(action, e))
                {
                    InputMap.ActionAddEvent(action, e);
                }
            }
        }
    }

    /// <summary>
    /// 把一个物理位符号翻译成引擎事件。
    /// </summary>
    /// <remarks>
    /// **键盘一律绑物理键位**（<c>PhysicalKeycode</c>）而不是字符键位：AZERTY 键盘上 WASD 的物理
    /// 位置是 ZQSD，按字符绑会让那批玩家的移动键散开成四个不相邻的键。物理位在任何布局下都是同
    /// 四个键。副作用是好的 —— 将来的改键界面显示的是玩家键盘上实际印着的字。
    ///
    /// 摇杆四向用同一个轴的正负两个方向各一条事件；扳机只取正方向（它从 0 压到 1，没有负半轴）。
    /// </remarks>
    public static InputEvent ToEvent(InputSymbol symbol) => symbol switch
    {
        InputSymbol.KeyW => Key(Godot.Key.W),
        InputSymbol.KeyA => Key(Godot.Key.A),
        InputSymbol.KeyS => Key(Godot.Key.S),
        InputSymbol.KeyD => Key(Godot.Key.D),
        InputSymbol.KeyQ => Key(Godot.Key.Q),
        InputSymbol.KeyE => Key(Godot.Key.E),
        InputSymbol.KeyF => Key(Godot.Key.F),
        InputSymbol.KeyJ => Key(Godot.Key.J),
        InputSymbol.KeyK => Key(Godot.Key.K),
        InputSymbol.KeySpace => Key(Godot.Key.Space),
        // 左右 Shift 都算：绑成通用 Shift 而不是限定左键，玩家用哪只手都行。
        InputSymbol.KeyShift => Key(Godot.Key.Shift),
        InputSymbol.Digit1 => Key(Godot.Key.Key1),
        InputSymbol.Digit2 => Key(Godot.Key.Key2),
        InputSymbol.Digit3 => Key(Godot.Key.Key3),
        InputSymbol.Digit4 => Key(Godot.Key.Key4),
        InputSymbol.Digit5 => Key(Godot.Key.Key5),
        InputSymbol.Digit6 => Key(Godot.Key.Key6),

        // 面键用引擎的布局中立编号：0 是下、1 是右、2 是左、3 是上（2026-08-30 从 as_text() 读回）。
        InputSymbol.PadFaceBottom => Pad(JoyButton.A),
        InputSymbol.PadFaceRight => Pad(JoyButton.B),
        InputSymbol.PadFaceLeft => Pad(JoyButton.X),
        InputSymbol.PadFaceTop => Pad(JoyButton.Y),
        InputSymbol.PadShoulderLeft => Pad(JoyButton.LeftShoulder),
        InputSymbol.PadShoulderRight => Pad(JoyButton.RightShoulder),
        InputSymbol.PadStickLeftClick => Pad(JoyButton.LeftStick),

        InputSymbol.PadStickLeftXMinus => Axis(JoyAxis.LeftX, -1f),
        InputSymbol.PadStickLeftXPlus => Axis(JoyAxis.LeftX, 1f),
        InputSymbol.PadStickLeftYMinus => Axis(JoyAxis.LeftY, -1f),
        InputSymbol.PadStickLeftYPlus => Axis(JoyAxis.LeftY, 1f),
        InputSymbol.PadTriggerLeft => Axis(JoyAxis.TriggerLeft, 1f),
        InputSymbol.PadTriggerRight => Axis(JoyAxis.TriggerRight, 1f),

        // 新增符号却忘了翻译时当场抛，而不是静默漏一条绑定 —— 漏掉的表现是「这个键没反应」。
        _ => throw new ArgumentOutOfRangeException(
            nameof(symbol), $"没有为这个物理位写翻译：{symbol}"),
    };

    private static InputEventKey Key(Key key) => new() { PhysicalKeycode = key };

    private static InputEventJoypadButton Pad(JoyButton button) => new() { ButtonIndex = button };

    private static InputEventJoypadMotion Axis(JoyAxis axis, float value) =>
        new() { Axis = axis, AxisValue = value };
}
