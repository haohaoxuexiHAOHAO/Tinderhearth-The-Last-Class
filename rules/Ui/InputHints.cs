namespace Tinderhearth.Rules.Ui;

/// <summary>
/// 按键提示的短记号（`UI-8` 显示、`FR-18`）。**从绑定表推，不另抄一份键位。**
/// </summary>
/// <remarks>
/// 为什么必须从 <see cref="InputBindings"/> 推：提示与实际绑定是两份数据时，改键位不改提示不会
/// 报错，只会让 HUD 一直教玩家按错的键 —— 而这种错玩家会当成自己记错了。推导让它对不上就编译不过
/// 或测试失败。
///
/// **这里是记号不是文案。** 数字与方位字不进文本表（`ENG-5` 管的是给玩家读的句子），它们是
/// 键位符号；真正的按键图标素材归美术，登记表里还没有那批槽位，所以第一版用记号顶。
///
/// **手柄面键刻意用方位字而不是 A／B／X／Y**：任天堂手柄的 A 与 B 位置与 Xbox 相反，按字母写
/// 迟早在某个手柄上教错。引擎自己也用布局中立的叫法（`Bottom/Right/Left/Top Action`），
/// 见 <see cref="InputSymbol"/> 的注释。扳机用 L／R —— 索尼叫 L2／R2、微软叫 LT／RT，
/// 首字母是两家的共同点。
/// </remarks>
public static class InputHints
{
    /// <summary>没有提示可显示时用的记号（未绑定、或该设备族上这个动作不存在）。</summary>
    public const string None = "";

    /// <summary>一个物理位的短记号。</summary>
    public static string LabelOf(InputSymbol symbol) => symbol switch
    {
        InputSymbol.Digit1 => "1",
        InputSymbol.Digit2 => "2",
        InputSymbol.Digit3 => "3",
        InputSymbol.Digit4 => "4",
        InputSymbol.Digit5 => "5",
        InputSymbol.Digit6 => "6",

        // 面键取方位，不取字母 —— 见类注释。
        InputSymbol.PadFaceBottom => "下",
        InputSymbol.PadFaceRight => "右",
        InputSymbol.PadFaceLeft => "左",
        InputSymbol.PadFaceTop => "上",

        InputSymbol.PadTriggerLeft => "L",
        InputSymbol.PadTriggerRight => "R",

        // 其余位现在没有一处要显示它们。**抛而不是返回空串** —— 静默返回空会让
        // 「忘了写记号」和「这个位故意不显示」长得一模一样。
        _ => throw new ArgumentOutOfRangeException(nameof(symbol),
            $"这个物理位还没有短记号：{symbol}（要显示它就在 InputHints 里补一条）"),
    };

    /// <summary>某个技能位属于哪一组。第 1–3 个归左扳机，第 4–6 个归右扳机。</summary>
    public static SkillGroup GroupOfSkill(string skillAction)
    {
        var index = IndexOfSkill(skillAction);
        return index < InputActions.SkillsPerGroup ? SkillGroup.Left : SkillGroup.Right;
    }

    /// <summary>某一组的修饰键记号（手柄上是扳机）。<see cref="SkillGroup.None"/> 没有记号。</summary>
    public static string GroupLabel(SkillGroup group) => group switch
    {
        SkillGroup.Left => LabelOf(InputSymbol.PadTriggerLeft),
        SkillGroup.Right => LabelOf(InputSymbol.PadTriggerRight),
        _ => None,
    };

    /// <summary>
    /// 某个技能位在某个设备族上该显示什么记号。
    /// </summary>
    /// <remarks>
    /// 键鼠：数字键直接对应，取该技能位自己的绑定。
    /// 手柄：技能位没有自己的绑定（<see cref="InputBindings.Exemptions"/> 登记了理由 —— 它由
    /// 修饰键组合解算发出），所以取**它对应的那个面键**的绑定。
    /// </remarks>
    public static string SkillLabel(string skillAction, InputDeviceKind device)
    {
        if (device == InputDeviceKind.KeyboardMouse)
        {
            var own = InputBindings.For(skillAction, device);
            return own.Count > 0 ? LabelOf(own[0].Symbol) : None;
        }

        var face = InputActions.ShadowedByModifier[
            IndexOfSkill(skillAction) % InputActions.SkillsPerGroup];
        var bindings = InputBindings.For(face, device);
        return bindings.Count > 0 ? LabelOf(bindings[0].Symbol) : None;
    }

    /// <summary>
    /// 手柄上完整的一句提示：修饰键 + 面键。键鼠上就是那个数字键。
    /// </summary>
    /// <remarks>
    /// 这是 `FR-17`「修饰键按下时 HUD 显示当前那一组对应哪三个技能」在文字上的形态 —— 玩家看到
    /// 「L下」就知道按住左扳机再按下面键。分隔符刻意没有：12px 下一个格子只装得下两个记号。
    /// </remarks>
    public static string SkillCombo(string skillAction, InputDeviceKind device) =>
        device == InputDeviceKind.KeyboardMouse
            ? SkillLabel(skillAction, device)
            : GroupLabel(GroupOfSkill(skillAction)) + SkillLabel(skillAction, device);

    private static int IndexOfSkill(string skillAction)
    {
        for (var i = 0; i < InputActions.Skills.Count; i++)
        {
            if (InputActions.Skills[i] == skillAction)
            {
                return i;
            }
        }

        throw new KeyNotFoundException($"不是技能位：{skillAction}");
    }
}
