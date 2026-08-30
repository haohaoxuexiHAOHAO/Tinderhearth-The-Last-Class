using Tinderhearth.Rules.Ui;
using Xunit;

namespace Tinderhearth.Rules.Tests.Ui;

/// <summary>
/// `UI-7` 输入映射的守卫：绑定表的完整性、修饰键组合的解算、设备切换与网格光标。
/// </summary>
/// <remarks>
/// 这几条为什么要有测试：它们的失效方式**全都不报错**。少绑一个动作表现为「这个键没反应」；
/// 组合解算写错表现为「打出了别的技能」；先松修饰键再松面键漏掉释放表现为「一个技能卡住不放」；
/// 设备判定没阈值表现为「图标自己跳」。这些在实机上都要碰巧才撞见，散文管不住，断言能。
///
/// **不含任何玩法数值**（`GP-2` 归他自己）。这里测的是绑定表内部的**关系**与输入判定，不是
/// 「某个键应该是哪个」—— 所以改键位时该失败的是关系被破坏的那一条，而不是全部。
///
/// 引擎那一侧（符号翻译成对的引擎枚举、组合真的过了事件流）测不到，规则层不引用 Godot。
/// 那一半由 `tools/check_input_map.py` 起真引擎核，判据打在启动日志里。
/// </remarks>
public class InputMappingTests
{
    [Fact]
    public void 正典点名的每个动作都在动作清单里()
    {
        // 清单来自战斗与关卡 · 按键与连击：七个战斗动作加 6 个技能位。
        foreach (var action in new[]
        {
            InputActions.AttackLight, InputActions.AttackHeavy, InputActions.Guard,
            InputActions.Dodge, InputActions.Sprint, InputActions.Jump, InputActions.Interact,
        })
        {
            Assert.Contains(action, InputActions.All);
        }

        Assert.Equal(6, InputActions.Skills.Count);
        foreach (var skill in InputActions.Skills)
        {
            Assert.Contains(skill, InputActions.All);
        }
    }

    [Fact]
    public void 动作名不重复()
    {
        // 重复的动作名会让后一条绑定静默覆盖前一条。
        Assert.Equal(InputActions.All.Count, InputActions.All.Distinct().Count());
    }

    [Fact]
    public void 每个动作在两个设备族上都有绑定或有写明理由的豁免()
    {
        // 这条是本文件最重要的一条：它让「忘了绑手柄」与「手柄上故意不绑」区分得开。
        foreach (var action in InputActions.All)
        {
            foreach (var device in new[] { InputDeviceKind.KeyboardMouse, InputDeviceKind.Gamepad })
            {
                var bound = InputBindings.For(action, device).Count > 0;
                var exempt = InputBindings.Exemptions.TryGetValue((action, device), out var why);
                Assert.True(bound || exempt,
                    $"{action} 在 {device} 上既没有绑定也没有登记豁免");
                Assert.False(bound && exempt,
                    $"{action} 在 {device} 上既有绑定又登记了豁免：{why}");
                if (exempt)
                {
                    Assert.False(string.IsNullOrWhiteSpace(why), $"{action}／{device} 的豁免没写理由");
                }
            }
        }
    }

    [Fact]
    public void 绑定表覆盖动作清单且不多不少()
    {
        Assert.Equal(InputActions.All.Count, InputBindings.Table.Count);
        foreach (var action in InputActions.All)
        {
            Assert.True(InputBindings.Table.ContainsKey(action), $"绑定表缺 {action}");
        }
    }

    [Fact]
    public void 同一个物理位不被两个动作抢()
    {
        // 抢同一个键的表现是「按一下同时干两件事」，而且只有一件是你想要的。
        var seen = new Dictionary<InputSymbol, string>();
        foreach (var (action, bindings) in InputBindings.Table)
        {
            foreach (var binding in bindings)
            {
                Assert.False(seen.TryGetValue(binding.Symbol, out var other),
                    $"{binding.Symbol} 被 {action} 与 {other} 同时占用");
                seen[binding.Symbol] = action;
            }
        }
    }

    [Fact]
    public void 每条绑定都登记了引擎自报名全文()
    {
        // 这是守卫的比对依据。空着等于那条绑定没人核对编号有没有写错。
        foreach (var (action, bindings) in InputBindings.Table)
        {
            foreach (var binding in bindings)
            {
                Assert.False(string.IsNullOrWhiteSpace(binding.EngineText),
                    $"{action} 的 {binding.Symbol} 没登记引擎自报名");
            }
        }
    }

    [Fact]
    public void 手柄绑定的自报名带上了引擎的编号()
    {
        // 全文比对的价值全在这里：文本里带着 Button N／Axis N，所以「符号翻译成了错的枚举」
        // 会被守卫当场抓住。只写 Xbox RT 那种叫法是抓不住轴 4 与轴 5 弄反的。
        foreach (var (action, bindings) in InputBindings.Table)
        {
            foreach (var binding in bindings.Where(b => b.Device == InputDeviceKind.Gamepad))
            {
                Assert.True(
                    binding.EngineText.Contains("Joypad Button ")
                        || binding.EngineText.Contains("on Axis "),
                    $"{action} 的 {binding.Symbol} 的自报名里没有引擎编号：{binding.EngineText}");
            }
        }
    }

    [Fact]
    public void 键盘绑定的自报名表明用的是物理键位()
    {
        // AZERTY 上 WASD 的物理位置是 ZQSD，按字符绑会让那批玩家的移动键散开。
        // 引擎给物理键位的自报名带 "- Physical" 后缀，所以这条能机器判。
        // Shift 是例外：引擎给修饰键只打 "Shift"，没有后缀。
        foreach (var (_, bindings) in InputBindings.Table)
        {
            foreach (var binding in bindings)
            {
                if (binding.Symbol is InputSymbol.KeyShift
                    || binding.Device == InputDeviceKind.Gamepad)
                {
                    continue;
                }

                Assert.EndsWith(" - Physical", binding.EngineText);
            }
        }
    }

    [Fact]
    public void 设备族由符号推出且键鼠与手柄分得开()
    {
        // 边界就在键盘段的最后一个与手柄段的第一个之间，所以两端各取一个盯住。
        Assert.Equal(InputDeviceKind.KeyboardMouse, new InputBinding(InputSymbol.KeyQ, "Q").Device);
        Assert.Equal(InputDeviceKind.KeyboardMouse, new InputBinding(InputSymbol.KeyJ, "J").Device);
        Assert.Equal(InputDeviceKind.KeyboardMouse,
            new InputBinding(InputSymbol.Digit6, "6").Device);
        Assert.Equal(InputDeviceKind.Gamepad,
            new InputBinding(InputSymbol.PadFaceBottom, "Xbox A").Device);
        Assert.Equal(InputDeviceKind.Gamepad,
            new InputBinding(InputSymbol.PadTriggerRight, "Xbox RT").Device);
    }

    [Fact]
    public void 轻重攻击在键鼠上相邻且都是手指静止位()
    {
        // 正典的连段是固定连段加空中连击，要求轻重攻击能快速交替，所以两者必须相邻。
        // J 与 K 分别是右手食指与中指的静止位（作者 2026-08-30 定，原为鼠标左右键）。
        Assert.Equal(InputSymbol.KeyJ,
            InputBindings.For(InputActions.AttackLight, InputDeviceKind.KeyboardMouse)[0].Symbol);
        Assert.Equal(InputSymbol.KeyK,
            InputBindings.For(InputActions.AttackHeavy, InputDeviceKind.KeyboardMouse)[0].Symbol);
        Assert.Equal(1, (int)InputSymbol.KeyK - (int)InputSymbol.KeyJ);
    }

    [Fact]
    public void 战斗动作一个都不绑鼠标键()
    {
        // 侧视战斗没有瞄准，鼠标的长处用不上却占着右手。真要给鼠标当改键选项是将来的事，
        // 那时往符号表里加回来 —— 这条测试会跟着失败，把人领到那段说明。
        foreach (var (_, bindings) in InputBindings.Table)
        {
            foreach (var binding in bindings)
            {
                Assert.DoesNotContain("Mouse", binding.EngineText);
            }
        }
    }

    [Fact]
    public void 三个被遮的面键恰好铺满一组技能位()
    {
        // 这条把两张表钉在一起：被遮清单的长度就是一组的技能数，两组就是全部六个。
        Assert.Equal(InputActions.SkillsPerGroup, InputActions.ShadowedByModifier.Count);
        Assert.Equal(InputActions.SkillsPerGroup * 2, InputActions.Skills.Count);
    }

    [Fact]
    public void 闪避不在被遮之列()
    {
        // 正典把闪避定为唯一带无敌帧的脱身手段，还专门为失衡结束设了逃生窗口 ——
        // 它是最不能在任何窗口里失效的动作，所以占第四个面键。
        Assert.DoesNotContain(InputActions.Dodge, InputActions.ShadowedByModifier);
        Assert.False(new SkillModifierState().ShouldSuppress(InputActions.Dodge));
    }

    [Fact]
    public void 防御不在被遮之列()
    {
        // 防御是唯一的按住型动作（普通防御持续耗 SP），被遮会让按住状态中途消失。
        Assert.DoesNotContain(InputActions.Guard, InputActions.ShadowedByModifier);
    }

    [Fact]
    public void 被遮的三个动作绑的就是手柄的三个面键()
    {
        // 防「有人把跳跃从面键挪走了，却忘了从被遮清单里删掉」。
        var faces = new[] { InputSymbol.PadFaceLeft, InputSymbol.PadFaceTop, InputSymbol.PadFaceBottom };
        foreach (var action in InputActions.ShadowedByModifier)
        {
            var pad = InputBindings.For(action, InputDeviceKind.Gamepad);
            Assert.Single(pad);
            Assert.Contains(pad[0].Symbol, faces);
        }

        // 第四个面键归闪避。
        var dodge = InputBindings.For(InputActions.Dodge, InputDeviceKind.Gamepad);
        Assert.Single(dodge);
        Assert.Equal(InputSymbol.PadFaceRight, dodge[0].Symbol);
    }

    [Fact]
    public void 两个修饰键绑的是扳机而不是肩键()
    {
        // 肩键留给防御与交互：正典的精准防御要求帧级准确，而扳机有行程（作者 2026-08-30 定）。
        Assert.Equal(InputSymbol.PadTriggerLeft,
            InputBindings.For(InputActions.SkillGroupLeft, InputDeviceKind.Gamepad)[0].Symbol);
        Assert.Equal(InputSymbol.PadTriggerRight,
            InputBindings.For(InputActions.SkillGroupRight, InputDeviceKind.Gamepad)[0].Symbol);
        Assert.Equal(InputSymbol.PadShoulderLeft,
            InputBindings.For(InputActions.Guard, InputDeviceKind.Gamepad)[0].Symbol);
        Assert.Equal(InputSymbol.PadShoulderRight,
            InputBindings.For(InputActions.Interact, InputDeviceKind.Gamepad)[0].Symbol);
    }

    [Fact]
    public void 扳机的死区显式高于引擎默认值()
    {
        // 引擎默认 0.2（已实测）。搭在扳机上的手指压两成就算按住，会误触发修饰键。
        Assert.Equal(0.5f, InputBindings.TriggerDeadzone);
        Assert.Equal(0.5f, InputBindings.DeadzoneFor(InputActions.SkillGroupLeft));
        Assert.Equal(0.5f, InputBindings.DeadzoneFor(InputActions.SkillGroupRight));

        // 摇杆与按钮不登记死区，用引擎默认值。
        Assert.Null(InputBindings.DeadzoneFor(InputActions.MoveLeft));
        Assert.Null(InputBindings.DeadzoneFor(InputActions.Jump));
    }

    [Fact]
    public void 键鼠六个技能位是数字键1到6()
    {
        var expected = new[]
        {
            InputSymbol.Digit1, InputSymbol.Digit2, InputSymbol.Digit3,
            InputSymbol.Digit4, InputSymbol.Digit5, InputSymbol.Digit6,
        };
        for (var i = 0; i < InputActions.Skills.Count; i++)
        {
            var keys = InputBindings.For(InputActions.Skills[i], InputDeviceKind.KeyboardMouse);
            Assert.Single(keys);
            Assert.Equal(expected[i], keys[0].Symbol);
        }
    }

    [Fact]
    public void 修饰键按住时面键映到对应的技能位()
    {
        var state = new SkillModifierState();
        state.Press(SkillGroup.Left);

        Assert.Equal(InputActions.Skills[0], state.SkillFor(InputActions.AttackLight));
        Assert.Equal(InputActions.Skills[1], state.SkillFor(InputActions.AttackHeavy));
        Assert.Equal(InputActions.Skills[2], state.SkillFor(InputActions.Jump));

        state.Release(SkillGroup.Left);
        state.Press(SkillGroup.Right);

        Assert.Equal(InputActions.Skills[3], state.SkillFor(InputActions.AttackLight));
        Assert.Equal(InputActions.Skills[4], state.SkillFor(InputActions.AttackHeavy));
        Assert.Equal(InputActions.Skills[5], state.SkillFor(InputActions.Jump));
    }

    [Fact]
    public void 没按修饰键时面键不映到技能位也不被遮()
    {
        var state = new SkillModifierState();

        Assert.Equal(SkillGroup.None, state.Active);
        Assert.Empty(state.ActiveSkills);
        Assert.Null(state.SkillFor(InputActions.AttackLight));
        Assert.False(state.ShouldSuppress(InputActions.AttackLight));
        Assert.Null(state.BeginSkill(InputActions.AttackLight));   // 不拦，照原动作走
    }

    [Fact]
    public void 修饰键按住时当前是哪三个技能可取()
    {
        // 这是验收要的「数据可取」。HUD 怎么显示归 UI-8。
        var state = new SkillModifierState();

        state.Press(SkillGroup.Left);
        Assert.Equal(new[] { "skill_1", "skill_2", "skill_3" }, state.ActiveSkills);

        state.Release(SkillGroup.Left);
        state.Press(SkillGroup.Right);
        Assert.Equal(new[] { "skill_4", "skill_5", "skill_6" }, state.ActiveSkills);
    }

    [Fact]
    public void 两个修饰键都按住时先按住的赢()
    {
        // 后按的赢会让玩家瞄着技能 1 却因为误碰打出技能 4 —— 白扣一次 MP 与冷却。
        var state = new SkillModifierState();
        state.Press(SkillGroup.Left);
        state.Press(SkillGroup.Right);

        Assert.Equal(SkillGroup.Left, state.Active);

        state.Release(SkillGroup.Left);
        Assert.Equal(SkillGroup.Right, state.Active);   // 松开先按的，仍按住的立刻接上

        state.Release(SkillGroup.Right);
        Assert.Equal(SkillGroup.None, state.Active);
    }

    [Fact]
    public void 重复按下同一个修饰键不改变生效组()
    {
        // 扳机是模拟轴，越过死区之后同一次按住会持续来事件。
        var state = new SkillModifierState();
        state.Press(SkillGroup.Right);
        state.Press(SkillGroup.Left);
        state.Press(SkillGroup.Right);

        Assert.Equal(SkillGroup.Right, state.Active);

        state.Release(SkillGroup.Right);
        Assert.Equal(SkillGroup.Left, state.Active);    // 只按住过一次，一次松开就干净
    }

    [Fact]
    public void 先松修饰键再松面键时技能位不卡在按下()
    {
        // 真实操作顺序，也是最容易漏的一条：松开时若重新算「当前是哪一组」，此刻已经没有生效组，
        // 技能位就永远收不到松开。
        var state = new SkillModifierState();
        state.Press(SkillGroup.Left);

        Assert.Equal(InputActions.Skills[0], state.BeginSkill(InputActions.AttackLight));
        Assert.True(state.IsDriving(InputActions.AttackLight));

        state.Release(SkillGroup.Left);
        Assert.Equal(SkillGroup.None, state.Active);
        Assert.Equal(InputActions.Skills[0], state.EndSkill(InputActions.AttackLight));
        Assert.False(state.IsDriving(InputActions.AttackLight));
    }

    [Fact]
    public void 没在驱动技能的面键松开时不发出多余的松开()
    {
        var state = new SkillModifierState();
        Assert.Null(state.EndSkill(InputActions.AttackLight));
    }

    [Fact]
    public void 失去焦点时全松开并交出还按着的技能位()
    {
        // 不交出来的话，玩家按住扳机去 Alt+Tab，回来时那个技能卡在按下不放。
        var state = new SkillModifierState();
        state.Press(SkillGroup.Right);
        state.BeginSkill(InputActions.AttackHeavy);

        var stuck = state.ReleaseAll();

        Assert.Equal(new[] { InputActions.Skills[4] }, stuck);
        Assert.Equal(SkillGroup.None, state.Active);
        Assert.False(state.AnyHeld);
        Assert.False(state.IsDriving(InputActions.AttackHeavy));
    }

    [Fact]
    public void 修饰键动作名与组一一对应()
    {
        Assert.Equal(SkillGroup.Left, SkillModifierState.GroupOf(InputActions.SkillGroupLeft));
        Assert.Equal(SkillGroup.Right, SkillModifierState.GroupOf(InputActions.SkillGroupRight));
        Assert.Equal(SkillGroup.None, SkillModifierState.GroupOf(InputActions.AttackLight));
    }

    [Fact]
    public void 离散按下一定切换设备提示()
    {
        var tracker = new InputDeviceTracker();

        Assert.Equal(InputDeviceKind.KeyboardMouse, tracker.Current);   // PC 首发，初始按键鼠
        Assert.True(tracker.Notice(InputDeviceKind.Gamepad, InputSignalKind.Press));
        Assert.Equal(InputDeviceKind.Gamepad, tracker.Current);

        // 同一设备再来一次不算「变了」，调用方不该白刷一遍图标。
        Assert.False(tracker.Notice(InputDeviceKind.Gamepad, InputSignalKind.Press));
    }

    [Fact]
    public void 摇杆漂移不抢走设备提示()
    {
        // 手柄搁在桌上有零点几的漂移。没阈值的话提示会自己在两个设备之间跳。
        var tracker = new InputDeviceTracker();

        Assert.False(tracker.Notice(InputDeviceKind.Gamepad, InputSignalKind.Axis, 0.3f));
        Assert.Equal(InputDeviceKind.KeyboardMouse, tracker.Current);

        Assert.True(tracker.Notice(InputDeviceKind.Gamepad, InputSignalKind.Axis, 0.8f));
        Assert.Equal(InputDeviceKind.Gamepad, tracker.Current);
    }

    [Fact]
    public void 轴的切换阈值高于摇杆死区且负方向同样算()
    {
        Assert.True(InputDeviceTracker.AxisSwitchThreshold > 0.2f);      // 高于引擎默认死区
        var tracker = new InputDeviceTracker();
        Assert.True(tracker.Notice(InputDeviceKind.Gamepad, InputSignalKind.Axis, -0.9f));
    }

    [Fact]
    public void 纯移动永远不切换设备提示()
    {
        // 鼠标被碰一下不代表玩家换了设备；真要用鼠标玩他会点下去，而点击是 Press。
        var tracker = new InputDeviceTracker();
        tracker.Notice(InputDeviceKind.Gamepad, InputSignalKind.Press);

        Assert.False(tracker.Notice(InputDeviceKind.KeyboardMouse, InputSignalKind.Motion));
        Assert.Equal(InputDeviceKind.Gamepad, tracker.Current);
    }

    [Fact]
    public void 内置的确认与返回都补了手柄绑定()
    {
        // 实测：Godot 4.7.2 的默认值里方向四个有手柄事件，ui_accept 与 ui_cancel 一条都没有。
        // 不补的话手柄能挪焦点却按不下去也退不出来，而 UiRoot 的返回键就是 ui_cancel。
        Assert.Equal(2, InputBindings.BuiltinUiPatches.Count);
        Assert.Equal(InputSymbol.PadFaceBottom, InputBindings.BuiltinUiPatches["ui_accept"][0].Symbol);
        Assert.Equal(InputSymbol.PadFaceRight, InputBindings.BuiltinUiPatches["ui_cancel"][0].Symbol);
    }

    [Fact]
    public void 内置补丁只补手柄不碰键鼠()
    {
        // 补丁若含键鼠绑定，就有把 Enter／Escape 覆盖掉的风险 —— 那是「修好手柄反而弄坏键鼠」。
        foreach (var (action, bindings) in InputBindings.BuiltinUiPatches)
        {
            Assert.NotEmpty(bindings);
            foreach (var binding in bindings)
            {
                Assert.Equal(InputDeviceKind.Gamepad, binding.Device);
                Assert.False(string.IsNullOrWhiteSpace(binding.EngineText),
                    $"{action} 的补丁没登记引擎自报名");
            }
        }
    }

    [Fact]
    public void 内置补丁用的面键与玩法动作的重叠是已知的()
    {
        // 下面键同时是跳跃、右面键同时是闪避。这条重叠是有意接受的（面板里由拿到焦点的控件消费
        // ui_accept），但轮询不受消费影响，所以「不暂停的面板打开时该不该屏蔽玩法动作」另记 UI-11。
        // 这条测试的作用是：有人改了面键分配时，这里会失败并把人领到那段说明。
        Assert.Equal(InputSymbol.PadFaceBottom,
            InputBindings.For(InputActions.Jump, InputDeviceKind.Gamepad)[0].Symbol);
        Assert.Equal(InputSymbol.PadFaceRight,
            InputBindings.For(InputActions.Dodge, InputDeviceKind.Gamepad)[0].Symbol);
    }

    [Fact]
    public void 列表与标签页走焦点网格摆放走光标()
    {
        Assert.Equal(PanelNavigationMode.Cursor, PanelNavigation.ModeFor("build"));
        Assert.Equal(PanelNavigationMode.Focus, PanelNavigation.ModeFor("codex"));
        Assert.Equal(PanelNavigationMode.Focus, PanelNavigation.ModeFor("assign"));
    }

    [Fact]
    public void 走光标的标签页每个都真的是手环的标签页()
    {
        // 拼错 id 会静默退回焦点导航，实机上只表现为「有点难用」。
        foreach (var tabId in PanelNavigation.CursorNavigatedTabs)
        {
            Assert.Contains(tabId, Wristband.Tabs.Select(t => t.Id));
        }
    }

    [Fact]
    public void 网格光标钳制在边界内而不环绕()
    {
        // 环绕会让镜头突然横跨整张地图，而正典要求相机行为可预期。
        var cursor = new GridCursor(40, 30);

        Assert.Equal(0, cursor.Column);
        Assert.Equal(0, cursor.Row);

        Assert.False(cursor.Move(-1, 0));           // 贴着左边界推，没动
        Assert.Equal(0, cursor.Column);

        Assert.True(cursor.Move(1, 1));
        Assert.Equal((1, 1), (cursor.Column, cursor.Row));

        cursor.MoveTo(39, 29);
        Assert.False(cursor.Move(1, 1));            // 贴着右下角推，没动
        Assert.Equal((39, 29), (cursor.Column, cursor.Row));
    }

    [Fact]
    public void 网格尺寸必须由调用方给且不接受非正数()
    {
        // FR-24：可建造区尺寸从配置读，不写死。连默认值都不给 —— 给了就会有人省掉传参。
        Assert.Throws<ArgumentOutOfRangeException>(() => new GridCursor(0, 30));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GridCursor(40, -1));
    }

    [Fact]
    public void 跳到越界的格子要抛而不是静默钳制()
    {
        // 静默钳制会把「坐标算错了」藏起来。
        var cursor = new GridCursor(40, 30);
        Assert.Throws<ArgumentOutOfRangeException>(() => cursor.MoveTo(40, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => cursor.MoveTo(0, -1));
    }

    [Fact]
    public void 绑定表里问不存在的动作要抛()
    {
        Assert.Throws<KeyNotFoundException>(
            () => InputBindings.For("attak_light", InputDeviceKind.KeyboardMouse));
    }
}
