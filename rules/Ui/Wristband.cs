namespace Tinderhearth.Rules.Ui;

/// <summary>
/// 手环：经营侧界面的统一容器（`UI-6`）。本类只管**有哪些标签页、各自在什么场合可用**。
/// </summary>
/// <remarks>
/// 形态取「统一容器带标签页」由作者 2026-08-30 定。[世界观]原写「手环的界面形态与交互结构
/// 待玩法与界面设计确定」，这里补上的就是那一块的骨架 —— 页内内容跟着各自功能的实现需求走，
/// 本条不做。
///
/// 为什么它在规则层：**关卡内的可用性是正典约束，不是界面细节。** [玩法定位 · 跨系统约定]
/// 写明「手环的管理功能在关卡内不可用，只允许查看」。把这条做成数据加一次判定，就能用单元
/// 测试盯住；散落到各页自己判，迟早有一页忘了判，而那种漏洞在战斗中才暴露。
///
/// 标签页清单只列**第一版会有的**。图鉴挂手环是正典明说的（「手环这个载体已经在了，不用做
/// 新界面」）；派工、订货、建造下单三项被正典点名为经营侧操作，所以是 <see cref="SurfaceKind.Manage"/>。
/// </remarks>
public static class Wristband
{
    /// <summary>手环容器本身。它是一个面板，不暂停世界。</summary>
    public static readonly UiSurface Surface =
        new("wristband", UiLayer.Panel, PausesWorld: false, SurfaceKind.View);

    /// <summary>标签页，顺序即显示顺序。查看类在前，操作类在后。</summary>
    public static readonly IReadOnlyList<WristbandTab> Tabs =
    [
        new("notice", SurfaceKind.View),      // 通知：正典说手环是唯一的事件通知渠道
        new("codex", SurfaceKind.View),       // 图鉴：宝物、宝石、技能、敌人、作物
        new("party", SurfaceKind.View),       // 队伍状态：关卡内明确允许查看
        new("prices", SurfaceKind.View),      // 行情：每个价格因子要能查到当前值与原因
        new("assign", SurfaceKind.Manage),    // 派工
        new("order", SurfaceKind.Manage),     // 订货：第一版商店走手环
        new("build", SurfaceKind.Manage),     // 建造下单：第一版走手环
    ];

    /// <summary>在给定场合下可用的标签页。</summary>
    public static IEnumerable<WristbandTab> AvailableIn(UiContext context) =>
        Tabs.Where(t => t.AvailableIn(context));

    /// <summary>某个标签页在给定场合下可不可用。找不到该 id 时抛 —— 拼错不该静默变成「不可用」。</summary>
    public static bool IsEnabled(string tabId, UiContext context)
    {
        var tab = Tabs.FirstOrDefault(t => t.Id == tabId)
            ?? throw new KeyNotFoundException($"手环没有这个标签页：{tabId}");
        return tab.AvailableIn(context);
    }
}

/// <summary>手环的一个标签页。</summary>
/// <param name="Id">稳定标识，也是文本键的后缀。</param>
/// <param name="Kind">用途分类，决定关卡内可不可用。</param>
public sealed record WristbandTab(string Id, SurfaceKind Kind)
{
    /// <summary>关卡内只允许查看类；基地与城区两类都可用。</summary>
    public bool AvailableIn(UiContext context) =>
        context != UiContext.Level || Kind != SurfaceKind.Manage;
}

/// <summary>界面所处的场合。**只有两种**，因为正典的视角规则只有两种且无例外。</summary>
public enum UiContext
{
    /// <summary>俯视的基地与城区。经营侧操作在这里进行。</summary>
    Base,

    /// <summary>侧视关卡。手环的管理功能在这里不可用。</summary>
    Level,
}
