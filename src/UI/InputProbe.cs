using Godot;
using Tinderhearth.Rules.Ui;

namespace Tinderhearth.UI;

/// <summary>
/// 启动时把 `UI-7` 的输入链路真跑一遍并打进日志（脚手架）。
/// </summary>
/// <remarks>
/// **为什么要有它**：规则层测试证明得了「先按住的赢」这类判定，证明不了「符号翻译成了对的引擎
/// 枚举」「组合真的经过引擎的事件流」「消费掉的面键不会同时打出攻击」—— 那几件事要有引擎在场。
/// 引擎内测试底座还不存在（归 `ENG-6`），所以照 `UI-3` 的办法来：跑一次、把判据打进
/// `--log-file`，由 `tools/check_input_map.py` 读回判定。
///
/// 能这么做的前提是一条实测：`Input.ParseInputEvent` 在 headless、**一个实体手柄都没接**
/// （`Input.GetConnectedJoypads()` 为空）时照样有效，注入的按下状态还跨帧保持。所以这套自检不
/// 依赖作者插手柄 —— 作者的实机确认是补一道人判（手感与图标），不是唯一判据。
///
/// **`UI-8`／`UI-10` 会用真正的 HUD 与端到端测试替换它。** 与 `Main.ProbeUiSkeleton` 同一性质。
///
/// 断言一律用 <c>Input.IsActionPressed</c> 而不是 <c>IsActionJustPressed</c>：后者依赖帧边界，
/// 在逐帧推进的脚本里会时好时坏，而这里要的是稳定判据。
/// </remarks>
public partial class InputProbe : Node
{
    private readonly InputRouter _router;
    private readonly List<Action> _steps = [];
    private int _index;
    private int _checks;
    private int _passed;

    /// <summary>本自检一共有几条判据。</summary>
    public int Checks => _checks;

    /// <summary>其中通过了几条。</summary>
    public int Passed => _passed;

    public InputProbe(InputRouter router)
    {
        _router = router;
    }

    public override void _Ready()
    {
        DescribeInputMap();
        DescribeSkillGroups();
        BuildSteps();
        ProcessMode = ProcessModeEnum.Always;
    }

    public override void _Process(double delta)
    {
        if (_index >= _steps.Count)
        {
            GD.Print("[输入] 自检 ", _passed, "/", _checks, " 条通过");
            SetProcess(false);
            return;
        }

        _steps[_index++]();
    }

    /// <summary>
    /// 把实际装进 `InputMap` 的东西逐条打出来。
    /// </summary>
    /// <remarks>
    /// 打的是**引擎自报的** `AsText()`，不是我们自己写的符号名。守卫拿它与绑定表里登记的片段比对 ——
    /// 这才是两个独立来源。打我们自己的符号名等于让它自己证明自己。
    /// </remarks>
    private void DescribeInputMap()
    {
        foreach (var action in InputActions.All)
        {
            var events = InputMap.ActionGetEvents(action);
            GD.Print("[输入] 动作 ", action,
                     " 死区 ", InputMap.ActionGetDeadzone(action).ToString("0.00"),
                     " 绑定 ", events.Count, " 条");
            foreach (var e in events)
            {
                GD.Print("[输入]   事件 ", action, " ｜ ", e.AsText());
            }

            CompareBindings(action, events);
        }

        // 界面导航靠引擎内置动作，本条不重定义。**必须把它们打出来**：手柄能不能走焦点导航
        // 取决于内置动作有没有手柄事件，而那是引擎给的默认值 —— 不是我们能假设的东西。
        // 全部事件与手柄事件都打，否则「一条都没有」与「有但不是手柄的」分不开。
        foreach (var builtin in (string[])["ui_up", "ui_down", "ui_left", "ui_right",
                                          "ui_accept", "ui_cancel", "ui_focus_next", "ui_focus_prev"])
        {
            if (!InputMap.HasAction(builtin))
            {
                GD.Print("[输入] 内置 ", builtin, " ｜ 不存在");
                continue;
            }

            var all = InputMap.ActionGetEvents(builtin);
            var pad = all.Where(e => e is InputEventJoypadButton or InputEventJoypadMotion).ToList();
            GD.Print("[输入] 内置 ", builtin, " ｜ 共 ", all.Count, " 条，手柄 ", pad.Count, " 条");
            foreach (var e in all)
            {
                GD.Print("[输入]   内置事件 ", builtin, " ｜ ", e.AsText());
            }
        }

        // 补给内置动作的那几条单独核：内置动作本来就有键鼠事件，所以判据是**包含**而不是全等，
        // 顺带证明补丁没把原有的键鼠事件擦掉（条数只会增不会减）。
        foreach (var (action, bindings) in InputBindings.BuiltinUiPatches)
        {
            var texts = InputMap.ActionGetEvents(action).Select(e => e.AsText()).ToList();
            foreach (var binding in bindings)
            {
                var hit = texts.Count(t => t == binding.EngineText) == 1;
                GD.Print("[输入] 内置补丁核对 ", hit ? "PASS" : "FAIL",
                         " ｜ ", action, " ｜ ", binding.Symbol,
                         " ｜ 期望 ", binding.EngineText,
                         " ｜ 实际 ", string.Join(" ／ ", texts));
            }
        }
    }

    /// <summary>
    /// 把绑定表登记的引擎自报名与引擎实际报出来的**逐条全等比对**。
    /// </summary>
    /// <remarks>
    /// 比对放在这里而不是放在 Python 守卫里，是因为守卫要读期望值就得用正则去解 C# 源码，而那件事
    /// 已经栽过一次：把 `EngineText` 改成跨行拼接之后，正则**静默少解出 12 条**，守卫照样全绿。
    /// 少算比算错更坏 —— 它把「没核」伪装成「通过」。
    ///
    /// 挪进来不损失独立性：两个来源仍是「人写在绑定表里的期望」与「引擎自己报的名字」，谁做减法
    /// 不影响这一点。守卫那边改成核对**条数**（`new(InputSymbol.` 的出现次数是个单 token 模式，
    /// 跨行也数得准）与**零条 FAIL**，于是「比对器自己坏了」也躲不过去。
    /// </remarks>
    private void CompareBindings(string action, Godot.Collections.Array<InputEvent> events)
    {
        var actual = events.Select(e => e.AsText()).OrderBy(t => t).ToList();
        var bindings = InputBindings.Table[action];

        foreach (var binding in bindings)
        {
            var hit = actual.Count(t => t == binding.EngineText);
            var ok = hit == 1;
            GD.Print("[输入] 绑定核对 ", ok ? "PASS" : "FAIL",
                     " ｜ ", action, " ｜ ", binding.Symbol,
                     " ｜ 期望 ", binding.EngineText,
                     " ｜ 实际 ", string.Join(" ／ ", actual));
        }

        if (bindings.Count != actual.Count)
        {
            GD.Print("[输入] 绑定核对 FAIL ｜ ", action, " ｜ 条数",
                     " ｜ 期望 ", bindings.Count, " 条 ｜ 实际 ", actual.Count, " 条");
        }
    }

    private void DescribeSkillGroups()
    {
        foreach (var group in (SkillGroup[])[SkillGroup.Left, SkillGroup.Right])
        {
            GD.Print("[输入] 技能组 ", group, " = ",
                     string.Join("、", SkillModifierState.SkillsIn(group)));
        }

        GD.Print("[输入] 被遮的面键动作 ", string.Join("、", InputActions.ShadowedByModifier),
                 "；不被遮的面键动作 ", InputActions.Dodge);
        GD.Print("[输入] 导航范式 建造页 ", PanelNavigation.ModeFor("build"),
                 "，标签页 ", PanelNavigation.ModeFor("codex"));
    }

    private void BuildSteps()
    {
        // ① 修饰键 + 面键 → 技能位，且原动作被拦
        Step(() => Inject(InputSymbol.PadTriggerLeft, 1f));
        Step(() =>
        {
            Check("左扳机按下后生效组为左",
                _router.ActiveSkillGroup == SkillGroup.Left,
                $"实际 {_router.ActiveSkillGroup}");
            Check("生效组能取到三个技能",
                _router.ActiveSkills.Count == InputActions.SkillsPerGroup,
                string.Join("、", _router.ActiveSkills));
            Inject(InputSymbol.PadFaceLeft, pressed: true);
        });
        Step(() =>
        {
            Check("左扳机+左面键打出技能 1",
                Input.IsActionPressed(InputActions.Skills[0]),
                $"skill_1 按下 {Input.IsActionPressed(InputActions.Skills[0])}");
            Check("门面把被遮的轻攻击判为未按下",
                !_router.IsPressed(InputActions.AttackLight),
                $"门面 {_router.IsPressed(InputActions.AttackLight)}");
            // 这一条是负面证据，专门证明门面为什么必需：引擎的轮询状态**没有**被消费清掉。
            Check("引擎轮询仍认为轻攻击被按下（所以不能直接轮询引擎）",
                Input.IsActionPressed(InputActions.AttackLight),
                $"引擎 {Input.IsActionPressed(InputActions.AttackLight)}");
            Inject(InputSymbol.PadFaceLeft, pressed: false);
        });

        // ② 先松修饰键再松面键，技能位不许卡在按下
        Step(() =>
        {
            Check("松开面键后技能 1 也松开",
                !Input.IsActionPressed(InputActions.Skills[0]),
                $"skill_1 按下 {Input.IsActionPressed(InputActions.Skills[0])}");
            Inject(InputSymbol.PadFaceLeft, pressed: true);
        });
        Step(() => Inject(InputSymbol.PadTriggerLeft, 0f));
        Step(() => Inject(InputSymbol.PadFaceLeft, pressed: false));
        Step(() =>
        {
            Check("先松扳机再松面键，技能 1 不卡在按下",
                !Input.IsActionPressed(InputActions.Skills[0]),
                $"skill_1 按下 {Input.IsActionPressed(InputActions.Skills[0])}");
            Check("修饰键松开后生效组回到无",
                _router.ActiveSkillGroup == SkillGroup.None,
                $"实际 {_router.ActiveSkillGroup}");
        });

        // ③ 两个修饰键同时按住时先按住的赢
        Step(() => Inject(InputSymbol.PadTriggerLeft, 1f));
        Step(() => Inject(InputSymbol.PadTriggerRight, 1f));
        Step(() => Check("两个扳机都按住时先按住的赢",
            _router.ActiveSkillGroup == SkillGroup.Left,
            $"实际 {_router.ActiveSkillGroup}"));
        Step(() => Inject(InputSymbol.PadTriggerLeft, 0f));
        Step(() =>
        {
            Check("松开先按的那个，仍按住的立刻接上",
                _router.ActiveSkillGroup == SkillGroup.Right,
                $"实际 {_router.ActiveSkillGroup}");
            Check("右组的第一个技能是 skill_4",
                _router.ActiveSkills.Count > 0 && _router.ActiveSkills[0] == InputActions.Skills[3],
                string.Join("、", _router.ActiveSkills));
            Inject(InputSymbol.PadTriggerRight, 0f);
        });

        // ④ 扳机死区：压到 0.2 不算按住，压到 0.6 才算
        Step(() => Inject(InputSymbol.PadTriggerRight, 0.2f));
        Step(() => Check("扳机压到 0.2 不算按住修饰键",
            _router.ActiveSkillGroup == SkillGroup.None,
            $"实际 {_router.ActiveSkillGroup}"));
        Step(() => Inject(InputSymbol.PadTriggerRight, 0.6f));
        Step(() =>
        {
            Check("扳机压到 0.6 算按住修饰键",
                _router.ActiveSkillGroup == SkillGroup.Right,
                $"实际 {_router.ActiveSkillGroup}");
            Inject(InputSymbol.PadTriggerRight, 0f);
        });

        // ⑤ 设备切换：按钮一定切，小幅轴不切，大幅轴才切
        Step(() => Inject(InputSymbol.KeyE, pressed: true));
        Step(() =>
        {
            Check("按键把提示切到键鼠",
                _router.Device == InputDeviceKind.KeyboardMouse,
                $"实际 {_router.Device}");
            Inject(InputSymbol.KeyE, pressed: false);
            Inject(InputSymbol.PadStickLeftXPlus, 0.3f);
        });
        Step(() =>
        {
            Check("摇杆小幅漂移不抢走提示",
                _router.Device == InputDeviceKind.KeyboardMouse,
                $"实际 {_router.Device}");
            Inject(InputSymbol.PadStickLeftXPlus, 0.8f);
        });
        Step(() =>
        {
            Check("摇杆推过阈值把提示切到手柄",
                _router.Device == InputDeviceKind.Gamepad,
                $"实际 {_router.Device}");
            Inject(InputSymbol.PadStickLeftXPlus, 0f);
        });

        // ⑥ 收尾：把注入出来的状态全清掉，别把卡住的键留给游戏
        Step(() =>
        {
            foreach (var action in InputActions.All)
            {
                Input.ActionRelease(action);
            }

            // Input.IsActionPressed 有重载，不能直接当方法组传，要包一层。
            var stillDown = InputActions.All.Where(a => Input.IsActionPressed(a)).ToList();
            Check("收尾后没有动作还处于按下",
                stillDown.Count == 0,
                string.Join("、", stillDown));
        });
    }

    private void Step(Action step) => _steps.Add(step);

    private void Check(string what, bool ok, string detail)
    {
        _checks++;
        if (ok)
        {
            _passed++;
        }

        GD.Print("[输入] 判据 ", ok ? "PASS" : "FAIL", " ｜ ", what, " ｜ ", detail);
    }

    /// <summary>注入一次按钮按下或松开。**事件由真正那份翻译产出**，所以自检也在验翻译。</summary>
    private static void Inject(InputSymbol symbol, bool pressed)
    {
        var e = InputMapInstaller.ToEvent(symbol);
        switch (e)
        {
            case InputEventJoypadButton pad:
                pad.Pressed = pressed;
                break;
            case InputEventKey key:
                key.Pressed = pressed;
                break;
            default:
                throw new InvalidOperationException($"{symbol} 不是按钮，用轴的那个重载");
        }

        Input.ParseInputEvent(e);
    }

    /// <summary>注入一次轴位移，值即压下的深度。</summary>
    private static void Inject(InputSymbol symbol, float axisValue)
    {
        if (InputMapInstaller.ToEvent(symbol) is not InputEventJoypadMotion motion)
        {
            throw new InvalidOperationException($"{symbol} 不是轴，用按钮的那个重载");
        }

        // 绑定表里负半轴的事件模板带 −1，注入时要保住方向，只改深度。
        motion.AxisValue = motion.AxisValue < 0 ? -axisValue : axisValue;
        Input.ParseInputEvent(motion);
    }
}
