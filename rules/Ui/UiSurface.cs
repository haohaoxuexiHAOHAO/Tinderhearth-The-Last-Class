namespace Tinderhearth.Rules.Ui;

/// <summary>
/// 一个可被导航栈管理的界面（`UI-6`）。只是标识与几条声明，不含任何节点。
/// </summary>
/// <param name="Id">稳定标识。引擎层拿它去找对应的 Control。</param>
/// <param name="Layer">画在哪一层。</param>
/// <param name="PausesWorld">
/// 打开时是否暂停世界。**背包必须为 <c>false</c>** —— 正典写明关卡内允许背包操作且不暂停，
/// 理由是随时能暂停整理等于给玩家一个免费的思考窗口。
/// </param>
/// <param name="Kind">
/// 操作类还是查看类。关卡内只允许查看（[玩法定位 · 跨系统约定]：派工、订货、建造下单这些
/// 经营侧操作在战斗中能调度会让关卡失去压力）。
/// </param>
public sealed record UiSurface(string Id, UiLayer Layer, bool PausesWorld, SurfaceKind Kind)
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

    /// <summary>随身物：背包。正典明确它不算管理功能，关卡内可用且不暂停。</summary>
    Carried,
}
