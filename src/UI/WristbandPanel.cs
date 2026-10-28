using Godot;
using Tinderhearth.Rules.UI;

namespace Tinderhearth.UI;

/// <summary>
/// 手环面板的骨架：一排顶层页签、当前那个页签的一排子页签、一块内容区。每一页里面显示什么还没做。
/// </summary>
/// <remarks>
/// 有哪些页、各自在什么场合怎么显示，都从规则层的 <see cref="Wristband"/> 读，本类只把结论翻译成按钮的
/// <c>Disabled</c> 与 <c>Visible</c>：灰着的页按钮禁用，关卡里没有的页按钮藏起来。
///
/// 布局全靠容器套起来：<c>MarginContainer</c> 留安全边距，<c>VBoxContainer</c> 从上往下放两排页签与内容，
/// <c>HBoxContainer</c> 排每一排的按钮。这里没有一个绝对像素坐标，因为逻辑宽度会变。
///
/// 按钮与内容区上放的是文本键，不是中文字面量 —— 全部文案都外置在文本表里；骨架先显示键，缺文案一眼
/// 看得出来。顶层页签换成图标、外加一行当前页标题归设计仓台账 `UI-44`。
///
/// 主题由外面挂进来（<see cref="Control.Theme"/>），本类不自己载字体。
/// </remarks>
public partial class WristbandPanel : Control
{
    private readonly Dictionary<string, Button> _tabButtons = [];
    private readonly Dictionary<string, Button> _pageButtons = [];
    private readonly Dictionary<string, HBoxContainer> _pageRows = [];
    private Label _content = null!;
    private WristbandTab? _activeTab;
    private WristbandPage? _activePage;
    private UIContext _context = UIContext.Base;
    private bool _built;

    /// <summary>当前场合。改它会立刻重算哪些页签可用、哪些灰着、哪些不显示。</summary>
    public UIContext Context
    {
        get => _context;
        set
        {
            _context = value;
            RefreshStates();
        }
    }

    public override void _Ready()
    {
        var margin = new MarginContainer();
        margin.SetAnchorsPreset(LayoutPreset.FullRect);
        foreach (var side in new[] { "left", "top", "right", "bottom" })
        {
            margin.AddThemeConstantOverride($"margin_{side}", UIMetrics.SafeMargin);
        }
        AddChild(margin);

        var column = new VBoxContainer { Name = "Column" };
        column.AddThemeConstantOverride("separation", UIMetrics.ItemGap);
        margin.AddChild(column);

        var tabs = Row("Tabs");
        column.AddChild(tabs);

        foreach (var tab in Wristband.Tabs)
        {
            var button = TabButton(tab.Id, TabKey(tab));
            button.Pressed += () => SelectTab(tab);
            tabs.AddChild(button);
            _tabButtons[tab.Id] = button;

            // 每个页签一排子页签，同一时刻只显示当前那一排；不分子页的页签那一排是空的、一直藏着。
            var row = Row($"Pages_{tab.Id}");
            row.Visible = false;
            column.AddChild(row);
            _pageRows[tab.Id] = row;
            if (!tab.HasSubTabs)
            {
                continue;
            }

            foreach (var page in tab.Pages)
            {
                var pageButton = TabButton(page.Id, PageKey(page));
                pageButton.Pressed += () => SelectPage(page);
                row.AddChild(pageButton);
                _pageButtons[page.Id] = pageButton;
            }
        }

        _content = new Label
        {
            Name = "Content",
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        column.AddChild(_content);

        // 面板一显示就把焦点放上去。不放的话手柄按方向键什么也不会发生：Godot 的焦点导航是从
        // 当前焦点找邻居，没有焦点就没有起点。键鼠点一下就有了焦点，所以这个坑只在手柄上出现。
        VisibilityChanged += OnVisibilityChanged;

        _built = true;
        RefreshStates();
    }

    /// <summary>
    /// 显示时给手柄一个焦点落点，隐藏时不留残余焦点。
    /// </summary>
    /// <remarks>
    /// 焦点落在当前那个页签上，而当前那个一定是可用的（见 <see cref="RefreshStates"/>）：关卡里那些整个
    /// 用不了的页签是禁用的，而禁用按钮拿不到焦点，落在它身上等于没落。
    /// </remarks>
    private void OnVisibilityChanged()
    {
        if (IsVisibleInTree() && _activeTab is { } tab)
        {
            _tabButtons[tab.Id].GrabFocus();
        }
    }

    /// <summary>切到某个顶层页签，落在它第一个可用的子页。传 <c>null</c> 是一个页签都用不了。</summary>
    private void SelectTab(WristbandTab? tab)
    {
        _activeTab = tab;
        foreach (var (tabId, row) in _pageRows)
        {
            row.Visible = tab is not null && tabId == tab.Id && tab.HasSubTabs;
        }

        SelectPage(tab?.LandingIn(Context));
    }

    /// <summary>切到某一页。灰着与藏起来的页按钮点不动，所以这里不必再判一次。</summary>
    private void SelectPage(WristbandPage? page)
    {
        _activePage = page;
        _content.Text = page is null ? "" : PageKey(page);
    }

    /// <summary>
    /// 按当前场合刷新每个页签与每一页怎么显示，顺带处理一个容易漏的情况：正开着的那一页变得用不了时要退出它。
    /// </summary>
    /// <remarks>
    /// 正开着的那个页签整个用不了，就换到第一个用得了的页签；页签还用得了、只是那一页用不了，就退回那个
    /// 页签的落点。两样都不做的话，场合一变，玩家就停在一页灰着的东西上。
    /// </remarks>
    private void RefreshStates()
    {
        if (!_built)
        {
            return;     // 还没搭好；_Ready 末尾会再算一遍
        }

        foreach (var tab in Wristband.Tabs)
        {
            Show(_tabButtons[tab.Id], tab.StateIn(Context));
        }

        foreach (var page in Wristband.Pages)
        {
            if (_pageButtons.TryGetValue(page.Id, out var button))
            {
                Show(button, page.StateIn(Context));
            }
        }

        if (_activeTab is not { } active || active.StateIn(Context) != WristbandPageState.Available)
        {
            SelectTab(Wristband.Tabs.FirstOrDefault(t => t.StateIn(Context) == WristbandPageState.Available));
        }
        else if (_activePage?.StateIn(Context) != WristbandPageState.Available)
        {
            SelectPage(active.LandingIn(Context));
        }
    }

    private static void Show(Button button, WristbandPageState state)
    {
        button.Visible = state != WristbandPageState.Hidden;
        button.Disabled = state == WristbandPageState.Disabled;
    }

    private static HBoxContainer Row(string name)
    {
        var row = new HBoxContainer { Name = name };
        row.AddThemeConstantOverride("separation", UIMetrics.ItemGap);
        return row;
    }

    private static Button TabButton(string name, string text) => new()
    {
        Name = name,
        Text = text,
        FocusMode = FocusModeEnum.All,      // 手柄靠焦点移动导航
    };

    private static string TabKey(WristbandTab tab) => $"ui.wristband.tab.{tab.Id}";

    private static string PageKey(WristbandPage page) => $"ui.wristband.page.{page.Id}";
}
