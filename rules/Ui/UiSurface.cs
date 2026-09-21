namespace Tinderhearth.Rules.Ui;

/// <summary>
/// 一个可被导航栈管理的界面（`UI-6`）。只是标识与几条声明，不含任何节点。
/// </summary>
/// <param name="Id">稳定标识。引擎层拿它去找对应的 Control。</param>
/// <param name="Layer">画在哪一层。</param>
/// <param name="Kind">
/// 操作类还是查看类。关卡内只允许查看（[玩法定位 · 跨系统约定]：派工、订货、建造下单这些
/// 经营侧操作在战斗中能调度会让关卡失去压力）。
/// </param>
/// <remarks>
/// **没有「要不要暂停」这个字段。** 正典的判据是「任何弹出界面并接管输入的东西一律暂停世界」
/// （[玩法定位 · 弹界面接管输入就暂停世界，世界里的动作不暂停]），而 <see cref="UiSurface"/>
/// 按定义就是这样的东西 —— 所以暂停由 <see cref="NavigationStack.WorldShouldPause"/> 按「栈里
/// 有没有层」判，不由各面板自己声明。**留一个恒为真的开关等于给人一个设错的机会。**
/// </remarks>
public sealed record UiSurface(string Id, UiLayer Layer, SurfaceKind Kind)
{
    /// <summary>这一层在关卡内能不能用。</summary>
    public bool AvailableInLevel => Kind != SurfaceKind.Manage;
}

/// <summary>界面的用途分类。**关卡内的可用性由它决定**，不由各面板自己判断。</summary>
public enum SurfaceKind
{
    /// <summary>查看类：图鉴、任务目标、队伍状态。关卡内可用。</summary>
    View,

    /// <summary>操作类：派工、订货、建造下单。关卡内禁用。</summary>
    Manage,

    /// <summary>随身物：背包。正典明确它不算管理功能，所以关卡内可用。</summary>
    Carried,
}
