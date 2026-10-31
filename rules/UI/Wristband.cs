namespace Tinderhearth.Rules.UI;

/// <summary>
/// 手环：经营侧界面的统一容器，形态是一个面板、两级页签。本类只管有哪些页、各自在什么场合怎么显示。
/// </summary>
/// <remarks>
/// 顶层页签各是一组页，子页才是「一页答一个问题」的那个页；只有一页的顶层页签就是不分子页。
///
/// 关卡里每个子页按它的用途分类各判各的：操作类灰着，查看类与随身物照常可用；关卡里根本没有的那一页
/// 不显示（仓库 —— 关卡里没有仓库）。灰着与不显示分开，是因为它们说的是两件事：灰着是「它在，只是
/// 这里用不了」，不显示是「这里没有这东西」。并成一种，玩家要么在关卡里找不到派工去哪了，要么对着
/// 一个在这里永远点不开的仓库。
///
/// 可用性做成一张表加一次判定，好让单元测试盯住。让各页自己判的话，迟早有一页忘了判，而那种漏洞只在
/// 战斗中暴露。页里具体显示什么不在这里，跟着各自功能的实现走。
///
/// 每一页归哪一类的依据在设计仓 design/界面系统.md 的「载体归属不是本页新分的」那一节，关卡里能做什么的
/// 判据在正典 canon/gameplay/玩法定位.md 的「跨系统约定」；图鉴分哪几类是正典同一份的「查询与图鉴」定的。
/// 有哪几个页签、各挂哪几页、什么顺序，家在这里。
/// </remarks>
public static class Wristband
{
    /// <summary>手环容器本身。它是一个面板，所以打开时世界暂停。</summary>
    public static readonly UISurface Surface =
        new("wristband", UILayer.Panel, SurfaceKind.View);

    /// <summary>顶层页签，顺序即从左到右；每个页签下的子页同样从左到右。</summary>
    /// <remarks>
    /// 顶层页签的标识也是它那张图标的文件名（设计仓 production/界面绘制约定.md 的「个人界面那一套」），
    /// 改名要连图一起改。
    /// </remarks>
    public static readonly IReadOnlyList<WristbandTab> Tabs =
    [
        new("character",                                // 个人界面，即角色面板
        [
            Page("attributes", SurfaceKind.Carried),    // 属性
            Page("status", SurfaceKind.Carried),        // 状态
            Page("talents", SurfaceKind.Carried),       // 天赋
            Page("skills", SurfaceKind.Carried),        // 技能
            Page("treasures", SurfaceKind.Carried),     // 宝物
            Page("enhance", SurfaceKind.Carried),       // 强化
            Page("diary", SurfaceKind.View),            // 日记
        ]),
        new("students",                                 // 学生列表
        [
            Page("roster", SurfaceKind.View),           // 名册，即队伍页
            Page("assign", SurfaceKind.Manage),         // 派工
        ]),
        new("backpack",                                 // 背包
        [
            Page("bag", SurfaceKind.Carried),           // 背包
            NotInLevel("storage", SurfaceKind.Manage),  // 仓库：关卡里没有仓库
        ]),
        new("quests",                                   // 任务列表
        [
            Page("notice", SurfaceKind.View),           // 通知：手环是唯一的事件通知渠道
            Page("board", SurfaceKind.Manage),          // 委托板
            Page("mainline", SurfaceKind.View),         // 主线
        ]),
        new("map", [Page("map", SurfaceKind.View)]),               // 地图，不分子页
        new("relations", [Page("relations", SurfaceKind.View)]),   // 关系列表，不分子页
        new("codex",                                    // 百科全书
        [
            Page("codex_treasure", SurfaceKind.View),   // 图鉴：宝物
            Page("codex_gem", SurfaceKind.View),        // 图鉴：宝石
            Page("codex_skill", SurfaceKind.View),      // 图鉴：技能
            Page("codex_enemy", SurfaceKind.View),      // 图鉴：敌人
            Page("codex_crop", SurfaceKind.View),       // 图鉴：作物
            Page("guide", SurfaceKind.View),            // 引导清单
        ]),
        new("business",                                 // 经营
        [
            Page("prices", SurfaceKind.View),           // 行情：每个价格因子要能查到当前值与原因
            Page("order", SurfaceKind.Manage),          // 订货与收购
            Page("build", SurfaceKind.Manage),          // 建造下单
            Page("settlement", SurfaceKind.View),       // 结算完整明细
        ]),
    ];

    /// <summary>全部子页，按页签从左到右、页签里从左到右。</summary>
    public static IEnumerable<WristbandPage> Pages => Tabs.SelectMany(t => t.Pages);

    /// <summary>按标识取一个顶层页签。找不到就抛 —— 拼错不该静默变成「打不开」。</summary>
    public static WristbandTab TabById(string tabId) =>
        Tabs.FirstOrDefault(t => t.Id == tabId)
            ?? throw new KeyNotFoundException($"手环没有这个页签：{tabId}");

    /// <summary>按标识取一个子页。找不到就抛 —— 拼错不该静默变成「不可用」。</summary>
    public static WristbandPage PageById(string pageId) =>
        Pages.FirstOrDefault(p => p.Id == pageId)
            ?? throw new KeyNotFoundException($"手环没有这一页：{pageId}");

    // 关卡里也在的页：能不能用按用途分类判。
    private static WristbandPage Page(string id, SurfaceKind kind) => new(id, kind, ExistsInLevel: true);

    // 关卡里根本没有的页：在关卡里不显示。
    private static WristbandPage NotInLevel(string id, SurfaceKind kind) => new(id, kind, ExistsInLevel: false);
}

/// <summary>手环的一个顶层页签：一组子页。</summary>
/// <param name="Id">稳定标识，也是文本键的后缀与页签图标的文件名。</param>
/// <param name="Pages">它下面的子页，顺序即从左到右。只有一页就是不分子页，没有子页签那一排。</param>
public sealed record WristbandTab(string Id, IReadOnlyList<WristbandPage> Pages)
{
    /// <summary>这个页签有没有子页签那一排。</summary>
    public bool HasSubTabs => Pages.Count > 1;

    /// <summary>这个页签自己在给定场合下怎么显示：取它下面最好的那一页。</summary>
    /// <remarks>
    /// 有一页可用它就可用；在的页都灰着它就灰着；一页都不在它就不显示。它是一组页的入口，入口的
    /// 状态只能由里面那几页推出来 —— 另给一格，就有两处在说「这个页签能不能点」。
    /// </remarks>
    public WristbandPageState StateIn(UIContext context)
    {
        var states = Pages.Select(p => p.StateIn(context)).ToList();
        return states.Contains(WristbandPageState.Available) ? WristbandPageState.Available
            : states.Contains(WristbandPageState.Disabled) ? WristbandPageState.Disabled
            : WristbandPageState.Hidden;
    }

    /// <summary>打开这个页签落在哪一页：第一个可用的子页。一页都用不了时是 <c>null</c>，页签本身点不开。</summary>
    /// <remarks>
    /// 基地里就是第一个子页。关卡里第一个子页可能正灰着，落在它上面等于打开一页用不了的东西，
    /// 所以往后找第一个可用的。
    /// </remarks>
    public WristbandPage? LandingIn(UIContext context) =>
        Pages.FirstOrDefault(p => p.StateIn(context) == WristbandPageState.Available);
}

/// <summary>手环的一个子页：「一页答一个问题」的那个页。</summary>
/// <param name="Id">稳定标识，也是文本键的后缀。整个手环里不重复。</param>
/// <param name="Kind">用途分类，决定它在关卡里可用还是灰着。</param>
/// <param name="ExistsInLevel">关卡里有没有这一页。没有的在关卡里不显示，例如仓库。</param>
public sealed record WristbandPage(string Id, SurfaceKind Kind, bool ExistsInLevel)
{
    /// <summary>这一页在给定场合下怎么显示。基地与城区里每一页都可用。</summary>
    public WristbandPageState StateIn(UIContext context) =>
        context != UIContext.Level ? WristbandPageState.Available
        : !ExistsInLevel ? WristbandPageState.Hidden
        : Kind.AvailableInLevel() ? WristbandPageState.Available
        : WristbandPageState.Disabled;
}

/// <summary>一页（或一个顶层页签）在某个场合下怎么显示。</summary>
public enum WristbandPageState
{
    /// <summary>可用：点得进去。</summary>
    Available,

    /// <summary>灰着：它在，只是这里用不了。</summary>
    Disabled,

    /// <summary>不显示：这个场合里根本没有这一页。</summary>
    Hidden,
}

/// <summary>界面所处的场合。只有这两种，因为视角规则只有两种、没有例外。</summary>
public enum UIContext
{
    /// <summary>俯视的基地与城区。经营侧操作在这里进行。</summary>
    Base,

    /// <summary>侧视关卡。手环的管理功能在这里不可用。</summary>
    Level,
}
