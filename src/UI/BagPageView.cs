using System.Globalization;
using Godot;
using Tinderhearth.Rules.Economy;
using Tinderhearth.Rules.Foundation.Text;
using Tinderhearth.Rules.UI;

namespace Tinderhearth.UI;

/// <summary>背包页从哪读：背包、图标与色板。农场在就绪时交给手环。</summary>
/// <param name="Bag">现在那一份背包。每次打开现读：睡一觉之后世界状态换了一份，背包也跟着换。</param>
/// <param name="Icons">按物品标识与品质取图标。</param>
/// <param name="Palette">色板：稀有度框与品质点按档位声明的显示色染。</param>
public sealed record BagPageSource(Func<Bag> Bag, ItemIconResolver Icons, Palette Palette);

/// <summary>
/// 手环里的背包那一页：深色厚框里一行四格，右边写还剩几个空格，下面一行写选中那一格是什么。
/// </summary>
/// <remarks>
/// 画什么判在规则层（<see cref="BagPage"/>），怎么排在 <see cref="BagPageLayout"/>；本类只把它们翻成节点，不设一个颜色、
/// 不定一个尺寸：格子、焦点框、选中框、稀有度框与品质点都是作者画的件，稀有度框与品质点按那一档声明的显示色染。
/// 格子由代码拼、不由作者摆，与随身栏同一个理由：一行几格与格距归规则层、有测试管着，背包多了格只多几行。
///
/// 每一格是一个按钮：键盘与手柄靠焦点在格子之间走，鼠标指上去与焦点停上去是同一个样子（格子里那一圈亮边）。
/// 按下去就选中它：外面套一圈选中框，下面那一行写它是什么。空格也选得中，那一行就空着。
///
/// 这一页自己不存东西，每次 <see cref="Refresh"/> 都照背包的当前值重画：手环开着时世界停着，背包在那期间不会变。
/// </remarks>
public sealed partial class BagPageView : HBoxContainer
{
    private readonly UIArt _art;
    private readonly TextCatalog _text;
    private readonly BagPageSource _source;
    private readonly List<Cell> _cells = [];
    private readonly GridContainer _grid;
    private readonly Label _freeSlots;
    private readonly Label _info;
    private IReadOnlyList<BagCell?> _shown = [];
    private int? _selected;

    /// <summary>搭一页。素材资源与文本表由手环给，背包从哪读由农场给。</summary>
    public BagPageView(UIArt art, TextCatalog text, BagPageSource source)
    {
        _art = art;
        _text = text;
        _source = source;
        Name = "BagPage";
        AddThemeConstantOverride("separation", UIMetrics.Grid);

        var left = new VBoxContainer { Name = "Left" };
        left.AddThemeConstantOverride("separation", UIMetrics.ItemGap);
        AddChild(left);

        var frame = new PanelContainer
        {
            Name = "Frame",
            ThemeTypeVariation = PixelTheme.BagFrame,
            SizeFlagsHorizontal = SizeFlags.ShrinkBegin,
        };
        left.AddChild(frame);

        _grid = new GridContainer { Name = "Grid", Columns = BagPageLayout.SlotsPerRow };
        _grid.AddThemeConstantOverride("h_separation", BagPageLayout.CellGap);
        _grid.AddThemeConstantOverride("v_separation", BagPageLayout.CellGap);
        frame.AddChild(_grid);

        _info = new Label { Name = "Info", ThemeTypeVariation = PixelTheme.PaperLabel };
        left.AddChild(_info);

        _freeSlots = new Label { Name = "FreeSlots", ThemeTypeVariation = PixelTheme.PaperLabel };
        AddChild(_freeSlots);
    }

    /// <summary>照背包的当前值重画每一格、空格数与选中那一行。格数变了（背包升了级）就重搭格子。</summary>
    public void Refresh()
    {
        var bag = _source.Bag();
        _shown = BagPage.CellsOf(bag);
        if (_cells.Count != _shown.Count)
        {
            Rebuild(_shown.Count);
        }

        for (var i = 0; i < _cells.Count; i++)
        {
            Paint(_cells[i], _shown[i]);
        }

        _freeSlots.Text = BagPage.FreeSlotsLine(bag, _text);
        ShowSelection();
    }

    /// <summary>格数变了：清掉旧的，按新的格数搭。选中位超出新格数就不再选中。</summary>
    private void Rebuild(int slotCount)
    {
        // 先摘下来再释放：释放要等这一帧末尾，留在格子那一片里的话，这一帧新旧两批格子会挤在一起排。
        foreach (var cell in _cells)
        {
            _grid.RemoveChild(cell.Button);
            cell.Button.QueueFree();
        }

        _cells.Clear();
        if (_selected >= slotCount)
        {
            _selected = null;
        }

        for (var i = 0; i < slotCount; i++)
        {
            _cells.Add(Build(i));
        }
    }

    /// <summary>
    /// 一格：按钮本身不画东西（各态都是空的样式盒），上面一层层叠作者画的件。
    /// </summary>
    /// <remarks>
    /// 叠的先后就是画的先后：格子、稀有度框、图标、品质点、件数、焦点框，最后是套在外面的选中框。稀有度框紧贴图标那一圈、
    /// 焦点框在格子里面紧贴描边那一圈、选中框在格子外面一圈，三样同时在也都看得出来（设计仓 production/界面绘制约定.md）。
    /// </remarks>
    private Cell Build(int index)
    {
        var button = new Button
        {
            Name = $"Slot{index}",
            FocusMode = FocusModeEnum.All,
            CustomMinimumSize = new Vector2(UIMetrics.Cell, UIMetrics.Cell),
        };
        foreach (var state in (string[])["normal", "hover", "pressed", "hover_pressed", "disabled", "focus"])
        {
            button.AddThemeStyleboxOverride(state, new StyleBoxEmpty());
        }

        _grid.AddChild(button);

        var slot = HudPlace.Picture(_art.KitSlotNormal, "kit/slot_normal");
        slot.Name = "Slot";
        button.AddChild(slot);

        var rarity = HudPlace.Picture(_art.KitRarityEdge, "kit/rarity_edge");
        rarity.Name = "Rarity";
        button.AddChild(rarity);

        var icon = new TextureRect
        {
            Name = "Icon",
            StretchMode = TextureRect.StretchModeEnum.Keep,
            MouseFilter = MouseFilterEnum.Ignore,
            Position = new Vector2(UIMetrics.CellInset, UIMetrics.CellInset),
            Size = new Vector2(UIMetrics.IconSmall, UIMetrics.IconSmall),
        };
        button.AddChild(icon);

        var pipTexture = HudPlace.Piece(_art.KitPipLarge, "kit/pip_large");
        var pip = HudPlace.Picture(pipTexture, "kit/pip_large");
        pip.Name = "Pip";
        pip.Position = new Vector2(BagPageLayout.PipInset, UIMetrics.Cell - BagPageLayout.PipInset - pipTexture.GetHeight());
        button.AddChild(pip);

        var count = new MiniText(_art, MiniText.Corner.BottomRight) { Name = "Count" };
        button.AddChild(count);
        count.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect, LayoutPresetMode.Minsize, HudLayout.CellCornerInset);

        var focus = HudPlace.Picture(_art.KitSlotFocus, "kit/slot_focus");
        focus.Name = "Focus";
        focus.Visible = false;
        button.AddChild(focus);

        var select = HudPlace.Picture(_art.KitSlotSelect, "kit/slot_select");
        select.Name = "Select";
        select.Visible = false;
        select.Position = new Vector2(-UIMetrics.SelectOutset, -UIMetrics.SelectOutset);
        button.AddChild(select);

        var cell = new Cell(button, rarity, icon, pip, count, focus, select);

        // 鼠标指着与焦点停着是同一个样子；两样都离开了才收起。
        button.FocusEntered += () => ShowFocus(cell);
        button.FocusExited += () => ShowFocus(cell);
        button.MouseEntered += () => ShowFocus(cell, hovered: true);
        button.MouseExited += () => ShowFocus(cell, hovered: false);
        button.Pressed += () =>
        {
            _selected = index;
            ShowSelection();
        };

        return cell;
    }

    /// <summary>把一格画成它装的那一堆；空格只画格子。</summary>
    private void Paint(Cell cell, BagCell? content)
    {
        cell.Rarity.Visible = content is not null;
        cell.Icon.Visible = content is not null;
        cell.Pip.Visible = content?.QualityColor is not null;
        cell.Count.Text = content is { ShowsCount: true } shown
            ? shown.Count.ToString(CultureInfo.InvariantCulture)
            : string.Empty;

        if (content is null)
        {
            cell.Icon.Texture = null;
            return;
        }

        cell.Icon.Texture = _source.Icons(content.ItemId, content.QualityId);
        cell.Rarity.Modulate = ColorOf(content.RarityColor);
        if (content.QualityColor is { } quality)
        {
            cell.Pip.Modulate = ColorOf(quality);
        }
    }

    /// <summary>选中框套在选中那一格外面，下面那一行写它是什么；空格或没选中时那一行空着。</summary>
    private void ShowSelection()
    {
        for (var i = 0; i < _cells.Count; i++)
        {
            _cells[i].Select.Visible = i == _selected;
        }

        _info.Text = _selected is { } index && index < _shown.Count && _shown[index] is { } content
            ? BagPage.InfoLine(content, _source.Bag().Items, _text)
            : string.Empty;
    }

    /// <summary>格子里那一圈亮边：焦点停着或鼠标指着就亮。</summary>
    private static void ShowFocus(Cell cell, bool? hovered = null)
    {
        if (hovered is { } now)
        {
            cell.Hovered = now;
        }

        cell.Focus.Visible = cell.Hovered || cell.Button.HasFocus();
    }

    /// <summary>档位声明的显示色是色板里一项的名字；载入物品时核过在色板里，取不到就是读的不是同一份色板。</summary>
    private Color ColorOf(string entry) =>
        _source.Palette.Entries.TryGetValue(entry, out var color)
            ? PixelTheme.ToColor(color)
            : throw new InvalidOperationException($"色板里没有「{entry}」—— 物品载入时应当已经核过");

    private sealed class Cell(
        Button button, TextureRect rarity, TextureRect icon, TextureRect pip, MiniText count, TextureRect focus, TextureRect select)
    {
        public Button Button { get; } = button;
        public TextureRect Rarity { get; } = rarity;
        public TextureRect Icon { get; } = icon;
        public TextureRect Pip { get; } = pip;
        public MiniText Count { get; } = count;
        public TextureRect Focus { get; } = focus;
        public TextureRect Select { get; } = select;
        public bool Hovered { get; set; }
    }
}
