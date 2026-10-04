using Godot;
using Tinderhearth.Rules.UI;

namespace Tinderhearth.UI;

/// <summary>
/// 手环面板的骨架：一条标签页按钮加一块内容区。每一页里面显示什么还没做。
/// </summary>
/// <remarks>
/// 有哪些页、各自在什么场合可用，都从规则层的 <see cref="Wristband"/> 读，本类只把结论翻译成
/// 按钮的 <c>Disabled</c>。
///
/// 布局全靠容器套起来：<c>MarginContainer</c> 留安全边距，<c>VBoxContainer</c> 把标签条放上、
/// 内容放下，<c>HBoxContainer</c> 排标签按钮。这里没有一个绝对像素坐标，因为逻辑宽度会变。
///
/// 主题由外面挂进来（<see cref="Control.Theme"/>），本类不自己载字体。
/// </remarks>
public partial class WristbandPanel : Control
{
    private readonly Dictionary<string, Button> _tabButtons = [];
    private Label _content = null!;
    private string _activeTab = "";

    /// <summary>当前场合。改它会立刻重算哪些标签页可用。</summary>
    public UIContext Context
    {
        get => _context;
        set
        {
            _context = value;
            RefreshAvailability();
        }
    }

    private UIContext _context = UIContext.Base;

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

        var tabs = new HBoxContainer { Name = "Tabs" };
        tabs.AddThemeConstantOverride("separation", UIMetrics.ItemGap);
        column.AddChild(tabs);

        foreach (var tab in Wristband.Tabs)
        {
            var button = new Button
            {
                Name = tab.Id,
                // 这里放的是文本键，不是中文字面量 —— 全部文案都外置在文本表里。
                // 真文案由 TextCatalog 在接线时填，骨架先显示键，缺文案一眼看得出来。
                Text = $"ui.wristband.{tab.Id}",
                FocusMode = FocusModeEnum.All,      // 手柄靠焦点移动导航
            };
            button.Pressed += () => Select(tab.Id);
            tabs.AddChild(button);
            _tabButtons[tab.Id] = button;
        }

        _content = new Label
        {
            Name = "Content",
            SizeFlagsVertical = SizeFlags.ExpandFill,
            Text = "（这一页里面显示什么还没做）",
        };
        column.AddChild(_content);

        // 面板一显示就把焦点放上去。不放的话手柄按方向键什么也不会发生：Godot 的焦点导航是从
        // 当前焦点找邻居，没有焦点就没有起点。键鼠点一下就有了焦点，所以这个坑只在手柄上出现。
        VisibilityChanged += OnVisibilityChanged;

        RefreshAvailability();
    }

    /// <summary>
    /// 显示时给手柄一个焦点落点，隐藏时不留残余焦点。
    /// </summary>
    /// <remarks>
    /// 焦点落在第一个可用的标签页上，不是第一个标签页：关卡里那些安排别人的页是禁用的，
    /// 而禁用按钮拿不到焦点，落在它身上等于没落。
    /// </remarks>
    private void OnVisibilityChanged()
    {
        if (!IsVisibleInTree())
        {
            return;
        }

        foreach (var tab in Wristband.Tabs)
        {
            if (tab.AvailableIn(Context))
            {
                _tabButtons[tab.Id].GrabFocus();
                return;
            }
        }
    }

    /// <summary>切到某一页。不可用的页按钮是禁用的、点不动，所以这里不必再判一次。</summary>
    private void Select(string tabId)
    {
        _activeTab = tabId;
        _content.Text = $"当前页 {tabId}";
    }

    /// <summary>
    /// 按当前场合刷新每一页可不可用，顺带处理一个容易漏的情况：正打开着的那页变得不可用时要退出它。
    /// </summary>
    private void RefreshAvailability()
    {
        foreach (var tab in Wristband.Tabs)
        {
            _tabButtons[tab.Id].Disabled = !tab.AvailableIn(Context);
        }

        if (_activeTab.Length > 0 && !Wristband.IsEnabled(_activeTab, Context))
        {
            _activeTab = "";
            _content.Text = "（这一页在关卡内不可用）";
        }
    }
}
