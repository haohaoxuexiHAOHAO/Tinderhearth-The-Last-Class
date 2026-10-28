using Godot;
using Tinderhearth.Rules.Foundation.Text;
using Tinderhearth.Rules.UI;

namespace Tinderhearth.UI;

/// <summary>
/// 手环面板的外壳：一圈厚框、压在外框顶边上的一排图标页签、当前那个页签的一排子页签、一块浅底内容区。
/// 每一页里面显示什么还没做。
/// </summary>
/// <remarks>
/// 有哪些页、各自在什么场合怎么显示，都从规则层的 <see cref="Wristband"/> 读，本类只把结论翻译成按钮的
/// <c>Disabled</c> 与 <c>Visible</c>：灰着的页按钮禁用，关卡里没有的页按钮藏起来。
///
/// 排法照作者画的模板与 <see cref="WristbandLayout"/>：外壳 560×328 那一块（含顶上那排页签）在屏幕里居中，
/// 页签最下面一行压在外框顶边那条线上，所以页签那一排在外框后面加、画在它上面。样子全从主题取
/// （<see cref="PixelTheme"/> 用素材资源组装的那几种类型变体），本类不设一个颜色、不载一张图 ——
/// 只有页签上那张图标按页签的标识从素材资源里取。
///
/// 子页签上的字从文本表取（<c>ui.wristband.page.*</c>），缺了显成缺文本的占位，一眼看得出来。
/// </remarks>
public partial class WristbandPanel : Control
{
    private readonly Dictionary<string, Button> _tabButtons = [];
    private readonly Dictionary<string, Button> _pageButtons = [];
    private readonly Dictionary<string, HBoxContainer> _pageRows = [];
    private UIArt _art = null!;
    private TextCatalog _text = null!;
    private Label _content = null!;
    private WristbandTab? _activeTab;
    private WristbandPage? _activePage;
    private UIContext _context = UIContext.Base;
    private bool _built;

    /// <summary>素材资源。页签上的图标从这里取。代码搭这个面板的那一方在挂上之前给。</summary>
    public UIArt? Art { get; set; }

    /// <summary>文本表。子页签上的字从这里取。代码搭这个面板的那一方在挂上之前给。</summary>
    public TextCatalog? Catalog { get; set; }

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
        _art = Art ?? throw new InvalidOperationException(
            $"{nameof(WristbandPanel)}（节点 {Name}）的 {nameof(Art)} 没给 —— 页签上的图标从素材资源取");
        _text = Catalog ?? throw new InvalidOperationException(
            $"{nameof(WristbandPanel)}（节点 {Name}）的 {nameof(Catalog)} 没给 —— 子页签上的字从文本表取");

        var center = new CenterContainer { Name = "Center" };
        center.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(center);

        var shell = new Control
        {
            Name = "Shell",
            CustomMinimumSize = new Vector2(WristbandLayout.ShellWidth, WristbandLayout.ShellHeight),
        };
        center.AddChild(shell);

        // 外框从页签那一排的最后一行起：页签最下面一行就是外框顶边那条线。
        var frame = new PanelContainer { Name = "Frame", ThemeTypeVariation = PixelTheme.WristbandFrame };
        frame.SetAnchorsPreset(LayoutPreset.FullRect);
        frame.OffsetTop = WristbandLayout.TabSize - WristbandLayout.TabOverlap;
        shell.AddChild(frame);

        var column = new VBoxContainer { Name = "Column" };
        column.AddThemeConstantOverride("separation", 0);
        frame.AddChild(column);

        var tabs = new HBoxContainer { Name = "Tabs" };
        tabs.AddThemeConstantOverride("separation", WristbandLayout.TabGap);
        tabs.OffsetLeft = WristbandLayout.TabRowInsetX;
        shell.AddChild(tabs);

        var tabGroup = new ButtonGroup();
        foreach (var tab in Wristband.Tabs)
        {
            var button = new Button
            {
                Name = tab.Id,
                ThemeTypeVariation = PixelTheme.WristbandTab,
                ToggleMode = true,
                ButtonGroup = tabGroup,
                Icon = TabIcon(tab, disabled: false),
                IconAlignment = HorizontalAlignment.Center,
                CustomMinimumSize = new Vector2(WristbandLayout.TabSize, WristbandLayout.TabSize),
                FocusMode = FocusModeEnum.All,      // 手柄靠焦点移动导航
            };
            button.Pressed += () => SelectTab(tab);
            tabs.AddChild(button);
            _tabButtons[tab.Id] = button;

            // 每个页签一排子页签，同一时刻只显示当前那一排；不分子页的页签那一排是空的、一直藏着。
            var row = new HBoxContainer { Name = $"Pages_{tab.Id}", Visible = false };
            row.AddThemeConstantOverride("separation", WristbandLayout.TabGap);
            column.AddChild(row);
            _pageRows[tab.Id] = row;
            if (!tab.HasSubTabs)
            {
                continue;
            }

            var pageGroup = new ButtonGroup();
            foreach (var page in tab.Pages)
            {
                var pageButton = new Button
                {
                    Name = page.Id,
                    ThemeTypeVariation = PixelTheme.WristbandSubTab,
                    ToggleMode = true,
                    ButtonGroup = pageGroup,
                    Text = _text[PageKey(page)],
                    FocusMode = FocusModeEnum.All,
                };
                pageButton.Pressed += () => SelectPage(page);
                row.AddChild(pageButton);
                _pageButtons[page.Id] = pageButton;
            }
        }

        var inset = new PanelContainer
        {
            Name = "Inset",
            ThemeTypeVariation = PixelTheme.ContentInset,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        column.AddChild(inset);

        _content = new Label { Name = "Content", ThemeTypeVariation = PixelTheme.PaperLabel };
        inset.AddChild(_content);

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
        if (tab is not null)
        {
            _tabButtons[tab.Id].SetPressedNoSignal(true);
        }

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
        if (page is not null && _pageButtons.TryGetValue(page.Id, out var button))
        {
            button.SetPressedNoSignal(true);
        }

        _content.Text = page is null ? "" : _text[PageKey(page)];
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
            var state = tab.StateIn(Context);
            Show(_tabButtons[tab.Id], state);
            _tabButtons[tab.Id].Icon = TabIcon(tab, disabled: state == WristbandPageState.Disabled);
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

    /// <summary>页签上那张图标：文件名就是页签的标识，灰着的时候换置灰版。</summary>
    private Texture2D TabIcon(WristbandTab tab, bool disabled)
    {
        var name = disabled ? $"{tab.Id}_disabled" : tab.Id;
        return _art.Cells().FirstOrDefault(c => c.Group == "tab" && c.Name == name).Texture
            ?? throw new InvalidOperationException(
                $"素材资源里没有页签「{tab.Id}」的图标 ui/tab/{name}.png —— 模板里页签那一排画了它之后，"
                    + "生成器加一项、再重跑 tools/ui_art_resource.py");
    }

    private static void Show(Button button, WristbandPageState state)
    {
        button.Visible = state != WristbandPageState.Hidden;
        button.Disabled = state == WristbandPageState.Disabled;
    }

    private static string PageKey(WristbandPage page) => $"ui.wristband.page.{page.Id}";
}
