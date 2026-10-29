using Godot;
using Tinderhearth.Rules.UI;

namespace Tinderhearth.UI;

/// <summary>
/// 随身栏那一条格子的屏幕部分：画出每格里是什么，并把玩家换格的按键接到规则层。
/// </summary>
/// <remarks>
/// 本文件里不自己定任何「量」，与 <see cref="LevelHud"/> 同一条纪律：尺寸与放置从
/// <see cref="HudLayout"/> 与 <see cref="UIMetrics"/> 取，显示什么从 <see cref="CarrySlotBar"/> 取，
/// 颜色从 <see cref="HudPalette"/> 取，位置一律走锚点预设、不写 <c>Position</c>。
///
/// 它坐在 <see cref="HudBlock.ActionBar"/> 那一块上，与关卡的技能栏是同一块、同一条栏，摆的是基地那一套的放置
/// （<see cref="UIContext.Base"/>）：同宽、同样贴右，关卡里在顶上、基地里在底边，理由在 <see cref="HudLayout"/>。
/// 随身栏没有新开 HUD 块，所以 HUD 一共占多少屏、会不会压住角色，照旧由 <see cref="HudLayout"/> 那几条测试判。
/// 它只在基地里有：关卡里那一侧明确后置（设计仓 design/界面系统.md 的「常驻那一层：战斗与经营的抬头显示」）。
///
/// 它不接管输入，所以按它不暂停世界：按一下只换手上那一格，不弹面板。因此鼠标过滤是忽略，
/// 输入是每帧问门面而不是等信号，与 <c>src/World/Farm/FarmField.cs</c> 的做法一致。物品图标由
/// 往格子里装东西的那一方给（<see cref="Icons"/>）—— 物品定义是会被 mod 换掉的内容，界面层不持有
/// 一张「标识对图标」的表。
///
/// 作者把它摆进场景：摆成 <see cref="UIRoot"/> 的子节点，接上 <see cref="Router"/> 与 <see cref="Art"/>；界面根就绪时
/// 把它挪到常驻显示那一层（<see cref="IPlacedUI"/>）。要读手上那一格的玩法节点在自己的检查器里
/// 接它。格子那几层仍由本类自己搭，与 <see cref="LevelHud"/> 同一个理由：HUD 的尺寸与放置归规则层、
/// 由测试判，作者摆的是这一个节点，不是格子。
/// </remarks>
public sealed partial class CarrySlotBarView : Control, IPlacedUI
{
    private const UIContext Here = UIContext.Base;

    // 随身栏本体在构造时就有，不等 _Ready：读它、往里装东西的玩法节点可能排在本节点前面就绪，
    // 那时它得已经在。全空是它接上背包之前的正常样子（见 CarrySlotBar.Empty）。
    private readonly CarrySlotBar _bar = CarrySlotBar.Empty();
    private readonly List<Cell> _cells = [];

    private InputRouter _router = null!;
    private UIArt _art = null!;
    private Control? _frame;

    /// <summary>输入门面。换格的按键一律经它问，缺了当场报错。</summary>
    [Export]
    public InputRouter? Router { get; set; }

    /// <summary>素材资源。那条栏、格子与选中框都从它取，缺了或缺格当场报错并一次列全。</summary>
    /// <remarks>摆进场景时接的是 <c>assets/self-drawn/ui/ui_art.tres</c>，与主场景接的是同一份。</remarks>
    [Export]
    public UIArt? Art { get; set; }

    /// <summary>拿一个物品标识换一张图标；没有图标就返回 <c>null</c>，那一格画空框。</summary>
    public delegate Texture2D? CarryIconResolver(string itemId);

    /// <summary>
    /// 图标从哪来。往格子里装东西的那一方在装之前设好。
    /// </summary>
    /// <remarks>
    /// 它不是检查器里的格子：标识对图标的那张表属于内容，由装东西的那一方持有。没设就往格子里装了
    /// 东西，画到那一格时当场报错 —— 不悄悄画成空框，否则「没给图标」看起来就像「这件东西没有图标」。
    /// 全空的格子用不到它，所以接上背包之前不设也不报错。
    /// </remarks>
    public CarryIconResolver? Icons { get; set; }

    /// <summary>这个界面挂在常驻显示那一层。</summary>
    public UILayer Layer => UILayer.Hud;

    /// <summary>
    /// 那条随身栏本体。玩法侧从这里读「玩家手上是哪一格」。
    /// </summary>
    /// <remarks>
    /// 暴露的是规则层那个对象，不是复制出来的下标：复制一份就有两处说「现在选第几格」，
    /// 而两处对不上的时候没有东西查得出来。
    /// </remarks>
    public CarrySlotBar Bar => _bar;

    /// <summary>这一块现在占屏幕的哪个矩形。可与 <see cref="HudLayout.RectOf"/> 的预测比对。</summary>
    public Rect2 BlockRect => _frame?.GetGlobalRect() ?? default;

    /// <summary>第 N 格现在显示的件数文字。为 1 或为空时是空串。</summary>
    public string CountText(int index) => _cells[index].Count.Text;

    /// <summary>第 N 格现在是不是选中态：那一格外面那一圈选中框显不显示。</summary>
    public bool IsLit(int index) => _cells[index].Select.Visible;

    /// <summary>第 N 格的图标显不显示。空格不显示。</summary>
    public bool IconShown(int index) => _cells[index].Icon.Visible;

    public override void _Ready()
    {
        // 先停掉每帧回调，校验过了再开。_Ready 抛出之后引擎并不会停掉这个节点，_Process 照旧
        // 每帧跑而门面还是 null —— 于是说清缺了什么的那一条会被成百条空引用刷走。
        SetProcess(false);

        _router = Router ?? throw new InvalidOperationException(
            $"{nameof(CarrySlotBarView)}（节点 {Name}）的 {nameof(Router)} 没接上 —— "
                + $"换格的按键要经输入门面问（挂了 {nameof(InputRouter)} 的那个节点）");
        _art = UIArtGuard.Require(Art, $"{nameof(CarrySlotBarView)}（节点 {Name}）");

        // 铺满所属层、块靠锚点贴角。必须用 SetAnchorsAndOffsetsPreset：对已经在树里的节点调
        // SetAnchorsPreset 会改写偏移、保住那个 0×0 的矩形，而且不报错（同 LevelHud 那一段）。
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;

        Build();
        AnchorBlock();
        Refresh();

        SetProcess(true);
    }

    // 尺寸一变就重摆那一块（幂等）。不摆的话贴右下角的块会拿 0×0 的父矩形算偏移、落到屏幕外去，
    // 而那只表现为「少了一块」。订阅跟着进出树走而不写在 _Ready 里：界面根会把本节点挪到常驻
    // 显示那一层，挪的时候它离开再进一次树，_Ready 却不再跑（见 IPlacedUI）。
    public override void _EnterTree() => Resized += AnchorBlock;

    public override void _ExitTree() => Resized -= AnchorBlock;

    /// <summary>
    /// 每帧问一次门面：有没有换格。
    /// </summary>
    /// <remarks>
    /// 用 <c>_Process</c> 而不是 <c>_PhysicsProcess</c>：换格不是玩法推进，它只改一个显示用的
    /// 下标，不该被顿帧那个闸门管着。
    ///
    /// 输入一律经门面，不直接调 <c>Input.IsActionPressed</c>：实测事件被消费掉之后轮询照旧
    /// 返回 true，只有门面那一侧的遮挡判定是完整的。
    /// </remarks>
    public override void _Process(double delta)
    {
        var before = _bar.SelectedIndex;

        // 键鼠：数字键直选。先判直选再判挪位 —— 两者绑在不同设备族上，同一帧本来不会都来，
        // 但顺序写死了就不用回答「同一帧都来时听谁的」。
        for (var i = 0; i < InputActions.CarrySlots.Count; i++)
        {
            if (_router.IsJustPressed(InputActions.CarrySlots[i]))
            {
                _bar.Select(i);
            }
        }

        // 手柄：挪一格，绕回。
        if (_router.IsJustPressed(InputActions.CarryNext))
        {
            _bar.SelectNext();
        }

        if (_router.IsJustPressed(InputActions.CarryPrev))
        {
            _bar.SelectPrevious();
        }

        if (_bar.SelectedIndex != before)
        {
            Refresh();
        }
    }

    /// <summary>内容换了之后叫一声。选中的是哪一格不会变，那由 <see cref="CarrySlotBar"/> 保证。</summary>
    public void OnContentChanged() => Refresh();

    // ── 搭那一块 ────────────────────────────────────────────────────────

    /// <summary>
    /// 格子照那条画好的栏排：格与格之间空 <see cref="HudLayout.ActionBarCellGap"/>，与关卡的技能栏逐格对齐。
    /// </summary>
    /// <remarks>
    /// 框取 <see cref="HudPlace.Frame"/>，外框尺寸是 <see cref="HudLayout.SizeOf"/>，所以它与关卡那条技能栏逐像素一样大。
    /// 格数与技能位同源，格子正好铺满内容区、不留要居中的空隙，于是两种内容的格子落在同一批横坐标上，换场景时
    /// 格位不跳。图标放在格子里往里让一个格内边距的那一格里，与技能位同一个画法。
    /// </remarks>
    private void Build()
    {
        var panel = HudPlace.Frame(HudBlock.ActionBar, Here, _art);
        AddChild(panel);
        _frame = panel;

        var row = new HBoxContainer { Name = "Content" };
        row.AddThemeConstantOverride("separation", HudLayout.ActionBarCellGap);
        panel.AddChild(row);

        for (var i = 0; i < CarrySlotBar.SlotCount; i++)
        {
            var cell = new Control
            {
                Name = $"Slot{i}",
                CustomMinimumSize = new Vector2(UIMetrics.Cell, UIMetrics.Cell),
                MouseFilter = MouseFilterEnum.Ignore,
            };
            row.AddChild(cell);

            // 格子本身铺满这一格。
            var slot = HudPlace.Picture(_art.KitSlotNormal, "kit/slot_normal");
            slot.Name = "Slot";
            slot.SetAnchorsPreset(LayoutPreset.FullRect);
            cell.AddChild(slot);

            var inner = new Control { Name = "Inner", MouseFilter = MouseFilterEnum.Ignore };
            cell.AddChild(inner);
            inner.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect, LayoutPresetMode.Minsize, UIMetrics.CellInset);

            var icon = new TextureRect
            {
                Name = "Icon",
                StretchMode = TextureRect.StretchModeEnum.Keep,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            icon.SetAnchorsPreset(LayoutPreset.FullRect);
            inner.AddChild(icon);

            // 件数角标压在图标右下，不另占一行：这一块只有一格高，加一行会把块撑高，
            // 而块一高就压掉画面上留给角色的地方。
            var count = new Label
            {
                Name = "Count",
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            count.SetAnchorsPreset(LayoutPreset.FullRect);
            inner.AddChild(count);

            // 选中框画在格子外面一圈：铺满这一格，再四边各往外让出那一圈。最后加，压在最上面。
            var select = HudPlace.Picture(_art.KitSlotSelect, "kit/slot_select");
            select.Name = "Select";
            select.Visible = false;
            cell.AddChild(select);
            select.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect, LayoutPresetMode.Minsize, -UIMetrics.SelectOutset);

            _cells.Add(new Cell(select, icon, count));
        }
    }

    /// <summary>把那一块照基地那一套的放置贴到右下角。这里没有一个坐标，逻辑宽度撑开时它自动跟着走。</summary>
    /// <remarks>进树早于 <c>_Ready</c>，那时块还没搭，尺寸变了也没东西可摆。</remarks>
    private void AnchorBlock()
    {
        if (_frame is { } frame)
        {
            HudPlace.Apply(frame, HudLayout.PlacementOf(HudBlock.ActionBar, Here));
        }
    }

    /// <summary>
    /// 把每一格画上去。
    /// </summary>
    /// <remarks>
    /// 选中态是格子外面那一圈选中框，不是换色，也不只改亮度。相反的两个意思要有不同的东西看得见（见设计仓
    /// production/像素绘制原则.md 的「明度先于色相」一节），这一对的意思是「这一格是不是我手上那个」。
    ///
    /// 件数为 1 时不画数字，这条规则在 <see cref="CarrySlot.ShowsCount"/>，本处不再判一次。
    /// </remarks>
    private void Refresh()
    {
        for (var i = 0; i < _cells.Count; i++)
        {
            var slot = _bar.Slots[i];
            var cell = _cells[i];
            var lit = i == _bar.SelectedIndex;

            cell.Select.Visible = lit;

            // 空格不画图标，但框照画 —— 格位固定不动，玩家才记得住哪一格是哪一格。
            cell.Icon.Texture = slot.IsEmpty ? null : IconOf(slot.ItemId);
            cell.Icon.Visible = cell.Icon.Texture is not null;

            cell.Count.Text = slot.ShowsCount ? slot.Count.ToString() : string.Empty;
            cell.Count.AddThemeColorOverride("font_color",
                PixelTheme.ToColor(lit ? HudPalette.Hot : HudPalette.Ink));
        }
    }

    private Texture2D? IconOf(string itemId) =>
        Icons is { } icons
            ? icons(itemId)
            : throw new InvalidOperationException(
                $"{nameof(CarrySlotBarView)}（节点 {Name}）的 {nameof(Icons)} 没设，格子里却装了「{itemId}」—— "
                    + "往随身栏里装东西的那一方要在装之前给一个按物品标识取图标的办法"
                    + "（这件东西没有图标就返回 null，那一格只画框）");

    private sealed record Cell(TextureRect Select, TextureRect Icon, Label Count);
}
