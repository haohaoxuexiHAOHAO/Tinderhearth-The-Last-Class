namespace Tinderhearth.Rules.Ui;

/// <summary>
/// 面板打开时被遮住的玩法动作（`UI-11`）。
/// </summary>
/// <remarks>
/// 问题根源（2026-08-30 实测，记在 <c>issue-UI-7</c> 的「已知重叠」段）：手柄的 `ui_accept`
/// 绑在下面键上，`ui_cancel` 绑在右面键上；而下面键同时是跳跃，右面键同时是闪避。面板打开时
/// 控件消费 `ui_accept` 的 InputEvent，但 <c>SetInputAsHandled</c> 挡不住轮询状态 ——
/// <c>Input.IsActionPressed</c> 此刻仍然返回 true，所以玩家在手环里按下确认会既操作控件又跳跃。
///
/// **裁定（作者 2026-09-02）：面板打开时屏蔽这两个玩法动作。**
///
/// 理由：面板打开时玩家的意图是操作界面，不是战斗。背包不暂停世界这件事让玩家保留了移动与攻防
/// 能力，让他们能一边整理背包一边走位、防御；但跳跃与闪避在手柄上与 UI 确认、返回共用同一个键，
/// 「按下去到底是哪个」本身就是歧义，歧义里选玩家在做的那件事（界面操作）更合理。代价是面板
/// 打开期间手柄玩家不能跳跃与闪避，但正典已为失衡结束设了逃生窗口，短暂不可用可接受。
///
/// 键鼠不受影响：键鼠的确认与返回用 Enter／Escape，这两个键没有绑玩法动作。
///
/// **落点是 <see cref="InputRouter"/> 的遮挡判定**，与修饰键那套共用同一个门面 ——
/// 这正是「玩法代码必须通过 <c>InputRouter</c> 问输入」那条规定的执行体。
/// </remarks>
public static class PanelInputBlock
{
    /// <summary>
    /// 面板打开时在手柄上被遮的玩法动作。
    /// </summary>
    /// <remarks>
    /// 清单来自 <see cref="InputBindings.BuiltinUiPatches"/>「已知重叠」那段：
    /// 下面键 (<c>PadFaceBottom</c>) ＝ 跳跃且 ＝ <c>ui_accept</c>，
    /// 右面键 (<c>PadFaceRight</c>) ＝ 闪避且 ＝ <c>ui_cancel</c>。
    ///
    /// 只列手柄侧的重叠动作，不列键鼠 —— 键鼠没有这个问题。
    /// </remarks>
    public static readonly IReadOnlySet<string> BlockedByOpenPanel =
        new HashSet<string> { InputActions.Jump, InputActions.Dodge };

    /// <summary>
    /// 这个动作在「面板打开且当前是手柄」这个组合下该不该被遮。
    /// </summary>
    /// <param name="action">要查的玩法动作名。</param>
    /// <param name="panelOpen">导航栈里有没有任何面板（<c>NavigationStack.Depth &gt; 0</c>）。</param>
    /// <param name="device">当前使用的设备族。</param>
    public static bool ShouldBlock(string action, bool panelOpen, InputDeviceKind device) =>
        panelOpen && device == InputDeviceKind.Gamepad && BlockedByOpenPanel.Contains(action);
}
