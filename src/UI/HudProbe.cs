using Godot;
using Tinderhearth.Rules.Foundation.Text;
using Tinderhearth.Rules.Ui;
using Tinderhearth.World;

namespace Tinderhearth.UI;

/// <summary>
/// 启动时把 `UI-8` 的 HUD 真摆一遍并打进日志（脚手架）。
/// </summary>
/// <remarks>
/// **为什么要有它**：规则层测试证明得了「预测的矩形按锚点算对了」，证明不了「节点真的贴在那条
/// 边上」；更证明不了**逻辑宽度撑开时右侧那几块跟着走** —— 而那正是「没有绝对像素坐标」这条
/// 验收标准的实质。写死坐标的界面在 640 宽的窗口上看起来完全正常，只在宽窗口上错位，所以
/// 静态扫代码不够，得真把窗口改成另一个宽高比再量一遍。
///
/// 同理，「数值全部由视图模型传入」静态扫也只能证明「代码里没写死数字」，证明不了「真的读了传
/// 进来那份」—— 界面完全可以拿视图模型当摆设、画一根固定长度的条。所以这里灌三份不同的量，
/// 核条长与自己算的期望像素数相等。
///
/// 判据按组打：**排版、字体、文本、视图模型、修饰键、收尾**。`tools/check_hud.py` 核每组的判据
/// **名字集合与登记的一模一样** —— 漏测一条不会报错，所以条数与名字都要核（同一手法见
/// <see cref="CameraProbe"/> 的两视角比对）。
///
/// **窗口改尺寸那一段需要真窗口。** headless 下 `DisplayServer` 是 dummy 后端，改窗口尺寸不会
/// 让拉伸重算，量出来的两次会一样 —— 那时判据会**假过**。所以这一段显式跳过并打出理由，
/// 而守卫把「跳过」判成失败：`check_hud.py` 不带 `--headless` 跑。
///
/// **`UI-10` 的端到端测试会替换它。** 与 <see cref="InputProbe"/>、<see cref="CameraProbe"/> 同性质。
/// </remarks>
public sealed partial class HudProbe : Node
{
    /// <summary>撑开用的窗口宽度。1298÷2 = 649 逻辑像素，**奇数** —— 居中落点那条要靠它才撞得出来。</summary>
    private const int WideWindowWidth = 1298;

    private const int WindowHeight = 720;

    /// <summary>改窗口尺寸后等几帧再量。两帧是 `Main` 那条实测教训，这里多留一帧余量。</summary>
    private const int FramesAfterResize = 3;

    private readonly LevelHud _hud;
    private readonly InputRouter _router;
    private readonly TextCatalog _text;
    private readonly IReadOnlyList<PixelTheme.Check> _fontChecks;
    private readonly List<Action> _steps = [];

    /// <summary>判据组名。**排版**那一组的名字集合由守卫逐条核，漏一条就判失败。</summary>
    private const string LayoutTag = "排版";

    private readonly Dictionary<HudBlock, Rect2> _narrow = [];
    private readonly Dictionary<HudBlock, Rect2> _wide = [];

    private HudViewModel _restoreModel = null!;
    private Vector2I _restoreWindow;
    private int _narrowWidth;
    private int _wideWidth;
    private int _index;
    private int _checks;
    private int _passed;

    public HudProbe(LevelHud hud, InputRouter router, TextCatalog text,
                    IReadOnlyList<PixelTheme.Check> fontChecks)
    {
        _hud = hud;
        _router = router;
        _text = text;
        _fontChecks = fontChecks;
    }

    /// <summary>自检跑完了。脚手架等这个信号才把 HUD 交回给玩家操作。</summary>
    public event Action? Finished;

    /// <summary>这次运行有真窗口吗。headless 下改窗口尺寸不会让拉伸重算。</summary>
    private static bool HasWindow => DisplayServer.GetName() != "headless";

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        _restoreModel = _hud.Model;
        _restoreWindow = DisplayServer.WindowGetSize();

        CheckFont();
        CheckText();
        BuildSteps();
    }

    public override void _Process(double delta)
    {
        if (_index >= _steps.Count)
        {
            GD.Print("[HUD] 自检 ", _passed, "/", _checks, " 条通过");
            SetProcess(false);
            Finished?.Invoke();
            return;
        }

        _steps[_index++]();
    }

    // ── 字体与文本 ──────────────────────────────────────────────────────

    /// <summary>
    /// 把引擎自报的十项字体属性与 <see cref="PixelFont"/> 逐项比对。
    /// </summary>
    /// <remarks>
    /// 设置在 `.ttf.import`、期望在规则层、实际值由引擎报 —— 三处独立，所以这一组判据真能拦住
    /// 「有人改了导入参数」。每项各打一条，守卫核条数等于
    /// <see cref="PixelFont.PropertyCount"/>，于是漏测一项也躲不过去。
    /// </remarks>
    private void CheckFont()
    {
        var font = PixelTheme.LoadFont();
        GD.Print("[HUD] 字体 ", PixelFont.ResourcePath,
                 " ｜ 版本 ", PixelFont.UpstreamVersion,
                 " ｜ 字号 ", UiMetrics.FontSize, " 行高 ", UiMetrics.LineHeight,
                 " ｜ 全局回退字体已换 ", ThemeDB.FallbackFont == font,
                 " ｜ 回退字号 ", ThemeDB.FallbackFontSize);

        foreach (var check in _fontChecks)
        {
            Check("字体", $"{check.Name} 与 ADR-0008 一致", check.Ok,
                  $"期望 {check.Expected}，引擎报 {check.Actual}");
        }

        // 度量也核一遍：汉字宽 12 是 [ADR-0008] 的实测值，换字体版本或换字形版本这个数会变。
        var size = font.GetStringSize("火种", fontSize: UiMetrics.FontSize);
        Check("字体", "两个全宽汉字的宽度等于两倍字号",
              Mathf.RoundToInt(size.X) == UiMetrics.FontSize * 2,
              $"实测 {size.X:0.##}px，期望 {UiMetrics.FontSize * 2}px");
    }

    /// <summary>HUD 用到的文本键一条都不许缺。缺了会显成 <c>◆缺文本:键◆</c>，那是有意做得显眼的。</summary>
    private void CheckText()
    {
        var missing = _text.MissingKeys(HudDemoModel.RequiredKeys);
        Check("文本", "HUD 用到的文本键一条都不缺", missing.Count == 0,
              $"共 {HudDemoModel.RequiredKeys.Count} 条，缺 {missing.Count} 条"
              + (missing.Count > 0 ? $"：{string.Join("、", missing)}" : string.Empty));
    }

    // ── 步骤 ────────────────────────────────────────────────────────────

    private void BuildSteps()
    {
        Step(() => _narrowWidth = LogicalWidth());

        // ① 当前逻辑尺寸下逐块量，与规则层的预测比对
        Step(Idle);
        Step(() => Measure(_narrow));
        Step(CheckPrediction);

        // ② 撑开逻辑宽度再量一遍。**这一段要真窗口**，理由见类注释。
        if (HasWindow)
        {
            Step(() => Resize(WideWindowWidth));
            Idles(FramesAfterResize);
            Step(() => _wideWidth = LogicalWidth());
            Step(() => Measure(_wide));
            Step(CheckStretch);
            Step(CheckWholePixels);
            Step(() => Resize(_restoreWindow.X));
            Idles(FramesAfterResize);
        }
        else
        {
            Step(() => GD.Print("[HUD] 量 跳过 ｜ 显示后端 ", DisplayServer.GetName(),
                                " 改窗口尺寸不会让拉伸重算，撑开那一段量不出东西 —— "
                                + "这几条由 tools/check_hud.py 在带窗口的运行里判"));
        }

        // ③ 视图模型换几种态：队友为 0、目标为 0、目标已达成、资源量变化
        Step(CheckTeammatesCollapse);
        Step(CheckObjectiveStates);
        Step(CheckValuesComeFromModel);

        // ④ 修饰键按住时显示当前那一组是哪三个技能（`FR-17`）
        Step(AllSkillsReady);
        Step(() => Inject(InputSymbol.PadTriggerLeft, 1f));
        Step(CheckModifierHighlight);
        Step(() => Inject(InputSymbol.PadTriggerLeft, 0f));
        Step(CheckModifierReleased);
        Step(() => Inject(InputSymbol.KeyJ, pressed: true));
        Step(CheckDeviceSwitch);
        Step(() => Inject(InputSymbol.KeyJ, pressed: false));

        // ⑤ 收尾：放置方案与视图模型都还原，别把自检的中间态留给玩家
        Step(Restore);
    }

    private void Measure(Dictionary<HudBlock, Rect2> into)
    {
        foreach (var block in Enum.GetValues<HudBlock>())
        {
            into[block] = _hud.RectOf(block);
        }
    }

    /// <summary>实际矩形与规则层的预测逐块比对，顺带报占屏与可读横带。</summary>
    private void CheckPrediction()
    {
        var width = LogicalWidth();
        var height = Mathf.RoundToInt(GetViewport().GetVisibleRect().Size.Y);
        var bad = new List<string>();

        foreach (var block in Enum.GetValues<HudBlock>())
        {
            var want = HudLayout.RectOf(block, width, height);
            var got = _hud.RectOf(block);
            if (Mathf.RoundToInt(got.Position.X) != want.X
                || Mathf.RoundToInt(got.Position.Y) != want.Y
                || Mathf.RoundToInt(got.Size.X) != want.Width
                || Mathf.RoundToInt(got.Size.Y) != want.Height)
            {
                bad.Add($"{block} 实际 {got.Position.X:0.#},{got.Position.Y:0.#} "
                        + $"{got.Size.X:0.#}x{got.Size.Y:0.#}，预测 {want.X},{want.Y} "
                        + $"{want.Width}x{want.Height}");
            }
        }

        // 根节点铺没铺满单独判一条：不铺满时块的锚点偏移会按错的父矩形算，贴下边与贴右边的三块
        // 会落到负坐标、画在屏幕外，而存图里只表现为「少了三块」，一句报错都没有（2026-08-31 实测）。
        var root = _hud.RootRect;
        Check(LayoutTag, "HUD 根节点铺满视口",
              Mathf.RoundToInt(root.Size.X) == width && Mathf.RoundToInt(root.Size.Y) == height,
              $"根节点 {root.Position.X:0.#},{root.Position.Y:0.#} "
              + $"{root.Size.X:0.#}x{root.Size.Y:0.#}｜视口 {width}x{height}");

        Check(LayoutTag, "四块的实际矩形与规则层预测逐块一致", bad.Count == 0,
              $"逻辑 {width}x{height}｜{(bad.Count == 0 ? "四块全对" : string.Join("；", bad))}");

        // 病根级判据：规则层的算式与**容器自己要的**最小尺寸逐块相等。
        // 只比矩形不够 —— 贴上边的块算式偏大时只往下多长几像素、位置不变，两边照样对得上
        // （2026-08-31 自证时撞出来的）。见 LevelHud.ContentMinOf。
        var drift = new List<string>();
        foreach (var block in Enum.GetValues<HudBlock>())
        {
            var want = HudLayout.SizeOf(block);
            var need = _hud.ContentMinOf(block);
            if (Mathf.RoundToInt(need.X) != want.Width || Mathf.RoundToInt(need.Y) != want.Height)
            {
                drift.Add($"{block} 容器要 {need.X:0.#}x{need.Y:0.#}，算式给 "
                          + $"{want.Width}x{want.Height}");
            }
        }

        Check(LayoutTag, "四块的算式与容器要的最小尺寸逐块相等", drift.Count == 0,
              drift.Count == 0 ? "四块全对（间距与内边距两边算法一致）" : string.Join("；", drift));

        var actor = HudLayout.ActorBand(width, height);
        var overlapping = Enum.GetValues<HudBlock>()
            .Where(b => Overlaps(_hud.RectOf(b), actor))
            .Select(b => b.ToString()).ToList();

        Check(LayoutTag, "四块都不压角色可能出现的那块", overlapping.Count == 0,
              $"角色可读区 {actor.X},{actor.Y} {actor.Width}x{actor.Height}"
              + (overlapping.Count > 0 ? $"｜压住它的：{string.Join("、", overlapping)}" : "｜没有"));

        var band = HudLayout.ClearBand(width, height);
        Check(LayoutTag, "可读横带贯通两端且高于角色可读区",
              band.Width == width && band.Height > actor.Height,
              $"横带 {band.Width}x{band.Height}（占屏高 {(double)band.Height / height:P1}）"
              + $"｜HUD 占屏 {HudLayout.CoverageRatio(width, height):P2}");
    }

    /// <summary>
    /// 撑开逻辑宽度后：左锚点不动、右锚点整幅跟走、居中走一半。
    /// </summary>
    /// <remarks>
    /// **这条是「没有绝对像素坐标」的行为执行体。** 写死坐标时右侧那几块不会动，于是宽窗口上
    /// 它们与屏幕右边缘之间会裂开一条缝 —— 而在 640 宽的窗口上一切看起来完全正常。
    /// </remarks>
    private void CheckStretch()
    {
        var delta = _wideWidth - _narrowWidth;
        var bad = new List<string>();
        var report = new List<string>();

        foreach (var block in Enum.GetValues<HudBlock>())
        {
            if (!_narrow.TryGetValue(block, out var before)
                || !_wide.TryGetValue(block, out var after))
            {
                bad.Add($"{block} 少量了一次");
                continue;
            }

            var moved = after.Position.X - before.Position.X;
            var want = HudLayout.AnchorOf(block) switch
            {
                HudAnchor.TopLeft or HudAnchor.BottomLeft => 0.0f,
                HudAnchor.TopRight or HudAnchor.BottomRight => delta,
                _ => float.NaN,
            };
            report.Add($"{block} 移 {moved:0.#}（应 {want:0.#}）");
            if (!Mathf.IsEqualApprox(moved, want))
            {
                bad.Add($"{block} 移了 {moved:0.#}，应移 {want:0.#}");
            }
        }

        Check(LayoutTag, "逻辑宽度撑开后左锚点不动右锚点跟着走",
              bad.Count == 0 && delta > 0,
              $"逻辑宽 {_narrowWidth}→{_wideWidth}（Δ{delta}）｜" + string.Join("；", report));
    }

    /// <summary>
    /// 四块全部落在整数逻辑像素上。
    /// </summary>
    /// <remarks>
    /// 这条**在奇数逻辑宽度下才有意义**，所以放在撑开那一段里（649 是奇数）。四角贴边这套
    /// 一个居中锚点都不用，所以它应当恒真；立项时另一套候选把技能条摆在底边居中，那时这条会
    /// 报出半像素落点 —— 作者 2026-08-31 选了四角，于是这条从「只允许居中有」变成「一个都不许有」，
    /// 判得更死。理由见 <see cref="HudAnchor"/>。
    /// </remarks>
    private void CheckWholePixels()
    {
        var offenders = new List<string>();
        foreach (var block in Enum.GetValues<HudBlock>())
        {
            var rect = _hud.RectOf(block);
            if (!Mathf.IsEqualApprox(rect.Position.X, Mathf.Round(rect.Position.X))
                || !Mathf.IsEqualApprox(rect.Position.Y, Mathf.Round(rect.Position.Y)))
            {
                offenders.Add($"{block} 在 {rect.Position.X:0.##},{rect.Position.Y:0.##}");
            }
        }

        Check(LayoutTag, "四块全部落在整数逻辑像素上", offenders.Count == 0,
              $"逻辑宽 {_wideWidth}（奇数）｜"
              + (offenders.Count > 0 ? string.Join("、", offenders) : "四块都是整数"));
    }

    private void CheckTeammatesCollapse()
    {
        _hud.Model = HudDemoModel.Build(_text, teammates: 0);
        var collapsed = !_hud.IsShown(HudBlock.Teammates);

        _hud.Model = HudDemoModel.Build(_text, HudLayout.MaxTeammates);
        var shown = _hud.IsShown(HudBlock.Teammates);
        var downMark = _hud.DownMarkShown(2);

        Check("视图模型", "队友为 0 时该区收起、满编时显示且倒地有记号",
              collapsed && shown && downMark,
              $"为 0 收起 {collapsed}｜满编显示 {shown}｜倒地记号 {downMark}");
    }

    private void CheckObjectiveStates()
    {
        var texts = new List<string>();
        var shown = true;
        foreach (var state in Enum.GetValues<HudDemoModel.ObjectiveState>())
        {
            _hud.Model = HudDemoModel.Build(_text, HudLayout.MaxTeammates, state);
            texts.Add($"{state}「{_hud.ObjectiveText}」");
            shown &= _hud.IsShown(HudBlock.Objective) && _hud.ObjectiveText.Length > 0;
        }

        // 三种态都要有字、都要显示 —— 正典要求进度始终可见，为 0 与已达成一样不许隐藏。
        Check("视图模型", "目标进度三种态都显示且都有字", shown, string.Join("｜", texts));
    }

    /// <summary>
    /// 换一份视图模型，屏幕上的量跟着变。**这是「数值全部由视图模型传入」的行为核。**
    /// </summary>
    private void CheckValuesComeFromModel()
    {
        var model = HudDemoModel.Build(_text, HudLayout.MaxTeammates);
        var kinds = Enum.GetValues<HudGaugeKind>();
        var readings = new List<string>();
        var bad = new List<string>();
        const int max = 2;

        foreach (var current in (int[])[0, 1, 2])
        {
            var gauges = kinds.Select((kind, i) => new HudGauge(
                kind, model.Gauges[i].Label,
                i == 0 ? current : model.Gauges[i].Current,
                i == 0 ? max : model.Gauges[i].Max)).ToList();
            _hud.Model = model.WithGauges(gauges);

            var want = Mathf.RoundToInt((float)current / max * HudLayout.GaugeBarWidth);
            var got = _hud.GaugeExtent(0);
            readings.Add($"{current}/{max}→{got}px（应 {want}）");
            if (got != want)
            {
                bad.Add($"{current}/{max} 时条长 {got}，应为 {want}");
            }
        }

        Check("视图模型", "资源条长度真的跟着视图模型走", bad.Count == 0,
              string.Join("｜", readings));

        // 冷却遮罩同理。它是本条唯一与时长相关的表现，而时长本身归 design/数值模型.md。
        _hud.Model = model;
        var mask = _hud.CooldownExtent(1);
        var ready = _hud.CooldownExtent(0);
        Check("视图模型", "冷却遮罩高度跟着剩余比例走且可用时为 0",
              mask > 0 && mask < UiMetrics.IconSmall && ready == 0,
              $"冷却中 {mask}px（图标 {UiMetrics.IconSmall}px）｜可用 {ready}px");
    }

    /// <summary>把六个位都设成已解锁且不在冷却，好让按键记号那几条判据不被空记号绊住。</summary>
    private void AllSkillsReady()
    {
        var model = HudDemoModel.Build(_text, HudLayout.MaxTeammates);
        _hud.Model = model.WithSkills(
            [.. model.Skills.Select(s => s with { Unlocked = true, CooldownRemaining = 0.0 })]);
    }

    private void CheckModifierHighlight()
    {
        var lit = Enumerable.Range(0, InputActions.Skills.Count).Where(_hud.SlotLit).ToList();
        var expected = Enumerable.Range(0, InputActions.SkillsPerGroup).ToList();
        var hints = Enumerable.Range(0, InputActions.Skills.Count).Select(_hud.SlotHint).ToList();
        var wanted = InputActions.Skills
            .Select(a => InputHints.SkillLabel(a, InputDeviceKind.Gamepad)).ToList();

        Check("修饰键", "按住左扳机时正好那一组三个技能位高亮",
              lit.SequenceEqual(expected),
              $"高亮 [{string.Join(",", lit)}]，应为 [{string.Join(",", expected)}]"
              + $"｜生效组 {_router.ActiveSkillGroup}");

        // 不再有「L」「R」记号列（作者 2026-08-31 去掉）：手柄靠组间距 + 高亮 + 图标内面键记号分辨，
        // 所以这里只核每个面键叠的记号对不对，组归属由上一条高亮判据盯着。
        Check("修饰键", "手柄下每个技能位图标内叠的面键记号与该位对应",
              hints.SequenceEqual(wanted),
              $"各位记号 {string.Join("、", hints.Select(h => $"「{h}」"))}"
              + $"｜应为 {string.Join("、", wanted.Select(h => $"「{h}」"))}"
              + $"｜生效组的三个技能 {string.Join("、", _router.ActiveSkills)}");
    }

    private void CheckModifierReleased()
    {
        var lit = Enumerable.Range(0, InputActions.Skills.Count).Where(_hud.SlotLit).ToList();
        Check("修饰键", "松开修饰键后没有技能位还亮着", lit.Count == 0,
              $"仍亮着 [{string.Join(",", lit)}]｜生效组 {_router.ActiveSkillGroup}");
    }

    /// <summary>切回键鼠后不重复画固定的数字键；数字绑定仍由 InputBindings 持有。</summary>
    private void CheckDeviceSwitch()
    {
        var hints = Enumerable.Range(0, InputActions.Skills.Count).Select(_hud.SlotHint).ToList();
        var wanted = Enumerable.Repeat(InputHints.None, InputActions.Skills.Count).ToList();
        Check("修饰键", "切回键鼠后技能位不重复显示数字提示",
              _router.Device == InputDeviceKind.KeyboardMouse && hints.SequenceEqual(wanted),
              $"设备 {_router.Device}｜记号 {string.Join("、", hints.Select(h => $"「{h}」"))}"
              + "｜应全部为空");
    }

    private void Restore()
    {
        _hud.Model = _restoreModel;
        foreach (var action in InputActions.All)
        {
            Input.ActionRelease(action);
        }

        var size = DisplayServer.WindowGetSize();
        Check("收尾", "自检还原了视图模型与窗口尺寸",
              ReferenceEquals(_hud.Model, _restoreModel) && size == _restoreWindow,
              $"视图模型已还原 {ReferenceEquals(_hud.Model, _restoreModel)}"
              + $"｜窗口 {size.X}x{size.Y}（原 {_restoreWindow.X}x{_restoreWindow.Y}）");
    }

    // ── 小工具 ──────────────────────────────────────────────────────────

    private int LogicalWidth() => Mathf.RoundToInt(GetViewport().GetVisibleRect().Size.X);

    private void Resize(int width)
    {
        DisplayServer.WindowSetSize(new Vector2I(width, WindowHeight));
        GD.Print("[HUD] 量 窗口改为 ", width, "x", WindowHeight,
                 " —— 等 ", FramesAfterResize, " 帧让拉伸重算（`Main` 那条实测教训）");
    }

    private void Idles(int frames)
    {
        for (var i = 0; i < frames; i++)
        {
            Step(Idle);
        }
    }

    private static void Idle()
    {
    }

    private void Step(Action step) => _steps.Add(step);

    private static bool Overlaps(Rect2 rect, HudRect other) =>
        rect.Position.X < other.Right && other.X < rect.End.X
        && rect.Position.Y < other.Bottom && other.Y < rect.End.Y;

    /// <summary>注入一次轴位移。事件模板由真正那份翻译产出，所以自检也在验翻译（同 `InputProbe`）。</summary>
    private static void Inject(InputSymbol symbol, float axisValue)
    {
        if (InputMapInstaller.ToEvent(symbol) is not InputEventJoypadMotion motion)
        {
            throw new InvalidOperationException($"{symbol} 不是轴");
        }

        motion.AxisValue = motion.AxisValue < 0 ? -axisValue : axisValue;
        Input.ParseInputEvent(motion);
    }

    /// <summary>注入一次按键按下或松开。</summary>
    private static void Inject(InputSymbol symbol, bool pressed)
    {
        if (InputMapInstaller.ToEvent(symbol) is not InputEventKey key)
        {
            throw new InvalidOperationException($"{symbol} 不是键盘键");
        }

        key.Pressed = pressed;
        Input.ParseInputEvent(key);
    }

    private void Check(string tag, string what, bool ok, string detail)
    {
        _checks++;
        if (ok)
        {
            _passed++;
        }

        GD.Print("[HUD] 判据 ", ok ? "PASS" : "FAIL", " ｜ ", tag, " ｜ ", what, " ｜ ", detail);
    }
}
