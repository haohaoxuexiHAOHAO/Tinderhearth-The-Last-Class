using Godot;
using Tinderhearth.Rules.Ui;

namespace Tinderhearth.UI;

/// <summary>
/// 关卡 HUD 的屏幕空间部分（`UI-8`）：资源、技能位、目标进度、队友状态。
/// </summary>
/// <remarks>
/// **本文件里一个「量」都没有。** 尺寸与放置全部从 <see cref="HudLayout"/> 与
/// <see cref="UiMetrics"/> 取，显示的数值全部从 <see cref="HudViewModel"/> 取，颜色从
/// <see cref="HudPalette"/> 取。这不是风格洁癖，是让两条会静默退化的规则有执行体：
///
/// - **没有绝对像素坐标。** 位置一律走 <see cref="Control.SetAnchorsAndOffsetsPreset"/>，
///   本文件不出现 <c>Position</c>。`aspect="expand"` 下逻辑宽度是变量（`UI-3` 实测
///   3840×2130 的窗口得到 649×360），写死横向坐标的界面在宽窗口上会错位，而**窄窗口上看不出来**。
/// - **数值全部由视图模型传入。** 于是「代码里没有写死的数字」可以机器扫：除 0 与 1 之外的
///   数字字面量出现在本文件就判失败（`tools/check_hud.py`）。0 与 1 留着是因为它们是结构量
///   （第一个元素、间距为零、加一取编号），不是玩法数值。
///
/// 两条都由 `tools/check_hud.py` 守着，静态扫加行为核两头拦，理由见那个文件。
///
/// **世界空间那一半不在这里。** 读条、精英血条与伤害数字归 `UI-9`，挂
/// <see cref="UiLayer.WorldSpace"/> —— 正典明确否掉「读条画在界面角落」，要求画在执行者身上。
/// </remarks>
public sealed partial class LevelHud : Control
{
    private const string SlotPathFormat = "res://assets/placeholder/ui/skill-slot-{0}.png";
    private const string IconPathFormat = "res://assets/placeholder/ui/skill-icon-{0}.png";
    private const string BarTrackPath = "res://assets/placeholder/ui/bar-track.png";
    private const string BarFillPath = "res://assets/placeholder/ui/bar-fill.png";
    private const string ObjectiveIconPath = "res://assets/placeholder/ui/objective.png";
    private const string PortraitPath = "res://assets/placeholder/ui/portrait-frame.png";

    /// <summary>
    /// 倒地记号。**是符号不是文案**，所以不进文本表（同按键记号那条，见 <see cref="InputHints"/>）。
    /// </summary>
    private const string DownMark = "×";

    private readonly InputRouter _router;
    private readonly Dictionary<HudBlock, Control> _roots = [];
    private readonly List<GaugeRow> _gauges = [];
    private readonly List<SkillCell> _skills = [];
    private readonly List<TeammateCell> _teammates = [];

    private Label _objective = null!;
    private HudViewModel _model;

    /// <summary>装一份 HUD。视图模型由调用方给 —— 本类不挑数据。</summary>
    public LevelHud(InputRouter router, HudViewModel model)
    {
        _router = router;
        _model = model;
    }

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

    /// <summary>某一块现在占屏幕的哪个矩形。给 `HudProbe` 读回来与规则层的预测比对。</summary>
    public Rect2 RectOf(HudBlock block) => _roots[block].GetGlobalRect();

    /// <summary>
    /// HUD 根节点自己占的矩形。
    /// </summary>
    /// <remarks>
    /// 它必须铺满视口，否则贴下边与贴右边的块会按一个错的父矩形算偏移、落到屏幕外去 ——
    /// 2026-08-31 实测踩过一次，见 <see cref="_Ready"/> 里那段注释。所以它自己也是一条判据。
    /// </remarks>
    public Rect2 RootRect => GetGlobalRect();

    /// <summary>某一块现在显不显示。队友为 0 时那一块是收起的。</summary>
    public bool IsShown(HudBlock block) => _roots[block].Visible;

    /// <summary>
    /// 某一块的**容器自己要的**最小尺寸（不含 <c>CustomMinimumSize</c>）。
    /// </summary>
    /// <remarks>
    /// 它是「排版算式与实现漂移」那条的**病根级判据**。只比实际矩形与预测矩形不够：贴上边的块
    /// 算式偏大时只会往下多长几像素、位置一点不变，于是两边照样对得上，漂移看不出来
    /// （2026-08-31 自证时撞出来的）。而容器要多少是它自己按间距与内边距算的 —— 拿它与
    /// <see cref="HudLayout.SizeOf"/> 比，任何一块的算式与实现分叉都逃不掉，与贴哪个角无关。
    /// </remarks>
    public Vector2 ContentMinOf(HudBlock block) => _roots[block].GetMinimumSize();

    // ── 下面几个只给 HudProbe 读，好让「屏幕上到底显示了什么」变成可判定的东西 ──

    /// <summary>目标进度那一行现在显示的字。</summary>
    public string ObjectiveText => _objective.Text;

    /// <summary>第 N 个技能位现在显示的按键记号。</summary>
    public string SlotHint(int index) => _skills[index].Hint.Text;

    /// <summary>第 N 个技能位现在是不是高亮（修饰键按住时它那一组会亮）。</summary>
    public bool SlotLit(int index) =>
        _skills[index].Slot.SelfModulate == PixelTheme.ToColor(HudPalette.Hot);

    /// <summary>第 N 条资源条现在填了多少像素。</summary>
    public int GaugeExtent(int index) => (int)_gauges[index].Fill.OffsetRight;

    /// <summary>第 N 个技能位的冷却遮罩现在盖了多高。</summary>
    public int CooldownExtent(int index) => (int)_skills[index].Mask.OffsetBottom;

    /// <summary>第 N 个队友格的倒地记号显不显示。</summary>
    public bool DownMarkShown(int index) => _teammates[index].Mark.Visible;

    public override void _Ready()
    {
        // HUD 铺满所属层，块靠锚点各自贴边。**不吃鼠标** —— 它是常驻显示，不是可操作面板。
        //
        // **必须用 `SetAnchorsAndOffsetsPreset` 而不是 `SetAnchorsPreset`。** 2026-08-31 实测：
        // 对**已经在树里**的节点调 `SetAnchorsPreset(FullRect)`，引擎会把偏移改写成
        // −640,−360 以保住当前那个 0×0 的矩形 —— 锚点确实变成了 0,0,1,1，尺寸却还是 0×0。
        // 那个 `keepOffsets` 参数的含义与名字给人的印象相反：`false` 是「改写偏移、保住视觉位置」，
        // `true` 才是「原样留着偏移数字」。加进树之前调它碰巧没事（那时 `_size_changed` 早退），
        // 所以这个坑只在「先 AddChild 再设锚点」的写法里出现，而且**不报错** —— 表现是贴下边与
        // 贴右边的块落到负坐标、画在屏幕外，存图里只看得出「少了几块」。
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;

        BuildObjective();
        BuildResources();
        BuildSkills();
        BuildTeammates();

        // **必须等自己的尺寸定下来再摆块。** 2026-08-31 实测踩到的：`_Ready` 里 HUD 根节点的
        // 尺寸还是 0×0（那一刻视口的拉伸尚未算完，与 `Main.PrintDisplayMetrics` 那条「读早了」
        // 是同一件事），而块的锚点偏移是拿**当时的父矩形**算的 —— 于是贴下边与贴右边的三块全落到
        // 负坐标上，画在屏幕外，只有贴左上角那块看起来是对的。**这种错在存图里只表现为「少了三块」，
        // 不报错。** 挂 Resized 之后尺寸每次变都重摆一遍；我们用的几个锚点预设算出的偏移与父尺寸
        // 无关，所以重复摆是幂等的。
        Resized += AnchorBlocks;
        AnchorBlocks();
        Refresh();

        // **订阅事件而不是每帧轮询**（`UI-7` 的门面把这两件事都准备好了）。
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

    /// <summary>
    /// 目标进度：一个图标加一行字。
    /// </summary>
    /// <remarks>
    /// 宽度按最长那句（达成态）定，并开 <see cref="Label.ClipText"/> —— 内容比框长时截断而不是
    /// 把整块撑开。撑开会让占屏比例与可读区的预测失效，而那两个数是选放置方案的依据。
    /// </remarks>
    private void BuildObjective()
    {
        var content = Row(HudBlock.Objective, UiMetrics.ItemGap);
        content.AddChild(Picture(ObjectiveIconPath, UiMetrics.IconSmall, UiMetrics.IconSmall));

        _objective = new Label
        {
            Name = "Text",
            ClipText = true,
            VerticalAlignment = VerticalAlignment.Center,
            CustomMinimumSize = new Vector2(
                HudLayout.ObjectiveMaxChars * UiMetrics.FontSize, UiMetrics.LineHeight),
        };
        content.AddChild(_objective);
    }

    /// <summary>主角资源：每条一行，标签在左、条在右。</summary>
    private void BuildResources()
    {
        // 行距为零：一条资源正好占一个行高，行与行之间靠基线节奏区分，不靠空隙。
        var column = Column(HudBlock.Resources, separation: 0);
        foreach (var gauge in _model.Gauges)
        {
            var row = new HBoxContainer { Name = gauge.Kind.ToString() };
            row.AddThemeConstantOverride("separation", UiMetrics.ItemGap);
            column.AddChild(row);

            var label = new Label
            {
                Name = "Label",
                VerticalAlignment = VerticalAlignment.Center,
                CustomMinimumSize = new Vector2(HudLayout.GaugeLabelWidth, UiMetrics.LineHeight),
            };
            row.AddChild(label);

            // 条比行矮，靠 ShrinkCenter 在行里居中 —— 不用纵向偏移，就不会有半格。
            var holder = new Control
            {
                Name = "Bar",
                CustomMinimumSize = new Vector2(HudLayout.GaugeBarWidth, HudLayout.GaugeBarHeight),
                SizeFlagsVertical = SizeFlags.ShrinkCenter,
            };
            row.AddChild(holder);

            var track = Stripe(BarTrackPath);
            track.SetAnchorsPreset(LayoutPreset.FullRect);
            holder.AddChild(track);

            // 填充部分：右锚点钉在左边，靠右偏移表达长度。长度是整数像素，所以不会有半格。
            var fill = Stripe(BarFillPath);
            fill.AnchorRight = 0;
            fill.AnchorBottom = 1;
            fill.SelfModulate = PixelTheme.ToColor(HudPalette.ColorOf(gauge.Kind));
            holder.AddChild(fill);

            _gauges.Add(new GaugeRow(label, fill));
        }
    }

    /// <summary>
    /// 6 个技能位：**一行横排**，两组之间空一个栅格，每组前面一个修饰键记号。
    /// </summary>
    /// <remarks>
    /// 横排由作者 2026-08-31 定（此前是两行三列）。`FR-17` 靠两样东西成立：按住修饰键时**那一组
    /// 三个连着高亮**（横排下是连续的三格，比两行更像「一组」），以及每组前面的记号列
    /// 「L」「R」告诉玩家没按住时哪三个归哪个扳机。
    ///
    /// 记号列在键鼠下是空的（数字键直接对应，没有修饰键这一层），但**列宽照留** ——
    /// 换设备时块宽不变，四块的位置就不会跟着跳。
    /// </remarks>
    private void BuildSkills()
    {
        // 两组之间留一个栅格：那道空隙就是「这三个归左扳机、那三个归右扳机」的视觉分界。
        var line = Row(HudBlock.Skills, UiMetrics.Grid);

        for (var group = 0; group < HudLayout.SkillGroupCount; group++)
        {
            // 槽框本身已有边界，同组三格紧邻；空隙只留给两组之间的分界。
            // 不再画「L」「R」记号列：键鼠下它永远是空的，手柄靠组间距 + 高亮 + 图标内面键记号分辨。
            var triad = new HBoxContainer { Name = $"Group{group}" };
            triad.AddThemeConstantOverride("separation", 0);
            line.AddChild(triad);

            for (var seat = 0; seat < HudLayout.SkillsPerGroup; seat++)
            {
                var index = (group * HudLayout.SkillsPerGroup) + seat;
                var cell = new VBoxContainer { Name = $"Slot{index}" };
                cell.AddThemeConstantOverride("separation", 0);
                triad.AddChild(cell);

                var frame = new Control
                {
                    Name = "Frame",
                    CustomMinimumSize = new Vector2(UiMetrics.IconSmall, UiMetrics.IconSmall),
                };
                cell.AddChild(frame);

                var slot = Picture(Numbered(SlotPathFormat, index),
                                   UiMetrics.IconSmall, UiMetrics.IconSmall);
                slot.SetAnchorsPreset(LayoutPreset.FullRect);
                frame.AddChild(slot);

                var icon = Picture(Numbered(IconPathFormat, index),
                                   UiMetrics.IconSmall, UiMetrics.IconSmall);
                icon.SetAnchorsPreset(LayoutPreset.FullRect);
                frame.AddChild(icon);

                // 冷却遮罩：**不透明**，从上往下遮住图标的一部分，退完即可用。
                // 用遮住而不是压暗，理由见 PixelColor —— 半透明会在屏幕上造出插值像素。
                var mask = new ColorRect
                {
                    Name = "Cooldown",
                    Color = PixelTheme.ToColor(HudPalette.Cooldown),
                    AnchorBottom = 0,
                    AnchorRight = 1,
                };
                frame.AddChild(mask);

                // 键鼠不重复画固定数字；手柄面键提示仍需保留，但叠在图标内而不另占一行。
                var hint = new Label
                {
                    Name = "Hint",
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    MouseFilter = MouseFilterEnum.Ignore,
                    CustomMinimumSize = new Vector2(UiMetrics.IconSmall, UiMetrics.IconSmall),
                };
                hint.SetAnchorsPreset(LayoutPreset.FullRect);
                frame.AddChild(hint);

                _skills.Add(new SkillCell(slot, icon, mask, hint));
            }
        }
    }

    /// <summary>
    /// 队友：头像框加一根血条，横排。为 0 时整块收起。
    /// </summary>
    /// <remarks>
    /// 格里没有名字 —— 20px 装不下 12px 的两个汉字，而识别本来就该靠头像（见
    /// <see cref="HudTeammate"/>）。于是倒地这个状态另加一个记号盖在头像上，不只靠颜色
    /// （[像素绘制原则 §4]：相反语义要有不同符号）。
    /// </remarks>
    private void BuildTeammates()
    {
        var row = Row(HudBlock.Teammates, UiMetrics.ItemGap);
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

            var portrait = Picture(PortraitPath, HudLayout.PortraitSize, HudLayout.PortraitSize);
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

            var holder = new Control
            {
                Name = "Bar",
                CustomMinimumSize = new Vector2(HudLayout.PortraitSize, HudLayout.GaugeBarHeight),
            };
            cell.AddChild(holder);

            var track = Stripe(BarTrackPath);
            track.SetAnchorsPreset(LayoutPreset.FullRect);
            holder.AddChild(track);

            var fill = Stripe(BarFillPath);
            fill.AnchorRight = 0;
            fill.AnchorBottom = 1;
            fill.SelfModulate = PixelTheme.ToColor(HudPalette.ColorOf(HudGaugeKind.Health));
            holder.AddChild(fill);

            _teammates.Add(new TeammateCell(cell, portrait, mark, fill));
        }
    }

    // ── 摆位与刷新 ──────────────────────────────────────────────────────

    /// <summary>
    /// 按当前方案把四块贴到各自那条边上。
    /// </summary>
    /// <remarks>
    /// 用引擎自己的锚点预设 API，尺寸取每块的最小尺寸、边距取安全边距。**这里没有一个坐标** ——
    /// 于是逻辑宽度撑开时右侧那两块自动跟着走，而这件事由 `HudProbe` 在两种宽高比下各量一遍来
    /// 证明，不靠「看代码觉得没问题」。
    /// </remarks>
    private void AnchorBlocks()
    {
        foreach (var (block, root) in _roots)
        {
            var preset = HudLayout.AnchorOf(block) switch
            {
                HudAnchor.TopLeft => LayoutPreset.TopLeft,
                HudAnchor.TopRight => LayoutPreset.TopRight,
                HudAnchor.BottomLeft => LayoutPreset.BottomLeft,
                HudAnchor.BottomRight => LayoutPreset.BottomRight,
                _ => throw new ArgumentOutOfRangeException(nameof(block)),
            };
            root.SetAnchorsAndOffsetsPreset(preset, LayoutPresetMode.Minsize, UiMetrics.SafeMargin);
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
            var gauge = _model.Gauges[i];
            _gauges[i].Label.Text = gauge.Label;
            _gauges[i].Fill.OffsetRight = Extent(gauge.Ratio, HudLayout.GaugeBarWidth);
        }

        RefreshSkills();

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
            cell.Portrait.SelfModulate = PixelTheme.ToColor(
                mate.Down ? HudPalette.Down : HudPalette.Ink);
            cell.Mark.Visible = mate.Down;
            Paint(cell.Mark, HudPalette.Hot);
            cell.Fill.OffsetRight = Extent(mate.Ratio, HudLayout.PortraitSize);
        }
    }

    /// <summary>
    /// 技能位：图标、冷却遮罩、按键记号，加当前生效那一组的高亮。
    /// </summary>
    /// <remarks>
    /// `FR-17` 的落点。记号从 <see cref="InputHints"/> 推，而它又从绑定表推 —— 改键位时提示
    /// 自动跟着改，不会一直教玩家按错的键。
    ///
    /// **键鼠不画记号**（作者 2026-08-31 实机定）：数字键 1–6 与六个槽位从左到右一一对应，
    /// 再在每个槽下画一遍数字，只是给屏幕底边添一行字，而冷却中的槽记号为空还会让这一行看起来
    /// 像「1 3 4」这种断号。手柄不同，`LT`／`RT` 加面键推不出来，那个记号照画。
    /// 数字绑定仍由绑定表持有，改键位后手柄提示与实际按键照样同源。
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

            cell.Icon.Visible = slot.Unlocked;
            cell.Mask.OffsetBottom = Extent(slot.MaskRatio, UiMetrics.IconSmall);
            cell.Slot.SelfModulate = PixelTheme.ToColor(
                lit ? HudPalette.Hot : slot.Unlocked ? HudPalette.Ink : HudPalette.Dim);

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

    /// <summary>比例换算成整数像素长度。**取整在这一处**，别处不许再算一遍。</summary>
    private static int Extent(double ratio, int full) => (int)Math.Round(ratio * full);

    private static void Paint(Label label, PixelColor color) =>
        label.AddThemeColorOverride("font_color", PixelTheme.ToColor(color));

    /// <summary>第 N 个槽位的素材路径。编号从 1 起 —— 登记表与美术槽位都是这么编的。</summary>
    private static string Numbered(string format, int index) =>
        string.Format(format, index + 1);

    /// <summary>
    /// 一块的外框：面板底 + 描边 + 一圈内边距，里面横排。
    /// </summary>
    /// <param name="block">哪一块。</param>
    /// <param name="separation">列距。**必须与 <see cref="HudLayout.ContentSizeOf"/> 的算法一致**，理由见 <see cref="Column"/>。</param>
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
    /// <param name="block">哪一块。</param>
    /// <param name="separation">
    /// 行距。**必须与 <see cref="HudLayout.ContentSizeOf"/> 的算法一致** —— 两边不一致时块会比
    /// 预测的矮或高几像素，而 <c>set_offsets_preset</c> 是按**内容最小尺寸**算偏移、实际尺寸却按
    /// 「内容最小与 <c>CustomMinimumSize</c> 取大」定的，于是块会整体偏出安全边距几像素。
    /// 2026-08-31 实测撞过一次（技能块行距写 0、算式按一个间距，块低了 4px），由 `HudProbe` 的
    /// 「实际矩形与预测逐块一致」当场抓出来。
    /// </param>
    private VBoxContainer Column(HudBlock block, int separation)
    {
        var box = new VBoxContainer { Name = "Content" };
        box.AddThemeConstantOverride("separation", separation);
        Frame(block).AddChild(box);
        return box;
    }

    /// <summary>
    /// 造一块的面板底。
    /// </summary>
    /// <remarks>
    /// 用 <see cref="StyleBoxFlat"/> 而不是九宫格素材，两条理由：描边宽度与内边距要从
    /// <see cref="UiMetrics"/> 取（素材一旦换尺寸，九宫格边距就得跟着改，那是第二份真相）；
    /// 而且**抗锯齿必须关掉** —— 圆角与抗锯齿会造出半透明边，违反[像素绘制原则 §9]。
    /// 面板素材 `panel.png` 留给对话框与弹窗，那里真需要九宫格花纹。
    /// </remarks>
    private PanelContainer Frame(HudBlock block)
    {
        var size = HudLayout.SizeOf(block);
        var style = new StyleBoxFlat
        {
            BgColor = PixelTheme.ToColor(HudPalette.Panel),
            BorderColor = PixelTheme.ToColor(HudPalette.Edge),
            AntiAliasing = false,
        };
        style.SetBorderWidthAll(1);
        style.SetContentMarginAll(UiMetrics.PanelPadding);

        var frame = new PanelContainer
        {
            Name = block.ToString(),
            CustomMinimumSize = new Vector2(size.Width, size.Height),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        frame.AddThemeStyleboxOverride("panel", style);
        AddChild(frame);
        _roots[block] = frame;
        return frame;
    }

    /// <summary>一张定尺寸的图。**不碰 <c>TextureFilter</c>** —— 项目级最近邻是文字清晰的唯一依靠。</summary>
    private static TextureRect Picture(string path, int width, int height) => new()
    {
        Name = "Art",
        Texture = Art(path),
        CustomMinimumSize = new Vector2(width, height),
        StretchMode = TextureRect.StretchModeEnum.Keep,
        MouseFilter = MouseFilterEnum.Ignore,
    };

    /// <summary>一根可横向拉伸的条。九宫格只切左右各 1px，中间那两列是纯色，拉开不会花。</summary>
    private static NinePatchRect Stripe(string path)
    {
        var strip = new NinePatchRect { Name = "Strip", Texture = Art(path) };
        strip.SetPatchMargin(Side.Left, 1);
        strip.SetPatchMargin(Side.Right, 1);
        return strip;
    }

    private static Texture2D Art(string path) =>
        ResourceLoader.Exists(path)
            ? GD.Load<Texture2D>(path)
            : throw new FileNotFoundException($"HUD 缺素材：{path}（登记表在 tools/asset-registry.json）");

    private sealed record GaugeRow(Label Label, NinePatchRect Fill);

    private sealed record SkillCell(TextureRect Slot, TextureRect Icon, ColorRect Mask, Label Hint);

    private sealed record TeammateCell(Control Root, TextureRect Portrait, Label Mark,
                                      NinePatchRect Fill);
}
