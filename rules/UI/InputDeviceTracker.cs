namespace Tinderhearth.Rules.UI;

/// <summary>一次输入信号的形态。它决定这次信号有没有资格切换按键提示。</summary>
public enum InputSignalKind
{
    /// <summary>离散按下：按键、鼠标键、手柄按钮。玩家的明确意图。</summary>
    Press,

    /// <summary>模拟轴：摇杆与扳机。要过阈值才算意图。</summary>
    Axis,

    /// <summary>纯移动：鼠标位移、陀螺仪一类。永远不切换设备，理由见 <see cref="InputDeviceTracker"/>。</summary>
    Motion,
}

/// <summary>
/// 记住玩家最后真正用的是哪个设备。按键提示图标照它切换。
/// </summary>
/// <remarks>
/// 规则是：离散按下一定切换，模拟轴要过阈值才切换，纯移动永远不切换。鼠标被碰一下不代表玩家换了
/// 设备，真要用鼠标他会点下去，而点击算离散按下 —— 这条顺便省掉一个「多少像素才算移动」的阈值。
///
/// 阈值必须高于摇杆死区。低了的话，手柄搁在桌上那零点几的漂移会持续把提示从键鼠抢过去，玩家看到
/// 的是图标自己来回跳。这种失效实机随手试是试不出来的，所以判定放在这一层让单元测试盯住。
///
/// 起始值取键鼠：发行目标是 Windows PC，玩家还没按任何东西时先显示键鼠，比显示「未知」有用。
/// </remarks>
public sealed class InputDeviceTracker
{
    /// <summary>
    /// 轴要推到多深才算换了设备。取 <see cref="InputBindings.TriggerDeadzone"/> 同一个数：
    /// 「够得上当修饰键」与「够得上换图标」是同一个门槛，分成两个数迟早对不上。
    /// </summary>
    public const float AxisSwitchThreshold = InputBindings.TriggerDeadzone;

    /// <summary>当前该显示哪一族的按键提示。</summary>
    public InputDeviceKind Current { get; private set; } = InputDeviceKind.KeyboardMouse;

    /// <summary>
    /// 收到一次输入信号。返回设备是否因此改变，调用方据此决定要不要刷新图标。
    /// </summary>
    /// <param name="device">这次信号来自哪个设备族。</param>
    /// <param name="kind">信号形态。</param>
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
