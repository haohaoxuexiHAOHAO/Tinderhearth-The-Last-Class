using Godot;
using Tinderhearth.Platform;
using Tinderhearth.Rules.Foundation.Text;
using Tinderhearth.Rules.UI;

namespace Tinderhearth.UI;

/// <summary>
/// 手环面板的外壳：一圈厚框、压在外框顶边上的一排图标页签、当前那个页签的一排子页签、一块浅底内容区，
/// 外加一个页签名字的小窗。内容区里背包那一页画背包（<see cref="BagPageView"/>），其余各页还只写页名。
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
///
/// 它有两种来路。主场景里由代码建出来、登记到界面根上，素材资源、文本表与主题由主场景给；别的场景（农场）里由作者
/// 摆成界面根的子节点、在检查器里接素材资源，界面根就绪时把它挪到面板那一层、登记成手环那个界面（<see cref="IPlacedSurface"/>）。
/// 后一种没有人给文本表与主题，所以没给就自己拿：文本表与主场景读的是同一处（<see cref="ContentBootstrap.LoadText"/>），
/// 主题用同一份素材资源装。它在自己的 <c>_Ready</c> 里搭好全部内容、连好全部信号，被界面根挪动之后不再搭第二遍。
/// 那些信号都连在它自己与它的子节点之间、从不断开，所以挪动不丢 —— <see cref="IPlacedUI"/> 说会丢的，是出树时断开的那种。
///
/// 顶层页签只画了图标、不写字，也不另放一行当前页标题：鼠标指着一个页签、或者键盘与手柄的焦点停在它上面，
/// 停满一段时间就在它下面弹出写着名字的小窗（<c>ui.wristband.tab.*</c>）。两种操作方式走同一个小窗、同一个
/// 时长 —— 焦点与悬停是同一个样子（设计仓 production/界面绘制约定.md），而 Godot 自带的提示框只认鼠标，
/// 键盘与手柄停上去永远不弹，所以这里自己做。
///
/// 打开手环与直达三个页签的键也由它自己听：每帧问一次输入门面，按下去该开、该换页签还是该关判在规则层的
/// <see cref="WristbandKeys"/>，本类只照做，开关一律经界面根的导航栈。经门面问，睡下到醒来那一段闸关着时就按不开。
/// 它挂在面板那一层上，那一层在世界暂停时照跑，所以手环开着时同一个键关得上。
/// </remarks>
public partial class WristbandPanel : Control, IPlacedSurface
{
    private readonly Dictionary<string, Button> _tabButtons = [];
    private readonly Dictionary<string, Button> _pageButtons = [];
    private readonly Dictionary<string, HBoxContainer> _pageRows = [];
    private UIArt _art = null!;
    private TextCatalog _text = null!;
    private Control _shell = null!;
    private Label _content = null!;
    private PanelContainer _inset = null!;
    private BagPageSource? _bag;
    private BagPageView? _bagView;
    private PanelContainer _tabName = null!;
    private Label _tabNameText = null!;
    private Godot.Timer _tabNameDelay = null!;
    private Button? _tabNameFor;
    private bool _placingFocus;
    private WristbandTab? _activeTab;
    private WristbandPage? _activePage;
    private UIContext _context = UIContext.Base;
    private bool _built;
    private InputRouter _router = null!;
    private UIRoot _ui = null!;

    /// <summary>输入门面。打开手环与直达三个页签的键一律经它问，缺了当场报错。</summary>
    /// <remarks>摆进场景时在检查器里拖挂了 <see cref="InputRouter"/> 的那个节点；主场景由代码在挂上之前给。</remarks>
    [Export]
    public InputRouter? Router { get; set; }

    /// <summary>素材资源。页签上的图标从这里取，缺了或缺格当场报错并一次列全。</summary>
    /// <remarks>
    /// 摆进场景时在检查器里接 <c>assets/self-drawn/ui/ui_art.tres</c>，与随身栏接的是同一份；主场景由代码在挂上之前给。
    /// </remarks>
    [Export]
    public UIArt? Art { get; set; }

    /// <summary>文本表。子页签上的字从这里取。代码搭这个面板的那一方在挂上之前给；没给就在就绪时自己读一份。</summary>
    public TextCatalog? Catalog { get; set; }

    /// <summary>背包那一页从哪读背包。农场在就绪时给；没给的场景（主场景）里那一页照旧只写页名。</summary>
    /// <remarks>它不是检查器里的格子：背包是世界状态的一部分，由持有它的那一方交过来。</remarks>
    public BagPageSource? Bag
    {
        get => _bag;
        set
        {
            _bag = value;
            if (_built)
            {
                ShowPageContent();
            }
        }
    }

    /// <summary>这个面板挂在面板那一层。</summary>
    public UILayer Layer => Wristband.Surface.Layer;

    /// <summary>它就是手环那个界面：经导航栈开关，开着时世界停着。</summary>
    public UISurface Surface => Wristband.Surface;

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
        // 先停掉每帧回调，校验过了、搭好了再开：_Ready 抛出之后引擎并不会停掉这个节点，_Process 照旧每帧跑，
        // 说清缺了什么的那一条会被成百条空引用刷走（与随身栏同一个做法）。
        SetProcess(false);

        _art = UIArtGuard.Require(Art, $"{nameof(WristbandPanel)}（节点 {Name}）");
        _router = Router ?? throw new InvalidOperationException(
            $"{nameof(WristbandPanel)}（节点 {Name}）的 {nameof(Router)} 没接上 —— "
                + $"打开手环的键要经输入门面问（挂了 {nameof(InputRouter)} 的那个节点）");
        _ui = UIRoot.Above(this) ?? throw new InvalidOperationException(
            $"{nameof(WristbandPanel)}（节点 {Name}）不在界面根下面 —— 把它直接摆成挂了 {nameof(UIRoot)} 的那个节点的子节点");

        // 摆进场景的那一种没有人给文本表与主题：自己拿，与主场景同一个来源。不拿的话子页签上一片缺文本的占位，
        // 字也退回引擎默认字体，而后者不报错。
        _text = Catalog ?? ContentBootstrap.LoadText(ContentBootstrap.BuildCatalog());
        Theme ??= PixelTheme.Install(_art, out _);

        var center = new CenterContainer { Name = "Center" };
        center.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(center);

        var shell = new Control
        {
            Name = "Shell",
            CustomMinimumSize = new Vector2(WristbandLayout.ShellWidth, WristbandLayout.ShellHeight),
        };
        center.AddChild(shell);
        _shell = shell;

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
            button.Pressed += () =>
            {
                HideTabName();      // 点下去了就是认得它，名字不用再弹
                SelectTab(tab);
            };
            button.MouseEntered += () => ArmTabName(button, tab);
            button.MouseExited += () => DisarmTabName(button);
            button.FocusEntered += () =>
            {
                ShowFocusLook(button, focused: true);
                if (!_placingFocus)
                {
                    ArmTabName(button, tab);
                }
            };
            button.FocusExited += () =>
            {
                ShowFocusLook(button, focused: false);
                DisarmTabName(button);
            };
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
                pageButton.FocusEntered += () => ShowFocusLook(pageButton, focused: true);
                pageButton.FocusExited += () => ShowFocusLook(pageButton, focused: false);
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
        _inset = inset;

        _content = new Label { Name = "Content", ThemeTypeVariation = PixelTheme.PaperLabel };
        inset.AddChild(_content);

        // 页签名字的小窗加在外壳最后，所以画在页签与外框上面；它不接鼠标，指针移到它身上也不算离开页签。
        _tabName = new PanelContainer
        {
            Name = "TabName",
            ThemeTypeVariation = PixelTheme.WristbandTabName,
            Visible = false,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _tabNameText = new Label { Name = "Text", MouseFilter = MouseFilterEnum.Ignore };
        _tabName.AddChild(_tabNameText);
        shell.AddChild(_tabName);

        _tabNameDelay = new Godot.Timer { Name = "TabNameDelay", OneShot = true, WaitTime = TabNameDelaySeconds() };
        _tabNameDelay.Timeout += ShowTabName;
        AddChild(_tabNameDelay);

        // 面板一显示就把焦点放上去。不放的话手柄按方向键什么也不会发生：Godot 的焦点导航是从
        // 当前焦点找邻居，没有焦点就没有起点。键鼠点一下就有了焦点，所以这个坑只在手柄上出现。
        VisibilityChanged += OnVisibilityChanged;

        _built = true;
        RefreshStates();

        SetProcess(true);
    }

    /// <summary>每帧问一次门面：打开手环与直达三个页签的键有没有按下去。</summary>
    /// <remarks>
    /// 同一帧只认一个，先问的是 <see cref="WristbandKeys.Actions"/> 里排在前面的那个：两个都照做的话，
    /// 结果取决于问的先后，而那不该由玩家去猜。
    /// </remarks>
    public override void _Process(double delta)
    {
        foreach (var action in WristbandKeys.Actions)
        {
            if (_router.IsJustPressed(action))
            {
                Apply(WristbandKeys.Press(action, _ui.Navigation, _activeTab?.Id));
                return;
            }
        }
    }

    /// <summary>照规则层的结论开、换页签或关。开关经导航栈，世界停不停、HUD 藏不藏都由界面根照栈去设。</summary>
    private void Apply(WristbandKeyResult result)
    {
        switch (result.Outcome)
        {
            case WristbandKeyOutcome.Open:
                // 先换好页签再打开：一显示就把焦点放在当前那个页签上，换在后面焦点就落在旧的那个上。
                if (result.TabId is { } openOn)
                {
                    SelectTab(Wristband.TabById(openOn));
                }

                _ui.Open(Surface);
                break;

            case WristbandKeyOutcome.Switch:
                SelectTab(Wristband.TabById(result.TabId!));
                PlaceFocus();
                break;

            case WristbandKeyOutcome.Close:
                _ui.Close(Surface);
                break;

            case WristbandKeyOutcome.Ignored:
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(result), $"没有这个结论：{result.Outcome}");
        }
    }

    /// <summary>
    /// 显示时给手柄一个焦点落点、把背包那一页照当前值重画；隐藏时不留残余焦点，也收起页签名字的小窗。
    /// </summary>
    private void OnVisibilityChanged()
    {
        if (!IsVisibleInTree())
        {
            HideTabName();
            return;
        }

        ShowPageContent();
        PlaceFocus();
    }

    /// <summary>把焦点放在当前那个页签上。</summary>
    /// <remarks>
    /// 当前那个一定是可用的（见 <see cref="RefreshStates"/>）：关卡里那些整个用不了的页签是禁用的，
    /// 而禁用按钮拿不到焦点，落在它身上等于没落。
    /// </remarks>
    private void PlaceFocus()
    {
        if (_activeTab is { } tab)
        {
            // 这一下是面板自己把焦点放上去，不是玩家停在页签上：不挡的话，每次打开手环什么都不动，
            // 过一会儿当前页签底下就弹出它的名字。这个挡法靠的是焦点进入那个信号在 GrabFocus 里当场发出：
            // 引擎源码 scene/main/viewport.cpp 的 _gui_control_grab_focus 是这么写的，本工程没实测。
            _placingFocus = true;
            _tabButtons[tab.Id].GrabFocus();
            _placingFocus = false;
        }
    }

    /// <summary>
    /// 键盘与手柄的焦点停在一个页签上时，让它显示成鼠标指着的那一张：焦点期间把它「没选中」那一格换成悬停那一格。
    /// </summary>
    /// <remarks>
    /// 作者画的焦点那一态是一整张换了底色的页签，而 Godot 把焦点那一格叠在当前那一态上面画，叠上去会盖掉选中那一张，
    /// 所以主题里焦点那一格是空的（<see cref="PixelTheme"/>），在这里换。只换「没选中」那一格：选中与不可用各画各的，
    /// 选中的页签拿到焦点时照旧是选中的样子。焦点与悬停因此是同一个样子（设计仓 production/界面绘制约定.md）。
    /// </remarks>
    private static void ShowFocusLook(Button button, bool focused)
    {
        if (focused)
        {
            button.AddThemeStyleboxOverride("normal", button.GetThemeStylebox("hover"));
        }
        else
        {
            button.RemoveThemeStyleboxOverride("normal");
        }
    }

    /// <summary>
    /// 鼠标或焦点停到一个页签上：先把名字换好，计时，停满了再弹（<see cref="ShowTabName"/>）。
    /// </summary>
    private void ArmTabName(Button button, WristbandTab tab)
    {
        _tabNameFor = button;
        _tabNameText.Text = _text[TabKey(tab)];
        _tabName.Visible = false;
        _tabNameDelay.Start();
    }

    /// <summary>鼠标或焦点离开一个页签。只收它自己那一份：另一种操作方式这时可能正停在别的页签上。</summary>
    private void DisarmTabName(Button button)
    {
        if (_tabNameFor == button)
        {
            HideTabName();
        }
    }

    private void HideTabName()
    {
        _tabNameDelay.Stop();
        _tabName.Visible = false;
        _tabNameFor = null;
    }

    /// <summary>
    /// 停满了：小窗贴着那个页签的下面，左边与它对齐；放不下就往左挪，不出外壳。
    /// </summary>
    private void ShowTabName()
    {
        if (_tabNameFor is not { } button || !button.IsVisibleInTree())
        {
            return;
        }

        _tabName.Visible = true;
        _tabName.ResetSize();
        var at = button.GlobalPosition - _shell.GlobalPosition;
        var x = Math.Min(at.X, WristbandLayout.ShellWidth - _tabName.Size.X);
        _tabName.Position = new Vector2(x, at.Y + button.Size.Y + UIMetrics.ItemGap);
    }

    /// <summary>
    /// 停多久才弹：读项目设置里提示框那一项（<c>gui/timers/tooltip_delay_sec</c>），在 Godot 的项目设置里改。
    /// </summary>
    /// <remarks>
    /// 借引擎自己那一项，而不另开一格：它答的就是「停多久弹提示」，将来别处用了自带的提示框，两边一样长。
    /// </remarks>
    private static double TabNameDelaySeconds() =>
        ProjectSettings.GetSetting("gui/timers/tooltip_delay_sec").AsDouble();

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
        ShowPageContent();
    }

    /// <summary>
    /// 内容区里放什么：背包那一页有了从哪读背包，就画背包；其余各页，与还没接上背包的主场景，照旧只写页名。
    /// </summary>
    /// <remarks>背包那一页第一次露面时才搭，之后每次露面都照背包的当前值重画。</remarks>
    private void ShowPageContent()
    {
        var showsBag = _activePage?.Id == Wristband.BagPageId && _bag is not null;
        if (showsBag && _bagView is null)
        {
            _bagView = new BagPageView(_art, _text, _bag!, _ui, Theme);
            _inset.AddChild(_bagView);
        }

        _content.Visible = !showsBag;
        if (_bagView is not null)
        {
            _bagView.Visible = showsBag;
            if (showsBag)
            {
                _bagView.Refresh();
            }
        }
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
        CursorSkin.ApplyButtonShape(button);
    }

    private static string PageKey(WristbandPage page) => $"ui.wristband.page.{page.Id}";

    private static string TabKey(WristbandTab tab) => $"ui.wristband.tab.{tab.Id}";
}
