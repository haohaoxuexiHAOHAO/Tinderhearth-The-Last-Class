namespace Tinderhearth.Rules.UI;

/// <summary>
/// 一个能被导航栈管理的界面。只是标识与几条声明，不含任何节点。
/// </summary>
/// <param name="Id">稳定标识。引擎层拿它去找对应的 Control。</param>
/// <param name="Layer">画在哪一层。</param>
/// <param name="Kind">操作类还是查看类。关卡内只允许查看。</param>
/// <remarks>
/// 没有「要不要暂停」这个字段。弹出界面接管输入时世界一律暂停，而本类按定义就是这种东西，
/// 所以暂停由 <see cref="NavigationStack.WorldShouldPause"/> 按栈里有没有层来判，不让各面板
/// 自己声明 —— 一个恒为真的开关只是多给人一个设错的机会。
///
/// 暂停与关卡内可用性这两条规则在设计仓 canon/gameplay/玩法定位.md 的「跨系统约定」一节。
/// </remarks>
public sealed record UISurface(string Id, UILayer Layer, SurfaceKind Kind)
{
    /// <summary>这一层在关卡内能不能用。</summary>
    public bool AvailableInLevel => Kind != SurfaceKind.Manage;
}

/// <summary>界面的用途分类。关卡内能不能用由它决定，不由各面板自己判断。</summary>
public enum SurfaceKind
{
    /// <summary>查看类：图鉴、任务目标、队伍状态。关卡内可用。</summary>
    View,

    /// <summary>操作类：派工、订货、建造下单。关卡内禁用。</summary>
    Manage,

    /// <summary>随身物：背包。它不算经营侧操作，所以关卡内可用。</summary>
    Carried,
}
