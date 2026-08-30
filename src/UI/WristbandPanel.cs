using Godot;
using Tinderhearth.Rules.Ui;

namespace Tinderhearth.UI;

/// <summary>
/// 手环容器骨架（`UI-6`）：标签页条加一块内容区。**页内内容不在本条范围。**
/// </summary>
/// <remarks>
/// 形态取「统一容器带标签页」由作者 2026-08-30 定。哪些页存在、各自在什么场合可用，都从
/// <see cref="Wristband"/> 读 —— 那是规则层，有单元测试盯着「关卡内只留查看类」。本类只把
/// 判定结果翻译成按钮的 <c>Disabled</c>。
///
/// 布局全靠容器：`MarginContainer`（安全边距）→ `VBoxContainer`（标签条在上、内容在下）→
/// `HBoxContainer`（标签按钮）。**没有一个绝对像素坐标** —— 正典要求如此，理由是 `expand`
/// 下逻辑宽度会变。
///
/// 文字暂时用引擎默认字体。像素字体与主题跟着 `UI-8` 落地（HUD 是第一个真要排版文字的界面），
/// 那时把 `Theme` 挂在本类的根节点上即可，不必改结构。
/// </remarks>
public partial class WristbandPanel : Control
{
    private readonly Dictionary<string, Button> _tabButtons = [];
    private Label _content = null!;
    private string _activeTab = "";

    /// <summary>当前场合。改它会立刻重算哪些标签页可用。</summary>
    public UiContext Context
    {
        get => _context;
        set
        {
            _context = value;
            RefreshAvailability();
        }
    }

    private UiContext _context = UiContext.Base;

    public override void _Ready()
    {
        var margin = new MarginContainer();
        margin.SetAnchorsPreset(LayoutPreset.FullRect);
        foreach (var side in new[] { "left", "top", "right", "bottom" })
        {
            margin.AddThemeConstantOverride($"margin_{side}", UiMetrics.SafeMargin);
        }
        AddChild(margin);

        var column = new VBoxContainer { Name = "Column" };
        column.AddThemeConstantOverride("separation", UiMetrics.ItemGap);
        margin.AddChild(column);

        var tabs = new HBoxContainer { Name = "Tabs" };
        tabs.AddThemeConstantOverride("separation", UiMetrics.ItemGap);
        column.AddChild(tabs);

        foreach (var tab in Wristband.Tabs)
        {
            var button = new Button
            {
                Name = tab.Id,
                // 文本键而不是中文字面量（`ENG-5` 的「全部文本外置」）。
                // 真文本由 TextCatalog 在接线时填，本骨架先显示键，缺文本一眼看得出来。
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
            Text = "（页内内容不在 UI-6 范围）",
        };
        column.AddChild(_content);

        RefreshAvailability();
    }

    /// <summary>切到某一页。不可用的页点不动，所以这里不必再判一次。</summary>
    private void Select(string tabId)
    {
        _activeTab = tabId;
        _content.Text = $"当前页 {tabId}";
    }

    /// <summary>
    /// 按场合刷新可用性，并处理一个容易漏的情况：**当前页在关卡里变得不可用时要退出它。**
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
