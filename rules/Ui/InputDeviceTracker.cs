namespace Tinderhearth.Rules.Ui;

/// <summary>一次输入信号的形状。**决定它有没有资格切换设备提示。**</summary>
public enum InputSignalKind
{
    /// <summary>离散按下：按键、鼠标键、手柄按钮。玩家的明确意图。</summary>
    Press,

    /// <summary>模拟轴：摇杆与扳机。要过阈值才算意图。</summary>
    Axis,

    /// <summary>纯移动：鼠标位移、陀螺仪一类。**永远不切换设备**，理由见 <see cref="InputDeviceTracker"/>。</summary>
    Motion,
}

/// <summary>
/// 记住玩家最后真正用的是哪个设备（`UI-7`）。按键提示图标照它切换。
/// </summary>
/// <remarks>
/// 为什么这条规则在规则层而不是「谁收到事件谁改一个字段」：**它的失效方式是图标闪烁**，而闪烁
/// 只在特定条件下出现（手柄放在桌上摇杆有零点几的漂移、鼠标被碰一下），实机随手试是试不出来的。
/// 做成三行规则加阈值，就能用单元测试把三种情形都钉住。
///
/// 三条规则：
///
/// 1. **离散按下一定切换。** 玩家按了键或按钮，那就是他在用的设备，没有歧义。
/// 2. **模拟轴要过阈值才切换。** 阈值必须高于摇杆死区，否则手柄搁在桌上的漂移会持续把提示从
///    键鼠抢过去 —— 玩家会看到图标自己跳。
/// 3. **纯移动永远不切换。** 鼠标被碰一下不代表玩家换了设备；真要用鼠标玩，他会点下去，而点击
///    是 <see cref="InputSignalKind.Press"/>。这条同时省掉一个「多少像素才算移动」的拍脑袋阈值。
///
/// 初始值取键鼠：本作发行目标是 Steam 的 Windows PC 端，玩家还没输入任何东西时先按键鼠显示，
/// 比显示「未知」有用。
/// </remarks>
public sealed class InputDeviceTracker
{
    /// <summary>
    /// 轴要推到多深才算换了设备。取 <see cref="InputBindings.TriggerDeadzone"/> 同一个数：
    /// 「够得上当修饰键」与「够得上换图标」是同一个门槛，两个数会分叉。
    /// </summary>
    public const float AxisSwitchThreshold = InputBindings.TriggerDeadzone;

    /// <summary>当前该显示哪一族的按键提示。</summary>
    public InputDeviceKind Current { get; private set; } = InputDeviceKind.KeyboardMouse;

    /// <summary>
    /// 收到一次输入信号。返回**设备是否因此改变** —— 调用方据此决定要不要刷新图标。
    /// </summary>
    /// <param name="device">这次信号来自哪个设备族。</param>
    /// <param name="kind">信号形状。</param>
    /// <param name="magnitude">
    /// 轴的绝对值，<see cref="InputSignalKind.Axis"/> 时才看。离散按下传什么都不影响判定。
    /// </param>
    public bool Notice(InputDeviceKind device, InputSignalKind kind, float magnitude = 1f)
    {
        var qualifies = kind switch
        {
            InputSignalKind.Press => true,
            InputSignalKind.Axis => Math.Abs(magnitude) >= AxisSwitchThreshold,
            _ => false,
        };

        if (!qualifies || device == Current)
        {
            return false;
        }

        Current = device;
        return true;
    }
}
