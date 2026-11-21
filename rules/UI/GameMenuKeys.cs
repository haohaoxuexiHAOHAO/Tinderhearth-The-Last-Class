namespace Tinderhearth.Rules.UI;

/// <summary>
/// 打开游戏菜单与直达三个页签的那几个键按下去，游戏菜单该怎么变：打开、换页签、关上，或者不理。
/// </summary>
/// <remarks>
/// 只判在这一处。直达键打开的是游戏菜单里的那个页签，不是第二个面板：两个入口各判各的，同一页就有两个状态机，
/// 而两处状态迟早对不上。开没开、谁在最上面读导航栈，
/// 停在哪个页签由游戏菜单自己报。
///
/// 每个键都是按一下开、再按一下关：开着时按开游戏菜单的键，或者按停着的那个页签的直达键，就关上；按别的页签的
/// 直达键就换过去，不关。开着时再按没反应的话，玩家分不清是键没绑上还是它本来就不管关。
///
/// 别的界面在最上面时几个键都不理。那时接管输入的是那一个（醒来那个摘要窗这一类），越过它开关游戏菜单，
/// 不是把它压在游戏菜单底下，就是让游戏菜单垫在它底下。
/// </remarks>
public static class GameMenuKeys
{
    /// <summary>这几个键，按这个顺序问：同一帧按了不止一个时，听排在前面的那个。</summary>
    public static readonly IReadOnlyList<string> Actions =
        [InputActions.OpenGameMenu, InputActions.OpenBag, InputActions.OpenMap, InputActions.OpenCharacter];

    /// <summary>三个直达键各开哪个页签。值是页签的标识，页签的家在 <see cref="GameMenu.Tabs"/>。</summary>
    public static readonly IReadOnlyDictionary<string, string> TabOf = new Dictionary<string, string>
    {
        [InputActions.OpenBag] = "backpack",
        [InputActions.OpenMap] = "map",
        [InputActions.OpenCharacter] = "character",
    };

    /// <summary>按下其中一个键。</summary>
    /// <param name="action">按的是哪个，<see cref="Actions"/> 之一；别的动作当场抛。</param>
    /// <param name="navigation">导航栈：游戏菜单开没开、谁在最上面。</param>
    /// <param name="shownTabId">游戏菜单停在哪个页签上；一个页签都用不了时是 <c>null</c>。</param>
    public static GameMenuKeyResult Press(string action, NavigationStack navigation, string? shownTabId)
    {
        ArgumentNullException.ThrowIfNull(navigation);

        string? tab = null;
        if (action != InputActions.OpenGameMenu && !TabOf.TryGetValue(action, out tab))
        {
            throw new ArgumentOutOfRangeException(nameof(action), $"这不是开游戏菜单的键：{action}");
        }

        if (navigation.Top is null)
        {
            return new(GameMenuKeyOutcome.Open, tab);
        }

        if (navigation.Top != GameMenu.Surface)
        {
            return GameMenuKeyResult.Ignored;
        }

        return tab is null || tab == shownTabId
            ? new(GameMenuKeyOutcome.Close, null)
            : new(GameMenuKeyOutcome.Switch, tab);
    }
}

/// <summary>按下一个开游戏菜单的键之后，游戏菜单该怎么变。</summary>
public enum GameMenuKeyOutcome
{
    /// <summary>不理：别的界面在最上面。</summary>
    Ignored,

    /// <summary>打开游戏菜单。</summary>
    Open,

    /// <summary>游戏菜单开着，换到另一个页签。</summary>
    Switch,

    /// <summary>关上游戏菜单。</summary>
    Close,
}

/// <summary>按下一个开游戏菜单的键之后，游戏菜单该怎么变。</summary>
/// <param name="Outcome">开、换、关，或者不理。</param>
/// <param name="TabId">打开或换到哪个页签。开游戏菜单的键打开时是 <c>null</c>：停在上一次停的那个页签。</param>
public sealed record GameMenuKeyResult(GameMenuKeyOutcome Outcome, string? TabId)
{
    /// <summary>不理。</summary>
    public static readonly GameMenuKeyResult Ignored = new(GameMenuKeyOutcome.Ignored, null);
}
