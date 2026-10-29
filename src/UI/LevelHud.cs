using Godot;
using Tinderhearth.Rules.UI;

namespace Tinderhearth.UI;

/// <summary>
/// 关卡 HUD 的屏幕空间部分：资源条与状态位、技能位、目标进度、队友状态，全部收在屏幕顶上那一段。
/// </summary>
/// <remarks>
/// 本文件里不自己定任何「量」：尺寸与放置从 <see cref="HudLayout"/> 与 <see cref="UIMetrics"/> 取（场合是
/// <see cref="UIContext.Level"/>），显示的数值从 <see cref="HudViewModel"/> 取，颜色从 <see cref="HudPalette"/>
/// 取。除 0 与 1 之外不写数字字面量，这两个是结构量（第一个元素、间距为零、加一取编号），不是玩法数值。
///
/// 位置一律走锚点预设加偏移（<see cref="HudPlace"/>），不写 <c>Position</c>。逻辑宽度不是定值：实测
/// 3840×2130 的窗口得到逻辑 649×360，写死横向坐标的界面在宽窗口上会错位，而在 640 宽的窗口上完全
/// 看不出来 —— 所以实机验 HUD 时拉一下窗口宽度。
///
/// 框、格子、资源条、资源条的标签、任务目标图标与头像框是素材资源里作者画的那几张（<see cref="UIArt"/>），尺寸照它们排
/// （格子 22、格距 6，头像框 20）。资源条怎么画、回复时怎么追、扣减时怎么留一截在 <see cref="GaugeBar"/>。技能图标是内容，
/// 由调用方给（<see cref="SkillIconResolver"/>），界面层不持有一张「技能对图标」的表。
///
/// 读条、精英血条与伤害数字不在这里，它们画在执行者身上、跟着角色走，见 <see cref="WorldSpaceUI"/>。
/// </remarks>
public sealed partial class LevelHud : Control
{

    /// <summary>
    /// 倒地记号。它是个符号、不是一句要翻译的话，所以不进文本表（按键记号同理，见
    /// <see cref="InputHints"/>）。
    /// </summary>
    private const string DownMark = "×";

    private const UIContext Here = UIContext.Level;

    private readonly InputRouter _router;
    private readonly UIArt _art;
    private readonly SkillIconResolver _icons;
    private readonly GaugeChaseSettings _motion;
    private readonly Dictionary<HudBlock, Control> _roots = [];
    private readonly List<GaugeRow> _gauges = [];
    private readonly List<SkillCell> _skills = [];
    private readonly List<TeammateCell> _teammates = [];

    private Label _objective = null!;
    private HudViewModel _model;

    /// <summary>装一份 HUD。视图模型、技能图标与资源条怎么动由调用方给，本类不挑数据；界面件从素材资源取。</summary>
    public LevelHud(InputRouter router, HudViewModel model, UIArt art, SkillIconResolver icons,
                    GaugeChaseSettings motion)
    {
        _router = router;
        _model = model;
        _art = art;
        _icons = icons;
        _motion = motion;
    }

    /// <summary>拿一个技能位换那一位上那个技能的图标；那个技能没有图标就返回 <c>null</c>，那一格只画框。</summary>
    /// <remarks>
    /// 只在解锁了的位上问。哪一位装着哪个技能是调用方的事，技能定义又是会被 mod 换掉的内容，所以界面层只问、不存。
    /// </remarks>
    public delegate Texture2D? SkillIconResolver(HudSkillSlot slot);

    /// <summary>当前视图模型。换一份就整屏刷新。</summary>
    public HudViewModel Model
    {
        get => _model;
        set
        {
            _model = value;
            if (_roots.Count > 0)
            {
                Refresh();
            }
        }
    }

    /// <summary>某一块现在占屏幕的哪个矩形。可与 <see cref="HudLayout.RectOf"/> 的预测比对。</summary>
    public Rect2 RectOf(HudBlock block) => _roots[block].GetGlobalRect();

    /// <summary>
    /// HUD 根节点自己占的矩形。
    /// </summary>
    /// <remarks>
    /// 它必须铺满视口。不铺满的话，贴右边的块会按一个错的父矩形算偏移、落到屏幕外去 ——
    /// 实测踩过，细节见 <see cref="_Ready"/> 里那段注释。
    /// </remarks>
    public Rect2 RootRect => GetGlobalRect();

    /// <summary>某一块现在显不显示。队友为 0 时那一块是收起的。</summary>
    public bool IsShown(HudBlock block) => _roots[block].Visible;

    /// <summary>
    /// 某一块的容器自己要多大，单位是界面像素（不含 <c>CustomMinimumSize</c>）。
    /// </summary>
    /// <remarks>
    /// 用它核对排版算式和实际摆出来的东西有没有分叉：容器要多少是它自己按间距与框边算的，拿它与
    /// <see cref="HudLayout.SizeOf"/> 比就与贴哪个角无关。
    /// </remarks>
    public Vector2 ContentMinOf(HudBlock block) => _roots[block].GetMinimumSize();

    // ── 下面几个把「屏幕上到底显示了什么」暴露成能读的值 ──

    /// <summary>目标进度那一行现在显示的字。</summary>
    public string ObjectiveText => _objective.Text;

    /// <summary>第 N 个技能位现在显示的按键记号。</summary>
    public string SlotHint(int index) => _skills[index].Hint.Text;

    /// <summary>第 N 个技能位现在是不是高亮（修饰键按住时它那一组会亮）。</summary>
    public bool SlotLit(int index) =>
        _skills[index].Slot.SelfModulate == PixelTheme.ToColor(HudPalette.Hot);

    /// <summary>第 N 条资源条现在填了多少像素。</summary>
    public int GaugeExtent(int index) => _gauges[index].Bar.Length;

    /// <summary>第 N 个技能位的冷却遮罩现在盖了多高。</summary>
    public int CooldownExtent(int index) => (int)_skills[index].Mask.OffsetBottom;

    /// <summary>第 N 个队友格的倒地记号显不显示。</summary>
    public bool DownMarkShown(int index) => _teammates[index].Mark.Visible;

    public override void _Ready()
    {
        // 铺满所属层，块靠锚点各自贴边。不吃鼠标 —— 它是常驻显示，不是可操作面板。
        // 必须用 SetAnchorsAndOffsetsPreset：实测对已经在树里的节点调 SetAnchorsPreset，引擎会
        // 改写偏移去保住当前那个 0×0 的矩形，于是锚点对了、尺寸还是 0×0，而且不报错。
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;

        BuildResources();
        BuildObjective();
        BuildTeammates();
        BuildSkills();

        // 等自己的尺寸定下来再摆块：实测 _Ready 这一刻根节点还是 0×0（视口拉伸没算完），而块的
        // 偏移是拿当时的父矩形算的，于是贴右边的块落到负坐标、画在屏幕外，还不报错。
        // 挂上 Resized 之后尺寸每变一次重摆一遍；放置每次都从锚点预设重算，重摆幂等。
        Resized += AnchorBlocks;
        AnchorBlocks();
        Refresh();

        // 订阅门面的事件，不每帧轮询。
        _router.SkillGroupChanged += OnSkillGroupChanged;
        _router.DeviceChanged += OnDeviceChanged;
    }

    public override void _ExitTree()
    {
        Resized -= AnchorBlocks;
        _router.SkillGroupChanged -= OnSkillGroupChanged;
        _router.DeviceChanged -= OnDeviceChanged;
    }

    // ── 搭四块 ──────────────────────────────────────────────────────────

    /// <summary>主角资源：每条一行，迷你图标在左、条在右；最下面一行留给状态位。</summary>
    private void BuildResources()
    {
        // 行距为零：一条资源正好占一个行高，行与行之间靠基线节奏区分，不靠空隙。
        var column = Column(HudBlock.Resources, separation: 0);
        foreach (var gauge in _model.Gauges)
        {
            var row = new HBoxContainer
            {
                Name = gauge.Kind.ToString(),
                CustomMinimumSize = new Vector2(HudLayout.ResourcesContentWidth, HudLayout.GaugeRowHeight),
            };
            row.AddThemeConstantOverride("separation", UIMetrics.ItemGap);
            column.AddChild(row);

            // 标签是一张迷你图标，不写字：与语言无关，将来出英文版不用重排。比行矮，靠 ShrinkCenter 居中。
            var label = new TextureRect
            {
                Name = "Label",
                Texture = GaugeIcon(gauge.Kind),
                CustomMinimumSize = new Vector2(HudLayout.GaugeLabelSize, HudLayout.GaugeLabelSize),
                StretchMode = TextureRect.StretchModeEnum.Keep,
                SizeFlagsVertical = SizeFlags.ShrinkCenter,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            row.AddChild(label);

            // 条比行矮，靠 ShrinkCenter 在行里居中 —— 不用纵向偏移，就不会有半格。
            var (fill, fillName) = FillOf(gauge.Kind);
            var bar = new GaugeBar(_art, fill, fillName, HudLayout.GaugeBarWidth, _motion)
            {
                SizeFlagsVertical = SizeFlags.ShrinkCenter,
            };
            row.AddChild(bar);

            _gauges.Add(new GaugeRow(bar));
        }

        // 状态位那一行只留位置：里面挂什么跟状态系统走，界面侧不新增读口。
        column.AddChild(new Control
        {
            Name = "Status",
            CustomMinimumSize = new Vector2(HudLayout.ResourcesContentWidth, HudLayout.StatusRowHeight),
            MouseFilter = MouseFilterEnum.Ignore,
        });
    }

    /// <summary>
    /// 目标进度：一个图标加一行字。
    /// </summary>
    /// <remarks>
    /// 宽度按最长那句（达成时那句）定，并开 <see cref="Label.ClipText"/>：内容比框长时截断，
    /// 不把整块撑开。撑开会改掉这一块占多大屏，而块占多大屏是放置判据的依据。
    /// </remarks>
    private void BuildObjective()
    {
        var content = Row(HudBlock.Objective, UIMetrics.ItemGap);
        var icon = HudPlace.Picture(_art.HudObjective, "hud/objective");
        icon.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        content.AddChild(icon);

        _objective = new Label
        {
            Name = "Text",
            ClipText = true,
            VerticalAlignment = VerticalAlignment.Center,
            CustomMinimumSize = new Vector2(
                HudLayout.ObjectiveMaxChars * UIMetrics.FontSize, UIMetrics.LineHeight),
        };
        content.AddChild(_objective);
    }

    /// <summary>
    /// 队友：头像框加一根血条，横排。为 0 时整块收起。
    /// </summary>
    /// <remarks>
    /// 格里不写名字：头像那么宽装不下 12px 的两个汉字，而认人本来就该靠头像（见
    /// <see cref="HudTeammate"/>）。倒地于是另加一个记号盖在头像上，不只换颜色 —— 相反的两个
    /// 意思要有不同的符号，见设计仓 production/像素绘制原则.md 的「明度先于色相」一节。
    /// </remarks>
    private void BuildTeammates()
    {
        var row = Row(HudBlock.Teammates, UIMetrics.ItemGap);
        for (var i = 0; i < HudLayout.MaxTeammates; i++)
        {
            var cell = new VBoxContainer { Name = $"Mate{i}" };
            cell.AddThemeConstantOverride("separation", 0);
            row.AddChild(cell);

            var head = new Control
            {
                Name = "Head",
                CustomMinimumSize = new Vector2(HudLayout.PortraitSize, HudLayout.PortraitSize),
            };
            cell.AddChild(head);

            var portrait = HudPlace.Picture(_art.HudPortraitFrame, "hud/portrait_frame");
            portrait.SetAnchorsPreset(LayoutPreset.FullRect);
            head.AddChild(portrait);

            var mark = new Label
            {
                Name = "Down",
                Text = DownMark,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            mark.SetAnchorsPreset(LayoutPreset.FullRect);
            head.AddChild(mark);

            // 队友的血条与主角的资源条是同一套槽与填充，只是窄到头像那么宽。
            var (fill, fillName) = FillOf(HudGaugeKind.Health);
            var bar = new GaugeBar(_art, fill, fillName, HudLayout.PortraitSize, _motion);
            cell.AddChild(bar);

            _teammates.Add(new TeammateCell(cell, portrait, mark, bar));
        }
    }

    /// <summary>
    /// 技能位：一行格子，格距照画好的那条栏。
    /// </summary>
    /// <remarks>
    /// 格与格等距，两组之间不另留空：画好的那条栏就是等距的。手柄上哪几格归哪个扳机靠按住扳机时那一组
    /// 连着高亮，和每格里叠的面键记号（手柄那一套跟设计仓台账 <c>UI-54</c> 走）。格子是作者画的那张 22×22，
    /// 图标放在 (3,3)，与随身栏那一条逐格对齐。
    /// </remarks>
    private void BuildSkills()
    {
        var line = Row(HudBlock.ActionBar, HudLayout.ActionBarCellGap);
        for (var index = 0; index < HudLayout.ActionBarCellCount; index++)
        {
            var cell = new Control
            {
                Name = $"Slot{index}",
                CustomMinimumSize = new Vector2(UIMetrics.Cell, UIMetrics.Cell),
                MouseFilter = MouseFilterEnum.Ignore,
            };
            line.AddChild(cell);

            // 格子本身铺满这一格；解锁没有、亮不亮在刷新时换。
            var slot = HudPlace.Picture(_art.KitSlotNormal, "kit/slot_normal");
            slot.Name = "Slot";
            slot.SetAnchorsPreset(LayoutPreset.FullRect);
            cell.AddChild(slot);

            // 图标那一格在格子里往里让一个格内边距（描边加留白）。
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

            // 冷却遮罩：不透明色块，从上往下遮住图标的一部分，退完即可用。
            // 用遮住而不是压暗，因为半透明会在屏幕上造出插值出来的中间色（见 PixelColor）。
            var mask = new ColorRect
            {
                Name = "Cooldown",
                Color = PixelTheme.ToColor(HudPalette.Cooldown),
                AnchorBottom = 0,
                AnchorRight = 1,
            };
            inner.AddChild(mask);

            // 手柄的面键提示叠在图标里，不另占一行 —— 多一行会改块高。
            var hint = new Label
            {
                Name = "Hint",
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            hint.SetAnchorsPreset(LayoutPreset.FullRect);
            inner.AddChild(hint);

            _skills.Add(new SkillCell(slot, icon, mask, hint));
        }
    }

    // ── 摆位与刷新 ──────────────────────────────────────────────────────

    /// <summary>按关卡那一套把各块贴到各自的位置上。放置全从规则层取，这里没有一个坐标。</summary>
    private void AnchorBlocks()
    {
        foreach (var (block, root) in _roots)
        {
            HudPlace.Apply(root, HudLayout.PlacementOf(block, Here));
        }
    }

    /// <summary>把视图模型画上去。</summary>
    private void Refresh()
    {
        var objective = _model.Objective;
        _objective.Text = objective.Complete
            ? objective.DoneMessage
            : $"{objective.Label} {objective.Done}／{objective.Total}";
        _objective.AddThemeColorOverride("font_color",
            PixelTheme.ToColor(objective.Complete ? HudPalette.Hot : HudPalette.Ink));

        for (var i = 0; i < _gauges.Count; i++)
        {
            _gauges[i].Bar.Set(_model.Gauges[i].Ratio);
        }

        RefreshSkills();

        // 队友那一块收起时别的块一动不动：每一块的放置都只按尺寸算，不看谁显示着。
        _roots[HudBlock.Teammates].Visible = _model.ShowTeammates;
        for (var i = 0; i < _teammates.Count; i++)
        {
            var cell = _teammates[i];
            var present = i < _model.Teammates.Count;
            cell.Root.Visible = present;
            if (!present)
            {
                continue;
            }

            var mate = _model.Teammates[i];
            // 倒地那一张记号随设计仓台账 UI-46 换上，到那时头像框不再染色；在那之前倒地是换色加一个叉。
            cell.Portrait.SelfModulate = mate.Down ? PixelTheme.ToColor(HudPalette.Down) : Colors.White;
            cell.Mark.Visible = mate.Down;
            Paint(cell.Mark, HudPalette.Hot);
            cell.Bar.Set(mate.Ratio);
        }
    }

    /// <summary>
    /// 技能位：图标、冷却遮罩、按键记号，加当前生效那一组的高亮。
    /// </summary>
    /// <remarks>
    /// 记号从 <see cref="InputHints"/> 来，它又从绑定表来 —— 改键位时提示自动跟着改，
    /// 不会一直教玩家按错的键。
    ///
    /// 键鼠下不画记号：技能键与槽位从左到右一一对应，再在每格上画一遍字母只是多一行字，而冷却中
    /// 的格记号为空还会让这一行看起来缺号。手柄不同，扳机加面键的组合玩家推不出来，记号照画。
    /// </remarks>
    private void RefreshSkills()
    {
        var device = _router.Device;
        var active = _router.ActiveSkillGroup;

        for (var i = 0; i < _skills.Count; i++)
        {
            var slot = _model.Skills[i];
            var cell = _skills[i];
            var group = InputHints.GroupOfSkill(slot.Action);
            var lit = active != SkillGroup.None && active == group;

            // 没解锁的位画「还没开」那张格子、不画图标：位置固定才有肌肉记忆，所以照画不藏。
            cell.Slot.Texture = slot.Unlocked
                ? HudPlace.Piece(_art.KitSlotNormal, "kit/slot_normal")
                : HudPlace.Piece(_art.KitSlotLocked, "kit/slot_locked");
            cell.Icon.Texture = slot.Unlocked ? _icons(slot) : null;
            cell.Icon.Visible = cell.Icon.Texture is not null;
            cell.Mask.OffsetBottom = Extent(slot.MaskRatio, UIMetrics.IconSmall);

            // 按住扳机时那一组亮起来的那一张跟手柄适配一起后置（设计仓台账 UI-54），在那之前那一组的格子换色。
            cell.Slot.SelfModulate = lit ? PixelTheme.ToColor(HudPalette.Hot) : Colors.White;

            // 冷却中不显示记号：那时按了也没用，留着记号是在教玩家按一个不会响的键。
            cell.Hint.Text = slot.Ready && device == InputDeviceKind.Gamepad
                ? InputHints.SkillLabel(slot.Action, device)
                : InputHints.None;
            Paint(cell.Hint, lit ? HudPalette.Hot : HudPalette.Dim);
        }
    }

    private void OnSkillGroupChanged(SkillGroup group) => RefreshSkills();

    private void OnDeviceChanged(InputDeviceKind device) => RefreshSkills();

    // ── 小工具 ──────────────────────────────────────────────────────────

    /// <summary>比例换算成整数像素长度。取整只在这一处做，别处不要再算一遍。</summary>
    private static int Extent(double ratio, int full) => (int)Math.Round(ratio * full);

    private static void Paint(Label label, PixelColor color) =>
        label.AddThemeColorOverride("font_color", PixelTheme.ToColor(color));

    /// <summary>资源条前面那个迷你图标，按是哪一条从素材资源取。素材资源启动时核过，这里缺了就是没核。</summary>
    private Texture2D GaugeIcon(HudGaugeKind kind) => (kind switch
    {
        HudGaugeKind.Health => _art.HudGaugeIconHp,
        HudGaugeKind.Stamina => _art.HudGaugeIconSp,
        HudGaugeKind.Mana => _art.HudGaugeIconMp,
        HudGaugeKind.DailyVigor => _art.HudGaugeIconVigor,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), $"没有这条资源：{kind}"),
    }) ?? throw new InvalidOperationException($"素材资源里少了资源条「{kind}」的标签图 —— 应当已在启动时核过");

    /// <summary>一块的外框，里面横排。列距必须与 <see cref="HudLayout.ContentSizeOf"/> 的算法一致。</summary>
    private HBoxContainer Row(HudBlock block, int separation)
    {
        var box = new HBoxContainer { Name = "Content" };
        box.AddThemeConstantOverride("separation", separation);
        Frame(block).AddChild(box);
        return box;
    }

    /// <summary>
    /// 一块的外框，里面竖排。
    /// </summary>
    /// <remarks>
    /// 行距必须与 <see cref="HudLayout.ContentSizeOf"/> 的算法一致：偏移是按块的最小尺寸算的，而实际
    /// 尺寸取「内容最小尺寸与 <c>CustomMinimumSize</c> 里较大的那个」，两边不一致时整块会偏出安全边距
    /// 几像素（实测撞过）。改这个数之后实机看一眼块有没有偏出去。
    /// </remarks>
    private VBoxContainer Column(HudBlock block, int separation)
    {
        var box = new VBoxContainer { Name = "Content" };
        box.AddThemeConstantOverride("separation", separation);
        Frame(block).AddChild(box);
        return box;
    }

    private PanelContainer Frame(HudBlock block)
    {
        var frame = HudPlace.Frame(block, Here, _art);
        AddChild(frame);
        _roots[block] = frame;
        return frame;
    }

    /// <summary>某条资源用哪张填充，连同它在素材资源里的名字（报错时用）。精力不在关卡里，问到就是排错了。</summary>
    private (NinePiece? Fill, string Name) FillOf(HudGaugeKind kind) => kind switch
    {
        HudGaugeKind.Health => (_art.HudBarFillHp, "hud/bar_fill_hp"),
        HudGaugeKind.Stamina => (_art.HudBarFillSp, "hud/bar_fill_sp"),
        HudGaugeKind.Mana => (_art.HudBarFillMp, "hud/bar_fill_mp"),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), $"关卡 HUD 里没有这条资源：{kind}"),
    };

    private sealed record GaugeRow(GaugeBar Bar);

    private sealed record SkillCell(TextureRect Slot, TextureRect Icon, ColorRect Mask, Label Hint);

    private sealed record TeammateCell(Control Root, TextureRect Portrait, Label Mark, GaugeBar Bar);
}
