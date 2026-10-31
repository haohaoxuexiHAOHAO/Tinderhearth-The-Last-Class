using System.Globalization;
using Godot;
using Tinderhearth.Rules.Economy;
using Tinderhearth.Rules.Foundation.Text;
using Tinderhearth.Rules.Progression;
using Tinderhearth.Rules.UI;

namespace Tinderhearth.UI;

/// <summary>背包页从哪读、被拒时找谁说、改了背包之后告诉谁。农场在就绪时交给手环。</summary>
/// <param name="Bag">现在那一份背包。每次打开现读：睡一觉之后世界状态换了一份，背包也跟着换。</param>
/// <param name="Icons">按物品标识与品质取图标。</param>
/// <param name="Palette">色板：稀有度框与品质点按档位声明的显示色染。</param>
/// <param name="Notify">被拒的那一下屏上出一行字（即时提示）。</param>
/// <param name="Changed">这一页挪了、排了、整理了或丢了背包里的东西之后叫一声：随身栏是背包开头那几格，要跟着重画。</param>
public sealed record BagPageSource(
    Func<Bag> Bag, ItemIconResolver Icons, Palette Palette, Action<string> Notify, Action Changed);

/// <summary>
/// 手环里的背包那一页：深色厚框里一行四格，右边一列是排序、筛选、一键整理、丢弃与还剩几个空格，下面一行写指着的那一格是什么。
/// </summary>
/// <remarks>
/// 画什么判在规则层（<see cref="BagPage"/>），按下去对背包做什么判在 <see cref="BagControls"/>，怎么排在 <see cref="BagPageLayout"/>；
/// 本类只把它们翻成节点，不设一个颜色、不定一个尺寸：格子各态、焦点框、选中框、稀有度框与品质点都是作者画的件，
/// 稀有度框与品质点按那一档声明的显示色染。格子由代码拼、不由作者摆，与随身栏同一个理由：一行几格与格距归规则层、
/// 有测试管着，背包多了格只多几行。
///
/// 每一格是一个按钮，键盘与手柄靠焦点在格子与右边那一列之间走，鼠标指上去与焦点停上去是同一个样子（格子里那一圈亮边）。
/// 挪格是点一格、再点目标格：第一下拿起那一堆（外面套一圈选中框），第二下与目标格互换，点回原来那一格是放回去。
/// 不做拖拽，两种操作方式因此走同一套（设计仓 spec/prd-items-in-bag.md 的「非目标」一节）。空格拿不起来。
/// 丢弃丢的是拿起来的那一堆，手上没东西时那个按钮是灰的；丢之前一律弹一道确认，丢不掉的那一样不弹、屏上一行字说清。
///
/// 下面那一行写鼠标指着的那一格，没指着就写焦点停着的那一格，都没有就写拿起来的那一堆：玩家读的是自己正对着的东西，
/// 拿起一堆再走到右边那一列去按丢弃时，那一行照旧写着手上是什么。
///
/// 被筛掉的那几格画成用不了的格子、不画里面的东西，点不到、焦点也停不上去：被筛掉的那几堆什么都不碰，
/// 界面这一侧让它们碰不到。开头那几格是随身栏，左上角写着它在随身栏里是第几格：排序与整理不碰这几格，
/// 不标出来的话，排完了开头几格没动看着像没排好。
///
/// 排序、整理与换筛选都放下手上那一堆：排过之后那一格装的已经不是拿起来的那一堆。合上手环或换到别的页也放下，
/// 拿起来只是界面上的一个记号，什么都没挪。
///
/// 这一页每次 <see cref="Refresh"/> 都照背包的当前值重画：手环开着时世界停着，背包只在这一页自己手里变。
/// </remarks>
public sealed partial class BagPageView : VBoxContainer
{
    /// <summary>丢之前那一问是哪一个界面：挂在弹窗那一层，关卡里也开得了（背包是随身物）。</summary>
    public static readonly UISurface DiscardSurface = new("bag.discard", UILayer.Dialog, SurfaceKind.Carried);

    private readonly UIArt _art;
    private readonly TextCatalog _text;
    private readonly BagPageSource _source;
    private readonly UIRoot _ui;
    private readonly Theme _theme;
    private readonly BagControls _controls = new(CarrySlotBar.SlotCount);
    private readonly List<Cell> _cells = [];
    private readonly Dictionary<BagSortKey, Button> _sortButtons = [];
    private readonly GridContainer _grid;
    private readonly Button _category;
    private readonly Button _quality;
    private readonly Button _tidy;
    private readonly Button _discard;
    private readonly Label _freeSlots;
    private readonly Label _info;
    private readonly Texture2D _slotNormal;
    private readonly Texture2D _slotDisabled;
    private ConfirmDialog? _confirm;
    private IReadOnlyList<BagCell?> _shown = [];
    private int? _held;
    private int? _hovered;
    private int? _focused;
    private int? _asked;

    /// <summary>搭一页。素材资源、文本表、界面根与主题由手环给，背包从哪读由农场给。</summary>
    /// <param name="art">素材资源：格子各态与几样框从这里取。</param>
    /// <param name="text">文本表：按钮上的字与下面那一行从这里取。</param>
    /// <param name="source">背包从哪读、被拒时找谁说、改了之后告诉谁。</param>
    /// <param name="ui">界面根：丢之前那一问登记在它上面、经它的导航栈开关。</param>
    /// <param name="theme">像素主题：那一问不在手环下面，接不到手环那一份，由这里转交。</param>
    public BagPageView(UIArt art, TextCatalog text, BagPageSource source, UIRoot ui, Theme theme)
    {
        _art = art ?? throw new ArgumentNullException(nameof(art));
        _text = text ?? throw new ArgumentNullException(nameof(text));
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _ui = ui ?? throw new ArgumentNullException(nameof(ui));
        _theme = theme ?? throw new ArgumentNullException(nameof(theme));
        _slotNormal = HudPlace.Piece(art.KitSlotNormal, "kit/slot_normal");
        _slotDisabled = HudPlace.Piece(art.KitSlotDisabled, "kit/slot_disabled");
        Name = "BagPage";
        AddThemeConstantOverride("separation", UIMetrics.ItemGap);

        var top = new HBoxContainer { Name = "Top" };
        top.AddThemeConstantOverride("separation", UIMetrics.Grid);
        AddChild(top);

        // 不随右边那一列拉高：格子那一片多高只由格数定（BagPageLayout.FrameHeight）。
        var frame = new PanelContainer
        {
            Name = "Frame",
            ThemeTypeVariation = PixelTheme.BagFrame,
            SizeFlagsVertical = SizeFlags.ShrinkBegin,
        };
        top.AddChild(frame);

        _grid = new GridContainer { Name = "Grid", Columns = BagPageLayout.SlotsPerRow };
        _grid.AddThemeConstantOverride("h_separation", BagPageLayout.CellGap);
        _grid.AddThemeConstantOverride("v_separation", BagPageLayout.CellGap);
        frame.AddChild(_grid);

        // 这一列多高由 BagPageLayout.ControlsHeight 算：这里加一行，那边的行数跟着改。
        var controls = new VBoxContainer { Name = "Controls" };
        controls.AddThemeConstantOverride("separation", UIMetrics.ItemGap);
        top.AddChild(controls);

        // 当前排序键是按下去的那一个。不用按钮组：按组的话再按一次当前那个键什么都不做，而它该再排一次。
        var sort = Row(controls, "Sort");
        foreach (var key in Enum.GetValues<BagSortKey>())
        {
            var button = TextButton(sort, key.ToString(), _text[BagPage.SortKeyKeys[key]]);
            button.ToggleMode = true;
            button.Pressed += () => SortBy(key);
            _sortButtons[key] = button;
        }

        // 两个按钮上写的就是当前的筛选条件，按一下换到下一档。
        var filter = Row(controls, "Filter");
        filter.AddChild(new Label
        {
            Name = "Label",
            Text = _text[BagPage.FilterKey],
            ThemeTypeVariation = PixelTheme.PaperLabel,
            VerticalAlignment = VerticalAlignment.Center,
        });
        _category = TextButton(filter, "Category", string.Empty);
        _category.Pressed += () => ChangeFilter(_controls.NextCategory);
        _quality = TextButton(filter, "Quality", string.Empty);
        _quality.Pressed += () => ChangeFilter(_controls.NextQuality);

        var actions = Row(controls, "Actions");
        _tidy = TextButton(actions, "Tidy", _text[BagPage.TidyKey]);
        _tidy.Pressed += Tidy;
        _discard = TextButton(actions, "Discard", _text[BagPage.DiscardKey]);
        _discard.Pressed += AskDiscard;

        _freeSlots = new Label { Name = "FreeSlots", ThemeTypeVariation = PixelTheme.PaperLabel };
        controls.AddChild(_freeSlots);

        // 横跨整页：写在厚框下面、只有厚框那么宽的话，名字一长就把右边那一列往外顶。
        _info = new Label { Name = "Info", ThemeTypeVariation = PixelTheme.PaperLabel };
        AddChild(_info);

        VisibilityChanged += () =>
        {
            if (!IsVisibleInTree())
            {
                Forget();
            }
        };
    }

    /// <summary>照背包的当前值重画每一格、右边那一列与下面那一行。格数变了（背包升了级）就重搭格子。</summary>
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
            Paint(_cells[i], _shown[i], _controls.Shows(bag, i));
        }

        foreach (var (key, button) in _sortButtons)
        {
            button.SetPressedNoSignal(key == _controls.SortKey);
        }

        _category.Text = BagPage.CategoryChoiceLabel(_controls.Filter, bag.Items, _text);
        _quality.Text = BagPage.QualityChoiceLabel(_controls.Filter, bag.Items, _text);
        _freeSlots.Text = BagPage.FreeSlotsLine(bag, _text);
        ShowSelection();
    }

    // ── 按下去 ────────────────────────────────────────────────────────

    /// <summary>点一格：手上没东西就拿起它，拿着的就与它互换；点回拿起的那一格是放回去。</summary>
    private void Press(int slot)
    {
        if (_held is not { } held)
        {
            if (_shown[slot] is not null)
            {
                _held = slot;
            }

            ShowSelection();
            return;
        }

        _held = null;
        if (held != slot)
        {
            _controls.Move(_source.Bag(), held, slot);
            _source.Changed();
        }

        Refresh();
    }

    private void SortBy(BagSortKey key)
    {
        _held = null;
        _controls.SortBy(_source.Bag(), key);
        _source.Changed();
        Refresh();
    }

    private void Tidy()
    {
        _held = null;
        _controls.Tidy(_source.Bag());
        _source.Changed();
        Refresh();
    }

    /// <summary>换一档筛选。背包一件不动，所以不叫随身栏重画。</summary>
    private void ChangeFilter(Action<ItemCatalog> next)
    {
        _held = null;
        next(_source.Bag().Items);
        Refresh();
    }

    /// <summary>丢拿起来的那一堆：丢不掉的那一样屏上一行字说清、不弹那一问；丢得掉的先问，点了确认才丢。</summary>
    private void AskDiscard()
    {
        // 手上没东西时这个按钮是灰的，按不到这里。
        if (_held is not { } held || _shown[held] is not { } content)
        {
            return;
        }

        var items = _source.Bag().Items;
        if (BagPage.DiscardRejection(content, items, _text) is { } rejected)
        {
            _source.Notify(rejected);
            return;
        }

        _asked = held;
        Confirm().Ask(
            BagPage.DiscardAsk(content, items, _text),
            _text[BagPage.DiscardYesKey],
            _text[BagPage.DiscardNoKey],
            () => Discard(held));
    }

    private void Discard(int slot)
    {
        var result = _controls.Discard(_source.Bag(), slot);
        if (result != BagDiscardResult.Discarded)
        {
            throw new InvalidOperationException(
                $"背包第 {slot} 格（从 0 数）丢弃的结果是 {result} —— 问之前已经核过那一格有东西、那一样丢得掉，"
                    + "那一问开着时背包不会变");
        }

        _held = null;
        _source.Changed();
        Refresh();
    }

    /// <summary>丢之前那一问：第一次要丢时才搭、才登记。</summary>
    /// <remarks>
    /// 不在搭这一页时登记：这一页可能在界面根就绪之前就搭好了（手环的 <c>_Ready</c> 早于界面根的），那时弹窗那一层还没建。
    /// </remarks>
    private ConfirmDialog Confirm()
    {
        if (_confirm is null)
        {
            _confirm = new ConfirmDialog(DiscardSurface, _ui, _theme);
            _ui.Register(DiscardSurface, _confirm);
            _confirm.Closed += OnConfirmClosed;
        }

        return _confirm;
    }

    /// <summary>
    /// 那一问关上了：焦点回到问的那一格（丢掉了就是那个空出来的格），那一格看不见了就落在一键整理上。
    /// </summary>
    /// <remarks>推迟到这一帧末尾：那一问刚藏起来，焦点这一刻还在它的按钮上，引擎随后才放掉。</remarks>
    private void OnConfirmClosed()
    {
        var slot = _asked;
        _asked = null;
        Callable.From(() =>
        {
            if (!IsVisibleInTree())
            {
                return;
            }

            if (slot is { } at && at < _cells.Count && !_cells[at].Button.Disabled)
            {
                _cells[at].Button.GrabFocus();
            }
            else
            {
                _tidy.GrabFocus();
            }
        }).CallDeferred();
    }

    /// <summary>放下手上那一堆，忘掉指着与停着的那一格。手环合上或换到别的页时走这里。</summary>
    private void Forget()
    {
        _held = null;
        _hovered = null;
        _focused = null;
        foreach (var cell in _cells)
        {
            cell.Hovered = false;
        }

        if (_cells.Count > 0)
        {
            ShowSelection();
        }
    }

    // ── 搭格子 ────────────────────────────────────────────────────────

    /// <summary>格数变了：清掉旧的，按新的格数搭。拿着的、指着的与停着的都不再算数。</summary>
    private void Rebuild(int slotCount)
    {
        // 先摘下来再释放：释放要等这一帧末尾，留在格子那一片里的话，这一帧新旧两批格子会挤在一起排。
        foreach (var cell in _cells)
        {
            _grid.RemoveChild(cell.Button);
            cell.Button.QueueFree();
        }

        _cells.Clear();
        _held = null;
        _hovered = null;
        _focused = null;

        for (var i = 0; i < slotCount; i++)
        {
            _cells.Add(Build(i));
        }
    }

    /// <summary>
    /// 一格：按钮本身不画东西（各态都是空的样式盒），上面一层层叠作者画的件。
    /// </summary>
    /// <remarks>
    /// 叠的先后就是画的先后：格子、稀有度框、图标、品质点、件数、随身栏的格号、焦点框，最后是套在外面的选中框。
    /// 稀有度框紧贴图标那一圈、焦点框在格子里面紧贴描边那一圈、选中框在格子外面一圈，三样同时在也都看得出来
    /// （设计仓 production/界面绘制约定.md）。格号在左上、件数在右下、品质点在左下，三个角各放一样。
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

        var slot = HudPlace.Picture(_slotNormal, "kit/slot_normal");
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

        if (index < CarrySlotBar.SlotCount)
        {
            var key = new MiniText(_art, MiniText.Corner.TopLeft)
            {
                Name = "CarryNumber",
                Text = (index + 1).ToString(CultureInfo.InvariantCulture),
            };
            button.AddChild(key);
            key.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect, LayoutPresetMode.Minsize, HudLayout.CellCornerInset);
        }

        var focus = HudPlace.Picture(_art.KitSlotFocus, "kit/slot_focus");
        focus.Name = "Focus";
        focus.Visible = false;
        button.AddChild(focus);

        var select = HudPlace.Picture(_art.KitSlotSelect, "kit/slot_select");
        select.Name = "Select";
        select.Visible = false;
        select.Position = new Vector2(-UIMetrics.SelectOutset, -UIMetrics.SelectOutset);
        button.AddChild(select);

        var cell = new Cell(button, slot, rarity, icon, pip, count, focus, select);

        // 鼠标指着与焦点停着是同一个样子；两样都离开了才收起。下面那一行跟着换。
        button.FocusEntered += () =>
        {
            _focused = index;
            ShowFocus(cell);
            ShowSelection();
        };
        button.FocusExited += () =>
        {
            if (_focused == index)
            {
                _focused = null;
            }

            ShowFocus(cell);
            ShowSelection();
        };
        button.MouseEntered += () =>
        {
            _hovered = index;
            cell.Hovered = true;
            ShowFocus(cell);
            ShowSelection();
        };
        button.MouseExited += () =>
        {
            if (_hovered == index)
            {
                _hovered = null;
            }

            cell.Hovered = false;
            ShowFocus(cell);
            ShowSelection();
        };
        button.Pressed += () => Press(index);

        return cell;
    }

    /// <summary>把一格画成它装的那一堆；空格只画格子，被筛掉的画成用不了的格子、不画里面的东西。</summary>
    private void Paint(Cell cell, BagCell? content, bool shown)
    {
        cell.Button.Disabled = !shown;
        cell.Button.FocusMode = shown ? FocusModeEnum.All : FocusModeEnum.None;
        CursorSkin.ApplyButtonShape(cell.Button);
        cell.Slot.Texture = shown ? _slotNormal : _slotDisabled;
        ShowFocus(cell);

        var drawn = shown ? content : null;
        cell.Rarity.Visible = drawn is not null;
        cell.Icon.Visible = drawn is not null;
        cell.Pip.Visible = drawn?.QualityColor is not null;
        cell.Count.Text = drawn is { ShowsCount: true } many
            ? many.Count.ToString(CultureInfo.InvariantCulture)
            : string.Empty;

        if (drawn is null)
        {
            cell.Icon.Texture = null;
            return;
        }

        cell.Icon.Texture = _source.Icons(drawn.ItemId, drawn.QualityId);
        cell.Rarity.Modulate = ColorOf(drawn.RarityColor);
        if (drawn.QualityColor is { } quality)
        {
            cell.Pip.Modulate = ColorOf(quality);
        }
    }

    /// <summary>选中框套在拿起来的那一格外面，丢弃跟着它灰与不灰；下面那一行写指着、停着或拿着的那一格。</summary>
    private void ShowSelection()
    {
        for (var i = 0; i < _cells.Count; i++)
        {
            _cells[i].Select.Visible = i == _held;
        }

        _discard.Disabled = _held is null;
        CursorSkin.ApplyButtonShape(_discard);

        var slot = _hovered ?? _focused ?? _held;
        _info.Text = slot is { } at && at < _shown.Count && !_cells[at].Button.Disabled && _shown[at] is { } content
            ? BagPage.InfoLine(content, _source.Bag().Items, _text)
            : string.Empty;
    }

    /// <summary>格子里那一圈亮边：焦点停着或鼠标指着就亮；用不了的格不亮。</summary>
    private static void ShowFocus(Cell cell) =>
        cell.Focus.Visible = !cell.Button.Disabled && (cell.Hovered || cell.Button.HasFocus());

    /// <summary>档位声明的显示色是色板里一项的名字；载入物品时核过在色板里，取不到就是读的不是同一份色板。</summary>
    private Color ColorOf(string entry) =>
        _source.Palette.Entries.TryGetValue(entry, out var color)
            ? PixelTheme.ToColor(color)
            : throw new InvalidOperationException($"色板里没有「{entry}」—— 物品载入时应当已经核过");

    private static HBoxContainer Row(Container column, string name)
    {
        var row = new HBoxContainer { Name = name };
        row.AddThemeConstantOverride("separation", UIMetrics.ItemGap);
        column.AddChild(row);
        return row;
    }

    private static Button TextButton(HBoxContainer row, string name, string text)
    {
        var button = new Button { Name = name, Text = text, FocusMode = FocusModeEnum.All };
        CursorSkin.ApplyButtonShape(button);
        row.AddChild(button);
        return button;
    }

    private sealed class Cell(
        Button button, TextureRect slot, TextureRect rarity, TextureRect icon, TextureRect pip, MiniText count,
        TextureRect focus, TextureRect select)
    {
        public Button Button { get; } = button;
        public TextureRect Slot { get; } = slot;
        public TextureRect Rarity { get; } = rarity;
        public TextureRect Icon { get; } = icon;
        public TextureRect Pip { get; } = pip;
        public MiniText Count { get; } = count;
        public TextureRect Focus { get; } = focus;
        public TextureRect Select { get; } = select;
        public bool Hovered { get; set; }
    }
}
