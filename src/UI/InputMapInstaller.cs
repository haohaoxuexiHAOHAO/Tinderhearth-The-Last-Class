using Godot;
using Tinderhearth.Rules.UI;

namespace Tinderhearth.UI;

/// <summary>
/// 把规则层的默认绑定表灌进引擎的 <c>InputMap</c>。物理位符号翻译成引擎事件只在这一处发生。
/// </summary>
/// <remarks>
/// 默认绑定写在代码里而不是 <c>project.godot</c> 的 <c>[input]</c> 段：两份来源迟早对不上，而且
/// 手柄的修饰键组合本来就写不进那一段 —— 实测 <c>InputEventJoypadButton</c> 与
/// <c>InputEventJoypadMotion</c> 直接继承 <c>InputEvent</c>，一个修饰键字段都没有。
///
/// 代价是编辑器里的 Input Map 面板会是空的。不要在那里手加动作：那等于造出第二份绑定来源，
/// 而两份对不上的时候引擎不报错。
///
/// <see cref="ToEvent"/> 写错枚举名编译器会拦住，但它翻译得对不对（某个扳机到底成了哪根轴）
/// 编译器管不了，所以改完绑定请实机按一遍。
/// </remarks>
public static class InputMapInstaller
{
    /// <summary>
    /// 建立本作的全部动作与默认绑定。已存在的同名动作会被先擦掉再建。
    /// </summary>
    /// <remarks>
    /// 先擦再建而不是「有就跳过」：跳过的话第二次调用会静默留着上一次的绑定，将来做「恢复默认」
    /// 时就得到两套叠在一起的绑定。擦掉只擦本作自己的动作，引擎内置的 <c>ui_*</c> 不动 ——
    /// 界面导航用的就是它们（<see cref="UIRoot"/> 在用 <c>ui_cancel</c>）。
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

        PatchBuiltinUIActions();
    }

    /// <summary>
    /// 给引擎内置的界面动作补上缺的手柄绑定。
    /// </summary>
    /// <remarks>
    /// 只追加，绝不先擦。擦掉会把 <c>ui_accept</c> 的 Enter 与空格、<c>ui_cancel</c> 的 Esc 一起
    /// 带走，键鼠玩家就退不出面板了 —— 修好手柄反而弄坏键鼠。
    ///
    /// 实测 Godot 4.7.2 的默认值里 <c>ui_accept</c> 与 <c>ui_cancel</c> 一条手柄事件都没有
    /// （方向那几个有），所以必须补。补哪几条、哪几条和已有绑定重叠见
    /// <see cref="InputBindings.BuiltinUIPatches"/>。
    /// </remarks>
    private static void PatchBuiltinUIActions()
    {
        foreach (var (action, bindings) in InputBindings.BuiltinUIPatches)
        {
            if (!InputMap.HasAction(action))
            {
                throw new InvalidOperationException(
                    $"引擎里没有内置动作 {action}：要补绑定的那个动作不存在，这里宁可抛也不静默跳过");
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
    /// 键盘一律绑物理键位（<c>PhysicalKeycode</c>）而不是字符键位：AZERTY 键盘上 WASD 的物理位置
    /// 是 ZQSD，按字符绑会让那批玩家的移动键散开成四个不相邻的键，而按物理位在任何布局下都是同
    /// 一组键。顺带的好处是将来的改键界面显示的是玩家键盘上实际印着的字。
    ///
    /// 摇杆四向用同一根轴的正负两个方向各一条事件；扳机只取正方向，它从 0 压到 1，没有负半轴。
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

        // 技能键，这里的先后顺序就是技能位编号。
        InputSymbol.KeyH => Key(Godot.Key.H),
        InputSymbol.KeyY => Key(Godot.Key.Y),
        InputSymbol.KeyU => Key(Godot.Key.U),
        InputSymbol.KeyI => Key(Godot.Key.I),
        InputSymbol.KeyO => Key(Godot.Key.O),
        InputSymbol.KeyL => Key(Godot.Key.L),

        // 数字键那一排，随身栏每个格子各一个。
        InputSymbol.Digit1 => Key(Godot.Key.Key1),
        InputSymbol.Digit2 => Key(Godot.Key.Key2),
        InputSymbol.Digit3 => Key(Godot.Key.Key3),
        InputSymbol.Digit4 => Key(Godot.Key.Key4),
        InputSymbol.Digit5 => Key(Godot.Key.Key5),
        InputSymbol.Digit6 => Key(Godot.Key.Key6),

        // 鼠标键走另一种事件类型，所以不经 Key()。鼠标也没有「物理位」这回事：键盘那条讲究是
        // 为了 AZERTY 之类的布局，而左键在任何布局下都是左键。
        InputSymbol.MouseLeft => Mouse(MouseButton.Left),
        InputSymbol.MouseRight => Mouse(MouseButton.Right),

        // 面键按引擎那套与手柄品牌无关的编号：0 是下、1 是右、2 是左、3 是上（实测读回的顺序）。
        InputSymbol.PadFaceBottom => Pad(JoyButton.A),
        InputSymbol.PadFaceRight => Pad(JoyButton.B),
        InputSymbol.PadFaceLeft => Pad(JoyButton.X),
        InputSymbol.PadFaceTop => Pad(JoyButton.Y),
        InputSymbol.PadShoulderLeft => Pad(JoyButton.LeftShoulder),
        InputSymbol.PadShoulderRight => Pad(JoyButton.RightShoulder),
        InputSymbol.PadStickLeftClick => Pad(JoyButton.LeftStick),
        InputSymbol.PadDpadLeft => Pad(JoyButton.DpadLeft),
        InputSymbol.PadDpadRight => Pad(JoyButton.DpadRight),

        InputSymbol.PadStickLeftXMinus => Axis(JoyAxis.LeftX, -1f),
        InputSymbol.PadStickLeftXPlus => Axis(JoyAxis.LeftX, 1f),
        InputSymbol.PadStickLeftYMinus => Axis(JoyAxis.LeftY, -1f),
        InputSymbol.PadStickLeftYPlus => Axis(JoyAxis.LeftY, 1f),
        InputSymbol.PadTriggerLeft => Axis(JoyAxis.TriggerLeft, 1f),
        InputSymbol.PadTriggerRight => Axis(JoyAxis.TriggerRight, 1f),

        // 加了新符号却忘了在这里翻译时当场抛，不静默漏一条绑定 —— 漏掉的表现是「这个键没反应」。
        _ => throw new ArgumentOutOfRangeException(
            nameof(symbol), $"没有给这个物理位写翻译：{symbol}"),
    };

    private static InputEventKey Key(Key key) => new() { PhysicalKeycode = key };

    private static InputEventMouseButton Mouse(MouseButton button) => new() { ButtonIndex = button };

    private static InputEventJoypadButton Pad(JoyButton button) => new() { ButtonIndex = button };

    private static InputEventJoypadMotion Axis(JoyAxis axis, float value) =>
        new() { Axis = axis, AxisValue = value };
}
