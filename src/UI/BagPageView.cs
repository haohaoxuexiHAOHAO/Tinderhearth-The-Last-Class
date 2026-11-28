using System.Globalization;
using Godot;
using Tinderhearth.Rules.Economy;
using Tinderhearth.Rules.Foundation.Text;
using Tinderhearth.Rules.Progression;
using Tinderhearth.Rules.UI;

namespace Tinderhearth.UI;

/// <summary>背包页从哪读、被拒时找谁说、改了背包之后告诉谁。基地那片地在就绪时交给游戏菜单。</summary>
/// <param name="Bag">现在那一份背包。每次打开现读：睡一觉之后世界状态换了一份，背包也跟着换。</param>
/// <param name="Icons">按物品标识与品质取图标。</param>
/// <param name="Palette">色板：稀有度框与品质点按档位声明的显示色染。</param>
/// <param name="Notify">被拒的那一下屏上出一行字（即时提示）。</param>
/// <param name="Changed">这一页挪了、排了、整理了或丢了背包里的东西之后叫一声：屏幕上的随身栏画的是背包上那一排，要跟着重画。</param>
public sealed record BagPageSource(
    Func<Bag> Bag, ItemIconResolver Icons, Palette Palette, Action<string> Notify, Action Changed);

/// <summary>
/// 游戏菜单里的背包那一页：深色厚框里一格对着背包网格的一格，右边一列是排序、筛选、一键整理、丢弃与还剩几个空格，下面一行写指着的那一格是什么。
/// </summary>
/// <remarks>
/// 画什么判在规则层（<see cref="BagPage"/>），按下去对背包做什么判在 <see cref="BagControls"/>，怎么排在 <see cref="BagPageLayout"/>；
/// 本类只把它们翻成节点，不设一个颜色、不定一个尺寸：格子各态、焦点框、选中框、稀有度框与品质点都是作者画的件，
/// 稀有度框与品质点按那一档声明的显示色染。格子由代码拼、不由作者摆，与随身栏同一个理由：一行几格就是背包网格的宽、
/// 格距归规则层、有测试管着，背包多了格只多几行。
///
/// 每一格是一个按钮，键盘与手柄靠焦点在格子与右边那一列之间走，鼠标指上去与焦点停上去是同一个样子（格子里那一圈亮边）。
/// 一堆画成一块：格子框挨着格子框，大东西那几格并成一个大框、图放正中，点到一块里的哪一格都是那一堆。
/// 挪格是点一格、再点目标格：第一下拿起那一堆（那一块的格子底色亮一档），之后指着哪里，那里就画出它要占的那一块，
/// 放得下与放不下用两张形状不同的件；第二下放过去 —— 空着就挪过去，是同标识的就合进去，
/// 压到别的东西就什么都不动、手上照旧拿着，可以再点别处；点回原来那一格是放回去。不互换（设计仓 design/背包系统.md 的
/// 「一堆放在哪：先合堆，再找第一块够大的空地，不能旋转」一节）。不做拖拽，两种操作方式因此走同一套（设计仓
/// spec/prd-items-in-bag.md 的「非目标」一节）。空格拿不起来。
/// 丢弃丢的是拿起来的那一堆，手上没东西时那个按钮是灰的；丢之前一律弹一道确认，丢不掉的那一样不弹、屏上一行字说清。
///
/// 下面那一行写鼠标指着的那一格，没指着就写焦点停着的那一格，都没有就写拿起来的那一堆：玩家读的是自己正对着的东西，
/// 拿起一堆再走到右边那一列去按丢弃时，那一行照旧写着手上是什么。
///
/// 被筛掉的那几格画成用不了的格子、不画里面的东西，点不到、焦点也停不上去：被筛掉的那几堆什么都不碰，
/// 界面这一侧让它们碰不到。页上先排随身栏那一排、再排网格；随身栏那几格左上角写着它在随身栏里是第几格：排序与整理
/// 不碰那一排，不标出来的话，排完了那几格没动看着像没排好。占不止一格的东西挪不进那一排，屏上一行字说清。
///
/// 排序、整理与换筛选都放下手上那一堆：排过之后那一格装的已经不是拿起来的那一堆。照新的先后摆不下全部时一格不动，
/// 屏上一行字说清。合上游戏菜单或换到别的页也放下，拿起来只是界面上的一个记号，什么都没挪。
///
/// 这一页每次 <see cref="Refresh"/> 都照背包的当前值重画：游戏菜单开着时世界停着，背包只在这一页自己手里变。
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
    private readonly BagControls _controls = new();
    private readonly List<Cell> _cells = [];
    private readonly Dictionary<int, Block> _blocks = [];
    private readonly Dictionary<BagSortKey, Button> _sortButtons = [];
    private readonly Control _grid;
    private readonly Control _cellLayer;
    private readonly Control _blockLayer;
    private readonly Control _numberLayer;
    private readonly NinePatchRect _preview;
    private readonly Button _category;
    private readonly Button _quality;
    private readonly Button _tidy;
    private readonly Button _discard;
    private readonly Label _freeSlots;
    private readonly Label _info;
    private readonly Texture2D _slotNormal;
    private readonly Texture2D _slotDisabled;
    private readonly Texture2D _slotFocus;
    private readonly NinePiece _frame;
    private readonly NinePiece _heldFrame;
    private readonly NinePiece _focusRing;
    private readonly NinePiece _rarityRing;
    private readonly NinePiece _dropFits;
    private readonly NinePiece _dropBlocked;
    private ConfirmDialog? _confirm;
    private int?[] _anchorOf = [];
    private int _columns;
    private int? _held;
    private int? _hovered;
    private int? _focused;
    private int? _asked;

    /// <summary>搭一页。素材资源、文本表、界面根与主题由游戏菜单给，背包从哪读由基地那片地给。</summary>
    /// <param name="art">素材资源：格子各态与几样框从这里取。</param>
    /// <param name="text">文本表：按钮上的字与下面那一行从这里取。</param>
    /// <param name="source">背包从哪读、被拒时找谁说、改了之后告诉谁。</param>
    /// <param name="ui">界面根：丢之前那一问登记在它上面、经它的导航栈开关。</param>
    /// <param name="theme">像素主题：那一问不在游戏菜单下面，接不到游戏菜单那一份，由这里转交。</param>
    public BagPageView(UIArt art, TextCatalog text, BagPageSource source, UIRoot ui, Theme theme)
    {
        _art = art ?? throw new ArgumentNullException(nameof(art));
        _text = text ?? throw new ArgumentNullException(nameof(text));
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _ui = ui ?? throw new ArgumentNullException(nameof(ui));
        _theme = theme ?? throw new ArgumentNullException(nameof(theme));
        _frame = Nine(art.KitSlotNormal, "kit/slot_normal");
        _heldFrame = Nine(art.KitSlotHeld, "kit/slot_held");
        _focusRing = Nine(art.KitSlotFocus, "kit/slot_focus");
        _rarityRing = Nine(art.KitRarityEdge, "kit/rarity_edge");
        _dropFits = Nine(art.WorldCellUsable, "world/cell_usable");
        _dropBlocked = Nine(art.WorldCellBlocked, "world/cell_blocked");
        _slotNormal = _frame.Texture!;
        _slotFocus = _focusRing.Texture!;
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

        // 格子那一片多大跟着背包网格走，搭格子时照那一份背包设（Rebuild）。四层从下往上：一格一个按钮（管点与焦点）、
        // 每一堆那一块、随身栏的格号、放下之前的预览；后三层不接鼠标，点到哪一格都落在下面那个按钮上。
        _grid = new Control { Name = "Grid" };
        frame.AddChild(_grid);
        _cellLayer = Layer("Cells");
        _blockLayer = Layer("Blocks");
        _numberLayer = Layer("CarryNumbers");
        _preview = new NinePatchRect { Name = "Preview", Visible = false, MouseFilter = MouseFilterEnum.Ignore };
        _grid.AddChild(_preview);

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
        var count = bag.CarryCount + bag.SlotCount;
        if (_cells.Count != count || _columns != bag.GridWidth)
        {
            Rebuild(bag);
        }

        _anchorOf = [.. Enumerable.Range(0, count).Select(i => BagPage.AnchorIndexOf(bag, i))];
        RebuildBlocks([.. BagPage.BlocksOf(bag).Where(b => _controls.Shows(bag, b.Index))]);
        for (var i = 0; i < _cells.Count; i++)
        {
            Paint(_cells[i], _controls.Shows(bag, i), _anchorOf[i] is { } at && _blocks.ContainsKey(at));
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

    /// <summary>
    /// 点一格：手上没东西就拿起它，拿着的就放过去；点回拿起的那一格是放回去。放不下时什么都不动，手上照旧拿着。
    /// </summary>
    private void Press(int slot)
    {
        if (_held is not { } held)
        {
            // 点到一块里的哪一格都拿得起那一堆；拿起时点的那一格记下来，放下时对准指着的那一格。
            if (_anchorOf[slot] is not null)
            {
                _held = slot;
            }

            ShowSelection();
            return;
        }

        if (held == slot)
        {
            _held = null;
            Refresh();
            return;
        }

        switch (_controls.Move(_source.Bag(), held, slot))
        {
            case BagMoveResult.Moved or BagMoveResult.Merged:
                _held = null;
                _source.Changed();
                break;
            case BagMoveResult.TooBigForCarry:
                _source.Notify(_text[BagPage.TooBigForCarryKey]);
                break;
            case BagMoveResult.Blocked:
                break;
        }

        Refresh();
    }

    private void SortBy(BagSortKey key) => Arranged(_controls.SortBy(_source.Bag(), key));

    private void Tidy() => Arranged(_controls.Tidy(_source.Bag()));

    /// <summary>排过或整理过之后：摆不下全部、一格没动时屏上一行字说清，摆好了叫随身栏重画。两种都放下手上那一堆。</summary>
    private void Arranged(BagArrangeResult result)
    {
        _held = null;
        if (result == BagArrangeResult.NoRoom)
        {
            _source.Notify(_text[BagPage.NoRoomToArrangeKey]);
        }
        else
        {
            _source.Changed();
        }

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
        if (_held is not { } held || ContentAt(held) is not { } content)
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
    /// 不在搭这一页时登记：这一页可能在界面根就绪之前就搭好了（游戏菜单的 <c>_Ready</c> 早于界面根的），那时弹窗那一层还没建。
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

    /// <summary>放下手上那一堆，忘掉指着与停着的那一格。游戏菜单合上或换到别的页时走这里。</summary>
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

    /// <summary>格数变了：清掉旧的，照这一份背包的大小重搭格子与随身栏的格号。拿着的、指着的与停着的都不再算数。</summary>
    private void Rebuild(Bag bag)
    {
        // 先摘下来再释放：释放要等这一帧末尾，留着的话这一帧新旧两批会叠在一起。
        foreach (var cell in _cells)
        {
            _cellLayer.RemoveChild(cell.Button);
            cell.Button.QueueFree();
        }

        foreach (var number in _numberLayer.GetChildren())
        {
            _numberLayer.RemoveChild(number);
            number.QueueFree();
        }

        _cells.Clear();
        _held = null;
        _hovered = null;
        _focused = null;
        _columns = bag.GridWidth;

        var rows = BagPageLayout.PageRows(bag.GridHeight, bag.CarryCount, _columns);
        _grid.CustomMinimumSize = new Vector2(BagPageLayout.GridWidth(_columns), BagPageLayout.GridHeight(rows));
        for (var i = 0; i < bag.CarryCount + bag.SlotCount; i++)
        {
            var (column, row) = BagPage.PositionOf(bag, i);
            var origin = BagPageLayout.CellOrigin(column, row);
            _cells.Add(Build(i, origin));
            if (i < bag.CarryCount)
            {
                AddCarryNumber(i, origin);
            }
        }
    }

    /// <summary>随身栏那一格左上角写它是第几格：写在每一块上面，那一格有东西时也看得见。</summary>
    private void AddCarryNumber(int index, PixelPoint origin)
    {
        var holder = new Control
        {
            Name = $"Carry{index}",
            MouseFilter = MouseFilterEnum.Ignore,
            Position = At(origin),
            Size = new Vector2(UIMetrics.Cell, UIMetrics.Cell),
        };
        _numberLayer.AddChild(holder);
        var key = new MiniText(_art, MiniText.Corner.TopLeft)
        {
            Name = "CarryNumber",
            Text = (index + 1).ToString(CultureInfo.InvariantCulture),
        };
        holder.AddChild(key);
        key.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect, LayoutPresetMode.Minsize, HudLayout.CellCornerInset);
    }

    /// <summary>照背包的当前值重搭每一块：看得见的每一堆一块，大东西那一块是格子框拉开的大框，图放在正中。</summary>
    private void RebuildBlocks(IReadOnlyList<BagBlock> blocks)
    {
        foreach (var block in _blocks.Values)
        {
            _blockLayer.RemoveChild(block.Root);
            block.Root.QueueFree();
        }

        _blocks.Clear();
        foreach (var block in blocks)
        {
            _blocks[block.Index] = BuildBlock(block);
        }
    }

    /// <summary>
    /// 一块：格子框（拿起来的那一堆换成亮一档的那一张）、稀有度那一圈、图、品质点、件数，最上面是焦点那一圈。
    /// </summary>
    /// <remarks>
    /// 稀有度那一圈紧贴图、焦点那一圈在格子框里面紧贴描边，大的那几块把同一张件按九宫格拉开（设计仓
    /// production/界面绘制约定.md）。件数在右下、品质点在左下，随身栏的格号在左上，三个角各放一样。
    /// </remarks>
    private Block BuildBlock(BagBlock block)
    {
        var size = BagPageLayout.BlockSize(block.Width, block.Height);
        var root = new Control
        {
            Name = $"Block{block.Index}",
            MouseFilter = MouseFilterEnum.Ignore,
            Position = At(BagPageLayout.CellOrigin(block.Column, block.Row)),
            Size = new Vector2(size.Width, size.Height),
        };
        _blockLayer.AddChild(root);

        var frame = Stretched(_frame, "Frame", Vector2.Zero, root.Size);
        root.AddChild(frame);

        var picture = BagPageLayout.PictureSize(block.Width, block.Height);
        var offset = At(BagPageLayout.PictureOffset(block.Width, block.Height));
        var ring = new Vector2(UIMetrics.CellInset, UIMetrics.CellInset);
        var rarity = Stretched(
            _rarityRing, "Rarity", offset - ring, new Vector2(picture.Width, picture.Height) + (2 * ring));
        rarity.Modulate = ColorOf(block.Content.RarityColor);
        root.AddChild(rarity);

        var look = _source.Icons(block.Content.ItemId, block.Content.QualityId);
        if (look.Texture is { } texture)
        {
            root.AddChild(new TextureRect
            {
                Name = "Icon",
                StretchMode = TextureRect.StretchModeEnum.Keep,
                MouseFilter = MouseFilterEnum.Ignore,
                Position = offset,
                Size = new Vector2(picture.Width, picture.Height),
                Texture = texture,
            });
        }
        else
        {
            // 图还是「暂无」：在图那一块上居中写那一样名字的第一个字（口径见 IconPlaceholder）。字体与字色跟游戏菜单的主题走，
            // 这里不设：深色的格子底上配浅色的正文字，那一对在主题里。
            root.AddChild(new Label
            {
                Name = "Placeholder",
                Text = look.Placeholder,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                MouseFilter = MouseFilterEnum.Ignore,
                Position = offset,
                Size = new Vector2(picture.Width, picture.Height),
            });
        }

        if (block.Content.QualityColor is { } quality)
        {
            var pipTexture = HudPlace.Piece(_art.KitPipLarge, "kit/pip_large");
            var pip = HudPlace.Picture(pipTexture, "kit/pip_large");
            pip.Name = "Pip";
            pip.Position = new Vector2(
                BagPageLayout.PipInset, size.Height - BagPageLayout.PipInset - pipTexture.GetHeight());
            pip.Modulate = ColorOf(quality);
            root.AddChild(pip);
        }

        if (block.Content.ShowsCount)
        {
            var count = new MiniText(_art, MiniText.Corner.BottomRight)
            {
                Name = "Count",
                Text = block.Content.Count.ToString(CultureInfo.InvariantCulture),
            };
            root.AddChild(count);
            count.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect, LayoutPresetMode.Minsize, HudLayout.CellCornerInset);
        }

        var focus = Stretched(_focusRing, "Focus", Vector2.Zero, root.Size);
        focus.Visible = false;
        root.AddChild(focus);
        return new Block(block, root, frame, focus);
    }

    /// <summary>
    /// 一格：按钮本身不画东西（各态都是空的样式盒），管点与焦点；上面叠一张格子框与焦点那一圈，那一格被一块盖着时都收起来，
    /// 由那一块画。
    /// </summary>
    private Cell Build(int index, PixelPoint origin)
    {
        var button = new Button
        {
            Name = $"Slot{index}",
            FocusMode = FocusModeEnum.All,
            Position = At(origin),
            CustomMinimumSize = new Vector2(UIMetrics.Cell, UIMetrics.Cell),
        };
        foreach (var state in (string[])["normal", "hover", "pressed", "hover_pressed", "disabled", "focus"])
        {
            button.AddThemeStyleboxOverride(state, new StyleBoxEmpty());
        }

        _cellLayer.AddChild(button);

        var slot = HudPlace.Picture(_slotNormal, "kit/slot_normal");
        slot.Name = "Slot";
        button.AddChild(slot);

        var focus = HudPlace.Picture(_slotFocus, "kit/slot_focus");
        focus.Name = "Focus";
        focus.Visible = false;
        button.AddChild(focus);

        var cell = new Cell(button, slot, focus);

        // 鼠标指着与焦点停着是同一个样子；两样都离开了才收起。下面那一行与放下之前的预览跟着换。
        button.FocusEntered += () =>
        {
            _focused = index;
            ShowFocus();
            ShowSelection();
        };
        button.FocusExited += () =>
        {
            if (_focused == index)
            {
                _focused = null;
            }

            ShowFocus();
            ShowSelection();
        };
        button.MouseEntered += () =>
        {
            _hovered = index;
            cell.Hovered = true;
            ShowFocus();
            ShowSelection();
        };
        button.MouseExited += () =>
        {
            if (_hovered == index)
            {
                _hovered = null;
            }

            cell.Hovered = false;
            ShowFocus();
            ShowSelection();
        };
        button.Pressed += () => Press(index);

        return cell;
    }

    /// <summary>
    /// 一格自己画什么：被一块盖着就什么都不画（那一块画）；空格只画格子；被筛掉的画成用不了的格子，点不到、焦点也停不上去。
    /// </summary>
    private void Paint(Cell cell, bool shown, bool covered)
    {
        cell.Button.Disabled = !shown;
        cell.Button.FocusMode = shown ? FocusModeEnum.All : FocusModeEnum.None;
        CursorSkin.ApplyButtonShape(cell.Button);
        cell.Slot.Texture = shown ? _slotNormal : _slotDisabled;
        cell.Slot.Visible = !covered;
        cell.Covered = covered;
    }

    /// <summary>
    /// 拿起来的那一块换成亮一档的格子框，丢弃跟着它灰与不灰；下面那一行写指着、停着或拿着的那一堆；拿着东西时，
    /// 指着的地方画出它要占的那一块。
    /// </summary>
    private void ShowSelection()
    {
        var heldBlock = _held is { } held && held < _anchorOf.Length ? _anchorOf[held] : null;
        foreach (var (index, block) in _blocks)
        {
            Dress(block.Frame, index == heldBlock ? _heldFrame : _frame);
        }

        _discard.Disabled = _held is null;
        CursorSkin.ApplyButtonShape(_discard);

        var slot = _hovered ?? _focused ?? _held;
        _info.Text = slot is { } at && at < _cells.Count && !_cells[at].Button.Disabled && ContentAt(at) is { } content
            ? BagPage.InfoLine(content, _source.Bag().Items, _text)
            : string.Empty;

        ShowPreview();
    }

    /// <summary>
    /// 拿着一堆指着别处时，要占的那一块上画四个角：放得下是够得到的那一张，放不下是够不到的那一张（形状不同，不只换颜色）。
    /// 指着拿起时点的那一格、被筛掉的格，或者手上没东西时不画。
    /// </summary>
    private void ShowPreview()
    {
        var pointed = _hovered ?? _focused;
        if (_held is not { } held || pointed is not { } at || at >= _cells.Count || _cells[at].Button.Disabled
            || _controls.Preview(_source.Bag(), held, at) is not { } preview)
        {
            _preview.Visible = false;
            return;
        }

        var origin = At(BagPageLayout.CellOrigin(preview.Column, preview.Row));
        var size = BagPageLayout.BlockSize(preview.Width, preview.Height);
        var inset = new Vector2(BagPageLayout.PreviewInset, BagPageLayout.PreviewInset);
        Dress(_preview, preview.Fits ? _dropFits : _dropBlocked);
        _preview.Position = origin + inset;
        _preview.Size = new Vector2(size.Width, size.Height) - (2 * inset);
        _preview.Visible = true;
    }

    /// <summary>
    /// 焦点那一圈：焦点停着或鼠标指着就亮；指着的那一格被一块盖着时亮的是那一整块；用不了的格不亮。
    /// </summary>
    private void ShowFocus()
    {
        var pointedBlocks = new HashSet<int>();
        for (var i = 0; i < _cells.Count; i++)
        {
            var cell = _cells[i];
            var pointed = !cell.Button.Disabled && (cell.Hovered || cell.Button.HasFocus());
            cell.Focus.Visible = pointed && !cell.Covered;
            if (pointed && cell.Covered && i < _anchorOf.Length && _anchorOf[i] is { } at)
            {
                pointedBlocks.Add(at);
            }
        }

        foreach (var (index, block) in _blocks)
        {
            block.Focus.Visible = pointedBlocks.Contains(index);
        }
    }

    /// <summary>页上第 <paramref name="index"/> 格被哪一堆盖着、画的是什么；空着或那一堆被筛掉了是 <c>null</c>。</summary>
    private BagCell? ContentAt(int index) =>
        index < _anchorOf.Length && _anchorOf[index] is { } at && _blocks.TryGetValue(at, out var block)
            ? block.Shape.Content
            : null;

    /// <summary>格子那一片里的一层：跟着那一片一样大，自己不接鼠标。</summary>
    private Control Layer(string name)
    {
        var layer = new Control { Name = name, MouseFilter = MouseFilterEnum.Ignore };
        _grid.AddChild(layer);
        layer.SetAnchorsPreset(LayoutPreset.FullRect);
        return layer;
    }

    /// <summary>取一张拉得开的件；缺图当场报，说清缺哪一张。</summary>
    private static NinePiece Nine(NinePiece? piece, string name)
    {
        _ = HudPlace.Piece(piece?.Texture, name);
        return piece!;
    }

    /// <summary>一张按九宫格拉开的件，边距照素材资源里那一格。</summary>
    private static NinePatchRect Stretched(NinePiece piece, string name, Vector2 position, Vector2 size)
    {
        var rect = new NinePatchRect { Name = name, MouseFilter = MouseFilterEnum.Ignore, Position = position, Size = size };
        Dress(rect, piece);
        return rect;
    }

    /// <summary>换一张件：图与九宫格边距一起换。</summary>
    private static void Dress(NinePatchRect rect, NinePiece piece)
    {
        rect.Texture = piece.Texture;
        rect.PatchMarginLeft = piece.Left;
        rect.PatchMarginTop = piece.Top;
        rect.PatchMarginRight = piece.Right;
        rect.PatchMarginBottom = piece.Bottom;
    }

    private static Vector2 At(PixelPoint point) => new(point.X, point.Y);

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

    private sealed class Cell(Button button, TextureRect slot, TextureRect focus)
    {
        public Button Button { get; } = button;
        public TextureRect Slot { get; } = slot;
        public TextureRect Focus { get; } = focus;
        public bool Hovered { get; set; }
        public bool Covered { get; set; }
    }

    private sealed class Block(BagBlock shape, Control root, NinePatchRect frame, NinePatchRect focus)
    {
        public BagBlock Shape { get; } = shape;
        public Control Root { get; } = root;
        public NinePatchRect Frame { get; } = frame;
        public NinePatchRect Focus { get; } = focus;
    }
}
