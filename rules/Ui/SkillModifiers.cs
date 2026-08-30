namespace Tinderhearth.Rules.Ui;

/// <summary>哪一组技能位当前有效。</summary>
public enum SkillGroup
{
    /// <summary>没按修饰键，面键做本来的事。</summary>
    None,

    /// <summary>左修饰键（手柄 LT）：技能 1–3。</summary>
    Left,

    /// <summary>右修饰键（手柄 RT）：技能 4–6。</summary>
    Right,
}

/// <summary>
/// 修饰键组合的解算（`UI-7`）。**手柄上「按住扳机 + 面键」出技能这件事在这里判。**
/// </summary>
/// <remarks>
/// 为什么必须有这么一个东西，而不是靠 `InputMap` 直接绑组合：实测 `InputEventJoypadButton` 与
/// `InputEventJoypadMotion` 直接继承 `InputEvent`，**一个修饰键字段都没有**（`InputEventKey`
/// 有四个）。`InputMap` 表达不了手柄上的组合，只能在代码里解算。
///
/// 为什么它在规则层：「现在按住修饰键，那这三个面键分别是哪个技能」是**规则**，而且 `UI-8` 要
/// 拿它在 HUD 上显示。放引擎层就只能靠插着手柄实机点，放这里则每条分支都有单元测试盯着。
///
/// **同时按住两个修饰键时先按住的赢。** 反过来（后按的赢）会让玩家在按住 LT 瞄准技能 1 的时候，
/// 因为误碰 RT 而打出技能 4 —— 那是白扣一次 MP 与冷却。先按的赢的失败形态是「误碰不产生任何
/// 效果」，那是两者中该选的那个。松开先按的那个之后，仍按住的那个立刻接上。
/// </remarks>
public sealed class SkillModifierState
{
    /// <summary>按住中的修饰键，按**按下顺序**排列。第 0 个就是当前生效的那个。</summary>
    private readonly List<SkillGroup> _held = [];

    /// <summary>正在被某个面键驱动着的技能位：被遮的动作 → 它这次按下发出的技能位。</summary>
    private readonly Dictionary<string, string> _driving = [];

    /// <summary>当前生效的那一组。</summary>
    public SkillGroup Active => _held.Count > 0 ? _held[0] : SkillGroup.None;

    /// <summary>现在是不是按着修饰键（面键因此被遮）。</summary>
    public bool AnyHeld => _held.Count > 0;

    /// <summary>
    /// 当前这一组是哪三个技能。**这就是验收要的「数据可取」** —— 没按修饰键时是空的。
    /// </summary>
    public IReadOnlyList<string> ActiveSkills => SkillsIn(Active);

    /// <summary>按下一个修饰键。已经按住的重复按下不改变生效组（轴事件会重复到达）。</summary>
    public void Press(SkillGroup group)
    {
        if (group == SkillGroup.None || _held.Contains(group))
        {
            return;
        }

        _held.Add(group);
    }

    /// <summary>松开一个修饰键。没按住的松开是空操作。</summary>
    public void Release(SkillGroup group) => _held.Remove(group);

    /// <summary>
    /// 全松开，返回那些还被按着的技能位**以便调用方逐个发出松开**。
    /// </summary>
    /// <remarks>
    /// 失去窗口焦点时必须调它。不调的话玩家按住 LT 去按 Alt+Tab，回来时修饰键还是「按住」状态，
    /// 而且被驱动的技能位永远收不到松开 —— 表现是一个技能卡在按下不放。
    /// </remarks>
    public IReadOnlyList<string> ReleaseAll()
    {
        _held.Clear();
        var stuck = _driving.Values.ToList();
        _driving.Clear();
        return stuck;
    }

    /// <summary>
    /// 这个动作现在该不该被拦下来（因为它的面键正被当技能键用）。
    /// </summary>
    /// <remarks>
    /// 拦下来这件事**必须由一个门面统一执行**：实测在 `_Input` 里 `SetInputAsHandled` 之后，
    /// `Input.IsActionPressed` 与 `IsActionJustPressed` **仍然是 true**。所以任何直接轮询引擎的
    /// 代码都会在修饰键按住时照旧看到轻攻击被按下，而这种失效不报错。
    ///
    /// 判据只看修饰键此刻按没按，**不看这个动作是不是在修饰键之前就按下了**。也就是说「按住轻
    /// 攻击不放，中途按下修饰键」会让轻攻击的按住状态当场消失。这是有意从简：正典的连段是点按
    /// （固定连段 + 空中连击 + 倒地追打），唯一的按住型动作是防御，而**防御刻意不在被遮之列**。
    /// 将来若真出现按住型攻击，这里要改成「记住按下时机，修饰键之后按下的才遮」。
    /// </remarks>
    public bool ShouldSuppress(string action) =>
        AnyHeld && InputActions.IsShadowedByModifier(action);

    /// <summary>
    /// 一个被遮的动作按下了。返回**该替它发出的技能位**，<c>null</c> 表示不拦、照原动作走。
    /// </summary>
    public string? BeginSkill(string shadowedAction)
    {
        if (!ShouldSuppress(shadowedAction) || SkillFor(shadowedAction) is not string skill)
        {
            return null;
        }

        _driving[shadowedAction] = skill;
        return skill;
    }

    /// <summary>
    /// 一个被遮的动作松开了。返回**该跟着松开的技能位**，<c>null</c> 表示这次松开不对应技能。
    /// </summary>
    /// <remarks>
    /// 这里刻意**不重新算一遍当前是哪一组**，而是查这个面键当初发出的是哪个技能。差别在一个真实
    /// 的顺序上：玩家按住 LT 按下 X（发出技能 1），然后**先松 LT 再松 X**。重新算的话此刻已经没有
    /// 生效组，技能 1 就永远收不到松开，卡在按下状态。
    /// </remarks>
    public string? EndSkill(string shadowedAction) =>
        _driving.Remove(shadowedAction, out var skill) ? skill : null;

    /// <summary>某个面键此刻是不是正在驱动一个技能位。</summary>
    public bool IsDriving(string shadowedAction) => _driving.ContainsKey(shadowedAction);

    /// <summary>
    /// 这个被遮的动作现在对应哪个技能位。没按修饰键、或这个动作不参与组合时返回 <c>null</c>。
    /// </summary>
    public string? SkillFor(string shadowedAction)
    {
        var slot = IndexOfSlot(shadowedAction);
        return slot < 0 || Active == SkillGroup.None ? null : SkillsIn(Active)[slot];
    }

    /// <summary>某一组对应的三个技能位，顺序与 <see cref="InputActions.ShadowedByModifier"/> 一致。</summary>
    public static IReadOnlyList<string> SkillsIn(SkillGroup group) => group switch
    {
        SkillGroup.Left => [.. InputActions.Skills.Take(InputActions.SkillsPerGroup)],
        SkillGroup.Right => [.. InputActions.Skills.Skip(InputActions.SkillsPerGroup)],
        _ => [],
    };

    /// <summary>修饰键动作名 → 组。不是修饰键就返回 <see cref="SkillGroup.None"/>。</summary>
    public static SkillGroup GroupOf(string action) => action switch
    {
        InputActions.SkillGroupLeft => SkillGroup.Left,
        InputActions.SkillGroupRight => SkillGroup.Right,
        _ => SkillGroup.None,
    };

    /// <summary>被遮动作在一组里排第几个。不参与组合时为 −1。</summary>
    private static int IndexOfSlot(string action)
    {
        for (var i = 0; i < InputActions.ShadowedByModifier.Count; i++)
        {
            if (InputActions.ShadowedByModifier[i] == action)
            {
                return i;
            }
        }

        return -1;
    }
}
