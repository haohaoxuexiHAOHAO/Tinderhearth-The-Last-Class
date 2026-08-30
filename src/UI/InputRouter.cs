using Godot;
using Tinderhearth.Rules.Ui;

namespace Tinderhearth.UI;

/// <summary>
/// 输入门面（`UI-7`）：装绑定、解算修饰键组合、记住最后用的设备。
/// </summary>
/// <remarks>
/// **玩法代码必须通过本类问输入，不许直接调 <c>Input.IsActionPressed</c>。** 这不是风格偏好，是
/// 2026-08-30 实测出来的硬约束：在 <c>_Input</c> 里对面键事件调 <c>SetInputAsHandled</c> 之后，
/// <c>Input.IsActionPressed</c> 与 <c>IsActionJustPressed</c> **仍然返回 true**。也就是说「拦下事件」
/// 只挡住了事件流，挡不住轮询 —— 任何直接轮询的代码都会在玩家按住扳机挑技能时照旧看到轻攻击被
/// 按下，打出一次没打算打的攻击。这种失效不报错，所以要有执行体：`tools/check_input_map.py` 会扫
/// `src/` 里除本文件之外的 <c>Input.IsActionPressed</c> 调用并判失败。
///
/// 分工与 `UI-6` 一致：**判定在规则层，节点在这里。** 「哪一组生效」「这个面键现在是哪个技能」
/// 「先按住的赢」「松开时该松哪个技能」都由 <see cref="SkillModifierState"/> 判，有单元测试盯着；
/// 本类只负责认出引擎事件、喂给它、再把结论翻译成引擎动作。
///
/// 对外用普通 C# 事件而不是 Godot 信号：消费方（`UI-8` 的 HUD）是 C#，用 C# 事件省掉枚举过
/// Variant 的一层，类型也不会退化成 int。
/// </remarks>
public partial class InputRouter : Node
{
    private readonly SkillModifierState _modifiers = new();
    private readonly InputDeviceTracker _devices = new();

    /// <summary>最后使用的设备族变了。按键提示图标照它换（显示归 `UI-8`）。</summary>
    public event Action<InputDeviceKind>? DeviceChanged;

    /// <summary>生效的技能组变了。HUD 照它显示「当前这一组是哪三个技能」（显示归 `UI-8`）。</summary>
    public event Action<SkillGroup>? SkillGroupChanged;

    /// <summary>现在该显示哪一族的按键提示。</summary>
    public InputDeviceKind Device => _devices.Current;

    /// <summary>当前生效的技能组，没按修饰键时是 <see cref="SkillGroup.None"/>。</summary>
    public SkillGroup ActiveSkillGroup => _modifiers.Active;

    /// <summary>
    /// 当前这一组是哪三个技能位。**这就是验收要的「修饰键按下时数据可取」。**
    /// </summary>
    public IReadOnlyList<string> ActiveSkills => _modifiers.ActiveSkills;

    /// <summary>某个面键此刻对应哪个技能位，没按修饰键时为 <c>null</c>。给 HUD 标在面键图标上用。</summary>
    public string? SkillOn(string shadowedAction) => _modifiers.SkillFor(shadowedAction);

    public override void _Ready()
    {
        InputMapInstaller.Install();

        // 世界暂停时仍要收输入：弹窗要求暂停，而那时界面还得能操作（`UI-6` 的导航栈）。
        ProcessMode = ProcessModeEnum.Always;
    }

    /// <summary>
    /// 这个动作现在是不是按着。**修饰键按住时被遮的动作一律返回 false。**
    /// </summary>
    public bool IsPressed(string action) =>
        !_modifiers.ShouldSuppress(action) && Input.IsActionPressed(action);

    /// <summary>这一帧这个动作是不是刚按下。同样受遮挡影响。</summary>
    public bool IsJustPressed(string action) =>
        !_modifiers.ShouldSuppress(action) && Input.IsActionJustPressed(action);

    /// <summary>这一帧这个动作是不是刚松开。</summary>
    public bool IsJustReleased(string action) => Input.IsActionJustReleased(action);

    /// <summary>
    /// 移动方向。**不受修饰键遮挡** —— 挑技能的时候还得能走位。
    /// </summary>
    public Vector2 MoveDirection() => Input.GetVector(
        InputActions.MoveLeft, InputActions.MoveRight,
        InputActions.MoveUp, InputActions.MoveDown);

    public override void _Input(InputEvent @event)
    {
        // 自己合成的动作事件不再回炉：它不属于任何物理设备，拿它去判「最后用的是哪个设备」
        // 会把结论污染成上一次组合的设备。
        if (@event is InputEventAction)
        {
            return;
        }

        NoticeDevice(@event);
        TrackModifiers(@event);
        ResolveCombo(@event);
    }

    public override void _Notification(int what)
    {
        // 失去焦点时把修饰键全松开。不做的话玩家按住扳机去 Alt+Tab，回来时修饰键还是「按住」，
        // 而被它驱动的技能位永远收不到松开 —— 表现是一个技能卡在按下不放。
        if (what is (int)NotificationApplicationFocusOut or (int)NotificationWMWindowFocusOut)
        {
            var before = _modifiers.Active;
            foreach (var stuck in _modifiers.ReleaseAll())
            {
                EmitAction(stuck, pressed: false);
            }

            if (before != _modifiers.Active)
            {
                SkillGroupChanged?.Invoke(_modifiers.Active);
            }
        }
    }

    /// <summary>认出事件来自哪个设备族、是什么形状，交给规则层判要不要换提示。</summary>
    private void NoticeDevice(InputEvent @event)
    {
        var (device, kind, magnitude) = @event switch
        {
            InputEventKey => (InputDeviceKind.KeyboardMouse, InputSignalKind.Press, 1f),
            InputEventMouseButton => (InputDeviceKind.KeyboardMouse, InputSignalKind.Press, 1f),
            InputEventMouseMotion => (InputDeviceKind.KeyboardMouse, InputSignalKind.Motion, 0f),
            InputEventJoypadButton => (InputDeviceKind.Gamepad, InputSignalKind.Press, 1f),
            InputEventJoypadMotion m =>
                (InputDeviceKind.Gamepad, InputSignalKind.Axis, Mathf.Abs(m.AxisValue)),
            _ => (_devices.Current, InputSignalKind.Motion, 0f),
        };

        if (_devices.Notice(device, kind, magnitude))
        {
            DeviceChanged?.Invoke(_devices.Current);
        }
    }

    /// <summary>更新修饰键的按住状态。按下与松开都要管，否则修饰键会卡住。</summary>
    private void TrackModifiers(InputEvent @event)
    {
        var before = _modifiers.Active;

        foreach (var action in (string[])[InputActions.SkillGroupLeft, InputActions.SkillGroupRight])
        {
            var group = SkillModifierState.GroupOf(action);
            if (@event.IsActionPressed(action))
            {
                // 扳机是模拟轴，越过死区之后同一次按住会持续来事件，所以 Press 必须可重复调用。
                _modifiers.Press(group);
            }
            else if (@event.IsActionReleased(action))
            {
                _modifiers.Release(group);
            }
        }

        if (before != _modifiers.Active)
        {
            SkillGroupChanged?.Invoke(_modifiers.Active);
        }
    }

    /// <summary>
    /// 修饰键按住时，把面键的按下与松开翻译成技能位的按下与松开。
    /// </summary>
    /// <remarks>
    /// 用 <c>InputEventAction</c> 合成技能位的按下（2026-08-30 实测：合成后
    /// <c>Input.IsActionPressed</c> 与 <c>IsActionJustPressed</c> 都为 true，且 <c>_Input</c> 收到
    /// 该事件一次）。这样技能位对下游而言就是个普通动作，下游不必知道它是组合出来的。
    ///
    /// 同时把原事件消费掉。**消费只挡事件流不挡轮询**（见类注释），所以轮询那一半靠
    /// <see cref="IsPressed"/> 的遮挡判定，两边合起来才完整。
    /// </remarks>
    private void ResolveCombo(InputEvent @event)
    {
        foreach (var action in InputActions.ShadowedByModifier)
        {
            if (@event.IsActionPressed(action) && _modifiers.BeginSkill(action) is string pressed)
            {
                EmitAction(pressed, pressed: true);
                GetViewport().SetInputAsHandled();
            }
            else if (@event.IsActionReleased(action) && _modifiers.EndSkill(action) is string released)
            {
                EmitAction(released, pressed: false);
                GetViewport().SetInputAsHandled();
            }
        }
    }

    private static void EmitAction(string action, bool pressed) =>
        Input.ParseInputEvent(new InputEventAction { Action = action, Pressed = pressed });
}
