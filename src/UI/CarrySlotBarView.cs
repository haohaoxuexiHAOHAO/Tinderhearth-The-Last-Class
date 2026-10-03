using Godot;
using Tinderhearth.Rules.UI;

namespace Tinderhearth.UI;

/// <summary>
/// 随身栏那条六格的屏幕部分（`UI-34`）。
/// </summary>
/// <remarks>
/// **本文件里一个「量」都没有**，与 <see cref="LevelHud"/> 同一条纪律：尺寸与放置从
/// <see cref="HudLayout"/> 与 <see cref="UIMetrics"/> 取，显示什么从 <see cref="CarrySlotBar"/> 取，
/// 颜色从 <see cref="HudPalette"/> 取，位置一律走锚点预设、不出现 <c>Position</c>。
///
/// **它坐在 <see cref="HudBlock.ActionBar"/> 那一块上，与关卡的技能位是同一块。** 俯视经营场景里
/// 没有技能位，所以那一块在那里正好空着给它 —— 于是随身栏**没有新开 HUD 块**，那条「不许压角色
/// 可读区」的既有硬判据输入一个字没变。
///
/// **它不接管输入，所以按它不暂停世界。** 判据在正典那条「弹界面接管输入就暂停世界」：随身栏是
/// 常驻那一层的东西，按一下就换手上那一格，不弹面板。所以这个 <see cref="Control"/> 的
/// <see cref="Control.MouseFilter"/> 是忽略，而输入是**每帧问门面**、不是靠信号 ——
/// 与 `src/World/Farm/FarmField.cs` 问那一下的形状相同。
///
/// **它不知道物品长什么样。** 图标由调用方给的解析器答（<see cref="CarryIconResolver"/>）——
/// 物品定义是内容、会被 mod 换掉（[ADR-0022]），所以界面层不许持有一张「标识对图标」的表。
/// </remarks>
public sealed partial class CarrySlotBarView : Control
{
    /// <summary>格框素材。**与技能位共用同一批** —— 两处都是 16px 的框，它们本来就是同一样东西。</summary>
    private const string SlotPathFormat = "res://assets/placeholder/ui/skill-slot-{0}.png";

    private readonly InputRouter _router;
    private readonly CarrySlotBar _bar;
    private readonly CarryIconResolver _icons;
    private readonly List<Cell> _cells = [];

    private Control _frame = null!;

    /// <summary>装一条随身栏。**状态与图标来源都由调用方给** —— 本类不挑数据。</summary>
    public CarrySlotBarView(InputRouter router, CarrySlotBar bar, CarryIconResolver icons)
    {
        _router = router;
        _bar = bar;
        _icons = icons;
    }

    /// <summary>拿一个物品标识换一张图标；没有图标就返回 <c>null</c>，那一格画空框。</summary>
    public delegate Texture2D? CarryIconResolver(string itemId);

    /// <summary>
    /// 那条随身栏本体。**玩法侧从这里读「玩家手上是哪一格」**（播种在 `GP-128`）。
    /// </summary>
    /// <remarks>
    /// 暴露的是规则层那个对象而不是一个复制出来的下标：复制一份就有两处说「现在选第几格」，
    /// 而两处对不上时没有任何东西判得出来。
    /// </remarks>
    public CarrySlotBar Bar => _bar;

    /// <summary>这一块现在占屏幕的哪个矩形。可与 <see cref="HudLayout.RectOf"/> 的预测比对。</summary>
    public Rect2 BlockRect => _frame.GetGlobalRect();

    /// <summary>第 N 格现在显示的件数文字。为 1 或为空时是空串。</summary>
    public string CountText(int index) => _cells[index].Count.Text;

    /// <summary>第 N 格现在是不是选中态。</summary>
    public bool IsLit(int index) =>
        _cells[index].Slot.SelfModulate == PixelTheme.ToColor(HudPalette.Hot);

    /// <summary>第 N 格的图标显不显示。空格不显示。</summary>
    public bool IconShown(int index) => _cells[index].Icon.Visible;

    public override void _Ready()
    {
        // 铺满所属层、块靠锚点贴角，理由与 LevelHud 那段实测注释同源：**必须用
        // SetAnchorsAndOffsetsPreset**，对已经在树里的节点调 SetAnchorsPreset 会改写偏移、
        // 保住那个 0×0 的矩形，而它不报错。
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;

        Build();

        // 等自己的尺寸定下来再摆块，之后每次尺寸变都重摆一遍（幂等）。
        // 不做的话贴右下角的块会拿 0×0 的父矩形算偏移、落到屏幕外去，而那只表现为「少了一块」。
        Resized += AnchorBlock;
        AnchorBlock();
        Refresh();
    }

    public override void _ExitTree() => Resized -= AnchorBlock;

    /// <summary>
    /// 每帧问一次门面：有没有换格。
    /// </summary>
    /// <remarks>
    /// **在按下的那一刻问，不等信号** —— 与交互那一类同一条口径。用 `_Process` 而不是
    /// `_PhysicsProcess`：换格不是玩法推进，它只改一个显示用的下标，不该被顿帧那个闸门管着。
    ///
    /// **输入一律经门面**（`_router`），不直接调 <c>Input.IsActionPressed</c>。理由是实测出来的：
    /// 事件被消费掉之后轮询照旧返回 true，所以只有门面那一侧的遮挡判定是完整的。
    /// </remarks>
    public override void _Process(double delta)
    {
        var before = _bar.SelectedIndex;

        // 键鼠：六个键直选。**先判直选再判挪位** —— 两者绑在不同设备族上，同一帧不会都来，
        // 但顺序写死了才不用回答「同一帧都来时听谁的」。
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

    /// <summary>内容换了之后叫一声。**选中位不动**，那是 <see cref="CarrySlotBar"/> 保证的。</summary>
    public void OnContentChanged() => Refresh();

    // ── 搭那一块 ────────────────────────────────────────────────────────

    /// <summary>
    /// 六格紧邻横排，坐在与技能位同一个框里。
    /// </summary>
    /// <remarks>
    /// 框的尺寸取 <see cref="HudLayout.SizeOf"/>，所以它与关卡那条技能栏**逐像素一样大**。
    /// 六格比框窄一个栅格（随身栏没有组，不需要那道分界），多出来的空隙留在容器里居中 ——
    /// 于是两种内容的格子落在同一批横坐标上，换场景时格位不跳。
    /// </remarks>
    private void Build()
    {
        var size = HudLayout.SizeOf(HudBlock.ActionBar);
        var style = new StyleBoxFlat
        {
            BgColor = PixelTheme.ToColor(HudPalette.Panel),
            BorderColor = PixelTheme.ToColor(HudPalette.Edge),
            AntiAliasing = false,
        };
        style.SetBorderWidthAll(1);
        style.SetContentMarginAll(UIMetrics.PanelPadding);

        var panel = new PanelContainer
        {
            Name = HudBlock.ActionBar.ToString(),
            CustomMinimumSize = new Vector2(size.Width, size.Height),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        panel.AddThemeStyleboxOverride("panel", style);
        AddChild(panel);
        _frame = panel;

        var row = new HBoxContainer
        {
            Name = "Content",
            Alignment = BoxContainer.AlignmentMode.Center,
        };
        row.AddThemeConstantOverride("separation", 0);
        panel.AddChild(row);

        for (var i = 0; i < CarrySlotBar.SlotCount; i++)
        {
            var frame = new Control
            {
                Name = $"Slot{i}",
                CustomMinimumSize = new Vector2(UIMetrics.IconSmall, UIMetrics.IconSmall),
                MouseFilter = MouseFilterEnum.Ignore,
            };
            row.AddChild(frame);

            var slot = Picture(string.Format(SlotPathFormat, i + 1));
            slot.SetAnchorsPreset(LayoutPreset.FullRect);
            frame.AddChild(slot);

            var icon = Picture(null);
            icon.SetAnchorsPreset(LayoutPreset.FullRect);
            frame.AddChild(icon);

            // 件数角标压在图标右下。**不另占一行** —— 那一块只有一个图标的高度，
            // 加一行会改块高，而块高是「不许压角色可读区」那笔账的输入。
            var count = new Label
            {
                Name = "Count",
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            count.SetAnchorsPreset(LayoutPreset.FullRect);
            frame.AddChild(count);

            _cells.Add(new Cell(slot, icon, count));
        }
    }

    /// <summary>把那一块贴到它该贴的角上。**这里没有一个坐标** —— 逻辑宽度撑开时它自动跟着走。</summary>
    private void AnchorBlock() =>
        _frame.SetAnchorsAndOffsetsPreset(
            HudLayout.AnchorOf(HudBlock.ActionBar) switch
            {
                HudAnchor.TopLeft => LayoutPreset.TopLeft,
                HudAnchor.TopRight => LayoutPreset.TopRight,
                HudAnchor.BottomLeft => LayoutPreset.BottomLeft,
                HudAnchor.BottomRight => LayoutPreset.BottomRight,
                _ => throw new ArgumentOutOfRangeException(nameof(HudBlock.ActionBar)),
            },
            LayoutPresetMode.Minsize, UIMetrics.SafeMargin);

    /// <summary>
    /// 把六格画上去。
    /// </summary>
    /// <remarks>
    /// **选中态靠换框色，不靠只改一个符号或只改亮度。** 相反语义要有不同的东西看得见
    /// （[像素绘制原则 §4]），而这一对的语义是「这一格是不是我手上那个」。
    ///
    /// **件数为 1 时不画数字**，规则在 <see cref="CarrySlot.ShowsCount"/>，本处不再判一次。
    /// </remarks>
    private void Refresh()
    {
        for (var i = 0; i < _cells.Count; i++)
        {
            var slot = _bar.Slots[i];
            var cell = _cells[i];
            var lit = i == _bar.SelectedIndex;

            cell.Slot.SelfModulate = PixelTheme.ToColor(lit ? HudPalette.Hot : HudPalette.Ink);

            // 空格不画图标，但**框照画** —— 位置固定才有肌肉记忆（同技能位那条）。
            cell.Icon.Texture = slot.IsEmpty ? null : _icons(slot.ItemId);
            cell.Icon.Visible = cell.Icon.Texture is not null;

            cell.Count.Text = slot.ShowsCount ? slot.Count.ToString() : string.Empty;
            cell.Count.AddThemeColorOverride("font_color",
                PixelTheme.ToColor(lit ? HudPalette.Hot : HudPalette.Ink));
        }
    }

    /// <summary>一张 16px 的图。**不碰 <c>TextureFilter</c>** —— 项目级最近邻是像素清晰的唯一依靠。</summary>
    private static TextureRect Picture(string? path) => new()
    {
        Name = path is null ? "Icon" : "Frame",
        Texture = path is null ? null : Art(path),
        CustomMinimumSize = new Vector2(UIMetrics.IconSmall, UIMetrics.IconSmall),
        StretchMode = TextureRect.StretchModeEnum.Keep,
        MouseFilter = MouseFilterEnum.Ignore,
    };

    private static Texture2D Art(string path) =>
        ResourceLoader.Exists(path)
            ? GD.Load<Texture2D>(path)
            : throw new FileNotFoundException(
                $"随身栏缺素材：{path}（在 Godot 里确认这个文件已导入）");

    private sealed record Cell(TextureRect Slot, TextureRect Icon, Label Count);
}
