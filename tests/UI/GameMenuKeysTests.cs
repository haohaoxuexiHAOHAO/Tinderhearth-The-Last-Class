using Tinderhearth.Rules.UI;
using Xunit;

namespace Tinderhearth.Rules.Tests.UI;

/// <summary>打开游戏菜单与直达三个页签的键：绑在哪、按下去游戏菜单怎么变。</summary>
/// <remarks>
/// 这几条失效全都不报错。直达键开成第二个面板，表现是同一页有两种样子；同一个键关不上，表现是「按了没反应」；
/// 越过弹窗开关游戏菜单，表现是一个窗被压在游戏菜单底下、怎么按都回不去。
/// </remarks>
public class GameMenuKeysTests
{
    private static readonly UISurface Confirm =
        new("confirm", UILayer.Dialog, SurfaceKind.View);

    private static NavigationStack Open(params UISurface[] surfaces)
    {
        var nav = new NavigationStack();
        foreach (var surface in surfaces)
        {
            nav.Push(surface);
        }

        return nav;
    }

    [Fact]
    public void 四个开游戏菜单的动作进动作清单_键鼠依次是Tab_B_M_C()
    {
        var expected = new (string Action, InputSymbol Key)[]
        {
            (InputActions.OpenGameMenu, InputSymbol.KeyTab),
            (InputActions.OpenBag, InputSymbol.KeyB),
            (InputActions.OpenMap, InputSymbol.KeyM),
            (InputActions.OpenCharacter, InputSymbol.KeyC),
        };

        foreach (var (action, key) in expected)
        {
            Assert.Contains(action, InputActions.All);
            var keys = InputBindings.For(action, InputDeviceKind.KeyboardMouse);
            Assert.Single(keys);
            Assert.Equal(key, keys[0].Symbol);
        }
    }

    [Fact]
    public void 手柄只给开游戏菜单绑了Start_三个直达键在手柄上是登记过的豁免()
    {
        var pad = InputBindings.For(InputActions.OpenGameMenu, InputDeviceKind.Gamepad);
        Assert.Single(pad);
        Assert.Equal(InputSymbol.PadStart, pad[0].Symbol);

        foreach (var action in GameMenuKeys.TabOf.Keys)
        {
            Assert.Empty(InputBindings.For(action, InputDeviceKind.Gamepad));
            Assert.True(InputBindings.Exemptions.ContainsKey((action, InputDeviceKind.Gamepad)),
                $"{action} 在手柄上没有绑定，也没登记豁免");
        }
    }

    [Fact]
    public void 几个键的清单与直达表对得上()
    {
        // 清单是「每帧问哪几个」，直达表是「各开哪个页签」。漏进一边，那个键要么没人问、要么问了不知道开哪。
        Assert.Equal(InputActions.OpenGameMenu, GameMenuKeys.Actions[0]);
        Assert.DoesNotContain(InputActions.OpenGameMenu, GameMenuKeys.TabOf.Keys);
        Assert.Equal(GameMenuKeys.TabOf.Keys.Order(), GameMenuKeys.Actions.Skip(1).Order());
        Assert.Equal(GameMenuKeys.Actions.Count, GameMenuKeys.Actions.Distinct().Count());
    }

    [Fact]
    public void 直达键指的页签游戏菜单里都有_两种场合都打得开()
    {
        // 哪天某个页签在某个场合里灰了或不显示，这条先失败：那时要先定那个键在那里按下去做什么，
        // 不然它会打开一个点不动的页签。
        foreach (var tabId in GameMenuKeys.TabOf.Values)
        {
            var tab = GameMenu.TabById(tabId);
            Assert.Equal(GameMenuPageState.Available, tab.StateIn(UIContext.Base));
            Assert.Equal(GameMenuPageState.Available, tab.StateIn(UIContext.Level));
        }
    }

    [Fact]
    public void 直达背包落在背包那一页()
    {
        // 「按 B 直接落在背包页」：背包页签打开时落在它的第一个可用子页，那一页就是背包。
        var tab = GameMenu.TabById(GameMenuKeys.TabOf[InputActions.OpenBag]);
        Assert.Equal("bag", tab.LandingIn(UIContext.Base)?.Id);
        Assert.Equal("bag", tab.LandingIn(UIContext.Level)?.Id);
    }

    [Fact]
    public void 关着时按开游戏菜单键_打开并停在上一次那个页签()
    {
        var result = GameMenuKeys.Press(InputActions.OpenGameMenu, Open(), shownTabId: "map");

        Assert.Equal(GameMenuKeyOutcome.Open, result.Outcome);
        Assert.Null(result.TabId);
    }

    [Fact]
    public void 关着时按直达键_打开并落在那个页签()
    {
        foreach (var (action, tabId) in GameMenuKeys.TabOf)
        {
            var result = GameMenuKeys.Press(action, Open(), shownTabId: "codex");

            Assert.Equal(GameMenuKeyOutcome.Open, result.Outcome);
            Assert.Equal(tabId, result.TabId);
        }
    }

    [Fact]
    public void 开着时按另一个直达键_换过去不关()
    {
        var result = GameMenuKeys.Press(InputActions.OpenMap, Open(GameMenu.Surface), shownTabId: "backpack");

        Assert.Equal(GameMenuKeyOutcome.Switch, result.Outcome);
        Assert.Equal("map", result.TabId);
    }

    [Fact]
    public void 开着时按停着的那个页签的直达键_关上()
    {
        foreach (var (action, tabId) in GameMenuKeys.TabOf)
        {
            var result = GameMenuKeys.Press(action, Open(GameMenu.Surface), shownTabId: tabId);

            Assert.Equal(GameMenuKeyOutcome.Close, result.Outcome);
        }
    }

    [Fact]
    public void 开着时再按开游戏菜单键_关上()
    {
        var result = GameMenuKeys.Press(InputActions.OpenGameMenu, Open(GameMenu.Surface), shownTabId: "map");

        Assert.Equal(GameMenuKeyOutcome.Close, result.Outcome);
    }

    [Fact]
    public void 一个页签都用不了时按直达键_换到那个页签()
    {
        var result = GameMenuKeys.Press(InputActions.OpenBag, Open(GameMenu.Surface), shownTabId: null);

        Assert.Equal(GameMenuKeyOutcome.Switch, result.Outcome);
        Assert.Equal("backpack", result.TabId);
    }

    [Fact]
    public void 别的界面在最上面时几个键都不理()
    {
        // 弹窗压在游戏菜单上，与游戏菜单关着、只开着一个弹窗，两种都不理。
        foreach (var nav in new[] { Open(GameMenu.Surface, Confirm), Open(Confirm) })
        {
            foreach (var action in GameMenuKeys.Actions)
            {
                Assert.Equal(GameMenuKeyResult.Ignored, GameMenuKeys.Press(action, nav, shownTabId: "backpack"));
            }
        }
    }

    [Fact]
    public void 不是开游戏菜单的键当场抛()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => GameMenuKeys.Press(InputActions.Interact, Open(), shownTabId: null));
    }

    [Fact]
    public void 按标识取页签拼错当场抛()
    {
        Assert.Equal("map", GameMenu.TabById("map").Id);
        Assert.Throws<KeyNotFoundException>(() => GameMenu.TabById("bakcpack"));
    }
}
