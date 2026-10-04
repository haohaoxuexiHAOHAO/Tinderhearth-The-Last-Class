using Godot;
using Tinderhearth.Rules.UI;

namespace Tinderhearth.UI;

/// <summary>
/// 输入门面：装好按键绑定、解算修饰键组合、记住玩家最后用的是键鼠还是手柄。
/// </summary>
/// <remarks>
/// 玩法代码一律通过本类问输入，不要直接调 <c>Input.IsActionPressed</c>。实测：在 <c>_Input</c> 里
/// 把面键事件标成已处理之后，<c>Input.IsActionPressed</c> 与 <c>IsActionJustPressed</c> 仍然返回
/// true —— 拦下事件只挡住事件流，挡不住轮询，而这个失效不报错，见 <c>CONVENTIONS.md</c>。
///
/// 判定都在规则层的 <see cref="SkillModifierState"/>：哪一组生效、这个面键现在是哪个技能、
/// 先按住的赢、松开时该松哪个技能。本类只负责认出引擎事件、喂给它，再把结论翻译回引擎动作。
///
/// 对外用普通 C# 事件而不是 Godot 信号：消费方也是 C#，省掉枚举过 Variant 那一层，
/// 类型也不会退化成 int。
/// </remarks>
public partial class InputRouter : Node
{
    private readonly SkillModifierState _modifiers = new();
    private readonly InputDeviceTracker _devices = new();

    /// <summary>最后用的设备族变了。按键提示图标照它换。</summary>
    public event Action<InputDeviceKind>? DeviceChanged;

    /// <summary>生效的技能组变了。HUD 照它显示当前这一组是哪几个技能。</summary>
    public event Action<SkillGroup>? SkillGroupChanged;

    /// <summary>现在该显示哪一族的按键提示。</summary>
    public InputDeviceKind Device => _devices.Current;

    /// <summary>当前生效的技能组，没按修饰键时是 <see cref="SkillGroup.None"/>。</summary>
    public SkillGroup ActiveSkillGroup => _modifiers.Active;

    /// <summary>
    /// 当前这一组是哪几个技能位。修饰键一按住就能读到。
    /// </summary>
    public IReadOnlyList<string> ActiveSkills => _modifiers.ActiveSkills;

    /// <summary>某个面键此刻对应哪个技能位，没按修饰键时为 <c>null</c>。给 HUD 标在面键图标上用。</summary>
    public string? SkillOn(string shadowedAction) => _modifiers.SkillFor(shadowedAction);

    public override void _Ready()
    {
        InputMapInstaller.Install();

        // 世界暂停时仍要收输入：弹出界面会暂停世界，而那时界面本身还得能操作。
        ProcessMode = ProcessModeEnum.Always;
    }

    /// <summary>
    /// 这个动作现在是不是按着。
    /// </summary>
    /// <remarks>
    /// 只有一层遮挡：修饰键按住时被遮的动作（<see cref="SkillModifierState.ShouldSuppress"/>）。
    ///
    /// 面板打开时这里不用遮任何东西。弹出界面接管输入时世界是暂停的，玩法节点不在跑（本类的
    /// <c>ProcessMode</c> 是 <c>Always</c>，玩法节点不是），所以手柄上「下面键既是跳跃又是界面
    /// 确认」那个歧义不会发生。暂停这条规则见设计仓 canon/gameplay/玩法定位.md。
    /// </remarks>
    public bool IsPressed(string action) =>
        !_modifiers.ShouldSuppress(action)
        && Input.IsActionPressed(action);

    /// <summary>这一帧这个动作是不是刚按下。同样受修饰键遮挡影响。</summary>
    public bool IsJustPressed(string action) =>
        !_modifiers.ShouldSuppress(action)
        && Input.IsActionJustPressed(action);

    /// <summary>这一帧这个动作是不是刚松开。</summary>
    public bool IsJustReleased(string action) => Input.IsActionJustReleased(action);

    /// <summary>
    /// 移动方向。不受修饰键遮挡 —— 挑技能的时候还得能走位。
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
            SkillGroup before = _modifiers.Active;
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

    /// <summary>认出事件来自哪个设备族、属于哪一类信号，交给规则层判要不要换提示。</summary>
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
        SkillGroup before = _modifiers.Active;

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
    /// 用 <c>InputEventAction</c> 合成技能位的按下。实测合成之后 <c>Input.IsActionPressed</c> 与
    /// <c>IsActionJustPressed</c> 都为 true，<c>_Input</c> 也收到该事件一次 —— 于是技能位对下游
    /// 就是个普通动作，下游不必知道它是组合出来的。
    ///
    /// 同时把原事件消费掉。消费只挡事件流、不挡轮询（见类注释），轮询那一半靠
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
