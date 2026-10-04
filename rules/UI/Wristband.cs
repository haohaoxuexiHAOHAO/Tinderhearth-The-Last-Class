namespace Tinderhearth.Rules.UI;

/// <summary>
/// 手环：经营侧界面的统一容器，形态是一个面板带标签页。本类只管有哪些页、各自在什么场合可用。
/// </summary>
/// <remarks>
/// 关卡内不能做经营侧操作，所以这里把可用性做成一张表加一次判定，好让单元测试盯住。让各页
/// 自己判的话，迟早有一页忘了判，而那种漏洞只在战斗中暴露。
///
/// 页里具体显示什么不在这里，跟着各自功能的实现走。标签页清单只列第一版会有的那几项。
///
/// 手环收哪些功能、关卡内允许查看不允许操作，见设计仓 canon/gameplay/玩法定位.md 的
/// 「跨系统约定」一节。
/// </remarks>
public static class Wristband
{
    /// <summary>手环容器本身。它是一个面板，所以打开时世界暂停。</summary>
    public static readonly UISurface Surface =
        new("wristband", UILayer.Panel, SurfaceKind.View);

    /// <summary>标签页，顺序即显示顺序。查看类在前，操作类在后。</summary>
    public static readonly IReadOnlyList<WristbandTab> Tabs =
    [
        new("notice", SurfaceKind.View),      // 通知：手环是唯一的事件通知渠道
        new("codex", SurfaceKind.View),       // 图鉴：宝物、宝石、技能、敌人、作物
        new("party", SurfaceKind.View),       // 队伍状态：关卡内也允许查看
        new("prices", SurfaceKind.View),      // 行情：每个价格因子要能查到当前值与原因
        new("assign", SurfaceKind.Manage),    // 派工
        new("order", SurfaceKind.Manage),     // 订货：第一版的商店走手环
        new("build", SurfaceKind.Manage),     // 建造下单：第一版走手环
    ];

    /// <summary>在给定场合下可用的标签页。</summary>
    public static IEnumerable<WristbandTab> AvailableIn(UIContext context) =>
        Tabs.Where(t => t.AvailableIn(context));

    /// <summary>某个标签页在给定场合下可不可用。找不到这个 id 就抛 —— 拼错不该静默变成「不可用」。</summary>
    public static bool IsEnabled(string tabId, UIContext context)
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
    public bool AvailableIn(UIContext context) =>
        context != UIContext.Level || Kind != SurfaceKind.Manage;
}

/// <summary>界面所处的场合。只有这两种，因为视角规则只有两种、没有例外。</summary>
public enum UIContext
{
    /// <summary>俯视的基地与城区。经营侧操作在这里进行。</summary>
    Base,

    /// <summary>侧视关卡。手环的管理功能在这里不可用。</summary>
    Level,
}
