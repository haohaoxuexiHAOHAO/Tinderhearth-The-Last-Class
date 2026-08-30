using Tinderhearth.Rules.Ui;
using Xunit;

namespace Tinderhearth.Rules.Tests.Ui;

/// <summary>
/// `UI-6` 界面骨架的守卫：层级、导航栈、手环可用性与排版单位。
/// </summary>
/// <remarks>
/// 这几条为什么要有测试，而不是写在文档里就算：它们的失效方式都**不会报错**。
/// 返回键被吞掉表现为「按了没反应」；关卡内忘了禁用派工要打到一半才发现；栅格被人从 8 改成
/// 6 只表现为某些界面对不齐。散文管不住这些，断言能。
///
/// **不含任何玩法数值**（`GP-2` 归他自己）。这里测的是排版单位之间的**关系**与界面规则，
/// 不是「某个数应该是几」—— 所以改分辨率或字号时，该失败的是关系被破坏的那一条。
/// </remarks>
public class UiSkeletonTests
{
    private static readonly UiSurface Backpack =
        new("backpack", UiLayer.Panel, PausesWorld: false, SurfaceKind.Carried);

    private static readonly UiSurface Roster =
        new("roster", UiLayer.Panel, PausesWorld: false, SurfaceKind.Manage);

    private static readonly UiSurface Confirm =
        new("confirm", UiLayer.Dialog, PausesWorld: true, SurfaceKind.View);

    [Fact]
    public void 层级自下而上的顺序固定()
    {
        Assert.True(UiLayer.World < UiLayer.WorldSpace);
        Assert.True(UiLayer.WorldSpace < UiLayer.Hud);
        Assert.True(UiLayer.Hud < UiLayer.Panel);
        Assert.True(UiLayer.Panel < UiLayer.Dialog);
        Assert.True(UiLayer.Dialog < UiLayer.Curtain);
    }

    [Fact]
    public void 世界空间UI单独成层而不是混进HUD()
    {
        // 读条要画在执行者身上、跟着角色移动；混进 HUD 层就会变成固定在屏幕角落。
        Assert.NotEqual(UiLayer.Hud, UiLayer.WorldSpace);
        Assert.True(UiLayer.WorldSpace < UiLayer.Hud);
    }

    [Fact]
    public void 返回键逐层退出()
    {
        var nav = new NavigationStack();
        nav.Push(Backpack);
        nav.Push(Confirm);

        Assert.Equal(2, nav.Depth);
        Assert.True(nav.HandleBack());
        Assert.Equal(Backpack, nav.Top);
        Assert.True(nav.HandleBack());
        Assert.Null(nav.Top);
    }

    [Fact]
    public void 栈空时返回键不吞输入()
    {
        // 吞掉的表现是「按了没反应」，玩家会以为游戏卡死 —— 这条是导航栈存在的主要理由之一。
        var nav = new NavigationStack();
        Assert.False(nav.HandleBack());
        Assert.Equal(0, nav.Depth);
    }

    [Fact]
    public void 重复压入同一层只是提到栈顶不重复叠()
    {
        var nav = new NavigationStack();
        nav.Push(Backpack);
        nav.Push(Roster);
        nav.Push(Backpack);

        Assert.Equal(2, nav.Depth);
        Assert.Equal(Backpack, nav.Top);
    }

    [Fact]
    public void 背包打开时不暂停世界()
    {
        // 正典：关卡内允许背包操作且不暂停 —— 随时能暂停整理等于给玩家一个免费的思考窗口。
        var nav = new NavigationStack();
        nav.Push(Backpack);
        Assert.False(nav.WorldShouldPause);

        nav.Push(Confirm);                  // 弹窗要求暂停
        Assert.True(nav.WorldShouldPause);

        nav.Pop();                          // 弹窗关掉后恢复不暂停
        Assert.False(nav.WorldShouldPause);
    }

    [Fact]
    public void 背包在关卡内可用而管理类面板不可用()
    {
        Assert.True(Backpack.AvailableInLevel);      // 随身物不算管理功能
        Assert.False(Roster.AvailableInLevel);       // 派工属于经营侧操作
    }

    [Fact]
    public void 关卡内手环只留查看类标签页()
    {
        var inLevel = Wristband.AvailableIn(UiContext.Level).Select(t => t.Id).ToList();
        var inBase = Wristband.AvailableIn(UiContext.Base).Select(t => t.Id).ToList();

        Assert.Equal(Wristband.Tabs.Count, inBase.Count);        // 基地里全开
        Assert.Contains("codex", inLevel);                       // 图鉴：正典明说可查看
        Assert.Contains("party", inLevel);                       // 队伍状态：同上
        Assert.DoesNotContain("assign", inLevel);                // 派工
        Assert.DoesNotContain("order", inLevel);                 // 订货
        Assert.DoesNotContain("build", inLevel);                 // 建造下单
    }

    [Fact]
    public void 手环标签页拼错时抛而不是静默判成不可用()
    {
        // 静默返回 false 会让「拼错 id」和「这里确实不可用」长得一模一样。
        Assert.Throws<KeyNotFoundException>(() => Wristband.IsEnabled("assgin", UiContext.Base));
    }

    [Fact]
    public void 排版栅格与基础单位和逻辑分辨率都对得上()
    {
        Assert.Equal(0, UiMetrics.BaseUnit % UiMetrics.Grid);      // 栅格整除基础单位
        Assert.Equal(0, UiMetrics.BaseWidth % UiMetrics.Grid);     // 横向不出现半格
        Assert.Equal(0, UiMetrics.BaseHeight % UiMetrics.Grid);    // 纵向同上
        Assert.Equal(0, UiMetrics.SafeMargin % UiMetrics.Grid);    // 安全边距是整数格
    }

    [Fact]
    public void 行高与字号的关系保持整数且面板装得下一行文字()
    {
        Assert.Equal(0, UiMetrics.LineHeight % 2);
        Assert.True(UiMetrics.LineHeight > UiMetrics.FontSize);

        // 1px 边框 + 内边距 + 一行文字 + 内边距 + 1px 边框，必须塞进 24px 高的面板。
        var needed = 1 + UiMetrics.PanelPadding + UiMetrics.FontSize
                     + UiMetrics.PanelPadding + 1;
        Assert.True(needed <= 24, $"一行文字连边框共占 {needed}px，超过 24px 的面板高度");
    }

    [Fact]
    public void 侧视有效视野是逻辑分辨率的一半且能被基础单位整除()
    {
        // 正典把侧视相机定为 2 倍整数缩放，所以能看到的世界只有一半宽高。
        Assert.Equal(320, UiMetrics.SideViewWorldWidth);
        Assert.Equal(180, UiMetrics.SideViewWorldHeight);
        Assert.Equal(0, UiMetrics.SideViewWorldWidth % UiMetrics.BaseUnit);
    }

    [Fact]
    public void 满宽汉字数是下限而不是定值()
    {
        // aspect="expand" 下逻辑宽度会随窗口宽高比撑开，所以这个数只能当地板用。
        Assert.Equal((640 - 8 * 2) / 12, UiMetrics.MaxFullWidthChars);
        Assert.True(UiMetrics.MaxFullWidthChars >= 50);
    }
}
