namespace Tinderhearth.Rules.UI;

/// <summary>
/// 打开手环与直达三个页签的那几个键按下去，手环该怎么变：打开、换页签、关上，或者不理。
/// </summary>
/// <remarks>
/// 只判在这一处。直达键打开的是手环里的那个页签，不是第二个面板：两个入口各判各的，同一页就有两个状态机，
/// 而两处状态迟早对不上（设计仓 design/proposals/键鼠技能位挪到右手.md）。开没开、谁在最上面读导航栈，
/// 停在哪个页签由手环自己报。
///
/// 每个键都是按一下开、再按一下关：开着时按开手环的键，或者按停着的那个页签的直达键，就关上；按别的页签的
/// 直达键就换过去，不关。开着时再按没反应的话，玩家分不清是键没绑上还是它本来就不管关。
///
/// 别的界面在最上面时几个键都不理。那时接管输入的是那一个（醒来那个摘要窗这一类），越过它开关手环，
/// 不是把它压在手环底下，就是让手环垫在它底下。
/// </remarks>
public static class WristbandKeys
{
    /// <summary>这几个键，按这个顺序问：同一帧按了不止一个时，听排在前面的那个。</summary>
    public static readonly IReadOnlyList<string> Actions =
        [InputActions.OpenWristband, InputActions.OpenBag, InputActions.OpenMap, InputActions.OpenCharacter];

    /// <summary>三个直达键各开哪个页签。值是页签的标识，页签的家在 <see cref="Wristband.Tabs"/>。</summary>
    public static readonly IReadOnlyDictionary<string, string> TabOf = new Dictionary<string, string>
    {
        [InputActions.OpenBag] = "backpack",
        [InputActions.OpenMap] = "map",
        [InputActions.OpenCharacter] = "character",
    };

    /// <summary>按下其中一个键。</summary>
    /// <param name="action">按的是哪个，<see cref="Actions"/> 之一；别的动作当场抛。</param>
    /// <param name="navigation">导航栈：手环开没开、谁在最上面。</param>
    /// <param name="shownTabId">手环停在哪个页签上；一个页签都用不了时是 <c>null</c>。</param>
    public static WristbandKeyResult Press(string action, NavigationStack navigation, string? shownTabId)
    {
        ArgumentNullException.ThrowIfNull(navigation);

        string? tab = null;
        if (action != InputActions.OpenWristband && !TabOf.TryGetValue(action, out tab))
        {
            throw new ArgumentOutOfRangeException(nameof(action), $"这不是开手环的键：{action}");
        }

        if (navigation.Top is null)
        {
            return new(WristbandKeyOutcome.Open, tab);
        }

        if (navigation.Top != Wristband.Surface)
        {
            return WristbandKeyResult.Ignored;
        }

        return tab is null || tab == shownTabId
            ? new(WristbandKeyOutcome.Close, null)
            : new(WristbandKeyOutcome.Switch, tab);
    }
}

/// <summary>按下一个开手环的键之后，手环该怎么变。</summary>
public enum WristbandKeyOutcome
{
    /// <summary>不理：别的界面在最上面。</summary>
    Ignored,

    /// <summary>打开手环。</summary>
    Open,

    /// <summary>手环开着，换到另一个页签。</summary>
    Switch,

    /// <summary>关上手环。</summary>
    Close,
}

/// <summary>按下一个开手环的键之后，手环该怎么变。</summary>
/// <param name="Outcome">开、换、关，或者不理。</param>
/// <param name="TabId">打开或换到哪个页签。开手环的键打开时是 <c>null</c>：停在上一次停的那个页签。</param>
public sealed record WristbandKeyResult(WristbandKeyOutcome Outcome, string? TabId)
{
    /// <summary>不理。</summary>
    public static readonly WristbandKeyResult Ignored = new(WristbandKeyOutcome.Ignored, null);
}
