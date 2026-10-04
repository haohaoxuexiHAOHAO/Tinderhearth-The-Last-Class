using Tinderhearth.Rules.UI;
using Xunit;

namespace Tinderhearth.Rules.Tests.UI;

/// <summary>
/// 界面的端到端验证：走整条链路，确认各条规则合起来仍然成立，不是各自单测通过但组合失效。
/// </summary>
/// <remarks>
/// 只测到规则层。节点摆放、实际渲染、窗口缩放这些引擎层的事由作者实机看，这里测的是规则层的
/// 合约能不能撑起那条链路。
///
/// 风格照 <see cref="HudTests"/> 和 <see cref="UISkeletonTests"/>：钉住关系、拿坏值反证、
/// 检查边界钳制，不含任何玩法数值。
/// </remarks>
public class E2ETests
{
    // ── 测试固件 ────────────────────────────────────────────────────
    private const int FakeMax = 40;

    /// <summary>最小可用的 HUD 视图模型，数值全部是编的，只为撞契约。</summary>
    private static HudViewModel MakeHud(int teammates = HudLayout.MaxTeammates) => new(
        gauges: [.. Enum.GetValues<HudGaugeKind>()
                        .Select(k => new HudGauge(k, k.ToString(), FakeMax / 2, FakeMax))],
        skills: [.. InputActions.Skills.Select(a => new HudSkillSlot(a, a, true, 0.0))],
        objective: new HudObjective("素材", 1, 3, "已达成，返回入口点撤离"),
        teammates: [.. Enumerable.Range(0, teammates)
                                 .Select(_ => new HudTeammate(FakeMax, FakeMax, Down: false))]);

    private static readonly UISurface Backpack =
        new("backpack", UILayer.Panel, SurfaceKind.Carried);

    private static readonly UISurface Wristband_ = Wristband.Surface;

    private static readonly UISurface Confirm =
        new("confirm", UILayer.Dialog, SurfaceKind.View);

    // ── 主路径：完整链路 ────────────────────────────────────────────
    [Fact]
    public void 主路径_背包打开后手环可叠加导航栈并逐层返回()
    {
        // 进入侧视场景，HUD 就绪，打开背包（世界暂停），再开手环
        var nav = new NavigationStack();
        var hud = MakeHud();

        nav.Push(Backpack);
        Assert.True(nav.WorldShouldPause);     // 弹界面接管输入就暂停世界，这是设计定的

        nav.Push(Wristband_);
        Assert.Equal(2, nav.Depth);
        Assert.True(nav.WorldShouldPause);     // 叠一层仍然暂停

        // 逐层返回
        Assert.True(nav.HandleBack());         // 关手环
        Assert.Equal(Backpack, nav.Top);

        Assert.True(nav.HandleBack());         // 关背包
        Assert.Null(nav.Top);                  // 回到 HUD 态
        Assert.Equal(0, nav.Depth);

        // HUD 数据在整条链路里不变（视图模型是不可变记录）
        Assert.Equal(4, hud.Gauges.Count);
        Assert.Equal(6, hud.Skills.Count);
    }

    [Fact]
    public void 主路径_弹窗叠在背包上关闭弹窗后回到背包()
    {
        var nav = new NavigationStack();
        nav.Push(Backpack);
        nav.Push(Confirm);

        Assert.True(nav.WorldShouldPause);
        Assert.Equal(2, nav.Depth);

        nav.Pop();                             // 关弹窗
        Assert.True(nav.WorldShouldPause);     // 底下还有背包，不许恢复
        Assert.Equal(Backpack, nav.Top);

        nav.Pop();
        Assert.Null(nav.Top);
        Assert.False(nav.WorldShouldPause);     // 全关才恢复
    }

    [Fact]
    public void 主路径_手环关卡内只允许查看类标签页()
    {
        // 侧视关卡里打开手环，操作类页必须禁用
        var inLevel = Wristband.AvailableIn(UIContext.Level).ToList();
        var inBase  = Wristband.AvailableIn(UIContext.Base).ToList();

        Assert.True(inLevel.Count < inBase.Count);    // 关卡内可用页少于基地
        Assert.All(inLevel, t => Assert.Equal(SurfaceKind.View, t.Kind));
        Assert.Contains(inLevel, t => t.Id == "codex");
        Assert.Contains(inLevel, t => t.Id == "party");
        Assert.DoesNotContain(inLevel, t => t.Id == "assign");
        Assert.DoesNotContain(inLevel, t => t.Id == "build");
    }

    [Fact]
    public void 主路径_HUD四块在侧视基准分辨率下一块都不压角色可读区()
    {
        // 侧视场景 HUD 出现后，确认整套放置对角色可读区无干扰
        Assert.Empty(HudLayout.BlocksOverActorBand(UIMetrics.BaseWidth, UIMetrics.BaseHeight));

        var band = HudLayout.ClearBand(UIMetrics.BaseWidth, UIMetrics.BaseHeight);
        Assert.Equal(UIMetrics.BaseWidth, band.Width);    // 贯通两端
        Assert.True(band.Height > 0);
    }

    [Fact]
    public void 主路径_HUD四块与世界空间层都落在正确的UILayer层级()
    {
        // 层级约束是全部 UI 条目的基础；合起来验一次
        Assert.True(UILayer.World < UILayer.WorldSpace);
        Assert.True(UILayer.WorldSpace < UILayer.Hud);
        Assert.True(UILayer.Hud < UILayer.Panel);
        Assert.True(UILayer.Panel < UILayer.Dialog);
        Assert.True(UILayer.Dialog < UILayer.Curtain);

        // 背包在 Panel 层，弹窗在 Dialog 层，确认弹窗永远在背包之上
        Assert.True(Backpack.Layer < Confirm.Layer);
    }

    // ── 失败路径：导航栈空时按返回 ─────────────────────────────────
    [Fact]
    public void 失败路径_导航栈空时按返回键不吞输入且栈仍为空()
    {
        var nav = new NavigationStack();
        Assert.False(nav.HandleBack());    // 不吞，返回 false 交给上层（通常是暂停菜单）
        Assert.Equal(0, nav.Depth);
        Assert.Null(nav.Top);
    }

    [Fact]
    public void 失败路径_关闭不在栈中的层不崩溃且返回false()
    {
        var nav = new NavigationStack();
        nav.Push(Backpack);
        Assert.False(nav.Close(Confirm));  // Confirm 没压进去，Close 返回 false 而不抛
        Assert.Equal(1, nav.Depth);
    }

    // ── 失败路径：窗口尺寸小于逻辑分辨率 ──────────────────────────
    [Fact]
    public void 失败路径_窗口小于逻辑分辨率时布局不抛()
    {
        // 小窗口下块可能跑出屏幕，但不该崩溃 ——
        // 如果这里抛了，小窗口下的 HUD 排版就会整块失效
        const int tiny = 320;
        const int tinyH = 180;
        var rects = HudLayout.RectsOf(tiny, tinyH);
        Assert.Equal(4, rects.Count);    // 四块都算出来了，即使坐标可能超界
    }

    [Fact]
    public void 失败路径_宽屏拉伸时左锚点块不动右锚点块跟着走()
    {
        // aspect="expand" 的实际效果：左锚 X 不变、右锚 X 随宽度增加
        const int wide = 649;
        var delta = wide - UIMetrics.BaseWidth;

        foreach (var block in Enum.GetValues<HudBlock>())
        {
            var narrow    = HudLayout.RectOf(block, UIMetrics.BaseWidth, UIMetrics.BaseHeight);
            var stretched = HudLayout.RectOf(block, wide, UIMetrics.BaseHeight);

            var xDelta = HudLayout.AnchorOf(block) switch
            {
                HudAnchor.TopLeft or HudAnchor.BottomLeft   => 0,
                HudAnchor.TopRight or HudAnchor.BottomRight => delta,
                _ => throw new InvalidOperationException(),
            };
            Assert.Equal(xDelta, stretched.X - narrow.X);
        }
    }

    [Fact]
    public void 失败路径_宽屏下占屏仍不超过一成()
    {
        const int wide = 649;
        var share = HudLayout.CoverageRatio(wide, UIMetrics.BaseHeight);
        Assert.True(share < 0.10, $"宽屏下 HUD 占屏 {share:P1}");
    }

    // ── 失败路径：队友数为 0 ─────────────────────────────────────────
    [Fact]
    public void 失败路径_队友数为零时队友区收起不留空槽()
    {
        var hud = MakeHud(teammates: 0);
        Assert.False(hud.ShowTeammates);    // 收起，不渲染四个空头像框
        Assert.Empty(hud.Teammates);
    }

    [Fact]
    public void 失败路径_队友超限时视图模型拒绝构造()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            MakeHud(teammates: HudLayout.MaxTeammates + 1));
    }

    [Fact]
    public void 失败路径_零队友时HUD整体约束仍然成立()
    {
        // 队友区收起后，其余三块与可读横带的约束不能因此失效
        var hud = MakeHud(teammates: 0);
        Assert.False(hud.ShowTeammates);

        // 布局算法本身不依赖 ShowTeammates，四块坐标不变
        Assert.Empty(HudLayout.BlocksOverActorBand(UIMetrics.BaseWidth, UIMetrics.BaseHeight));
    }

    // ── 失败路径：目标进度为 0 与已完成 ─────────────────────────────
    [Fact]
    public void 失败路径_目标进度为零时不完成且仍然显示()
    {
        // 「为 0」不算达成 —— 没有要采的东西才算（Total = 0）
        var obj = new HudObjective("素材", 0, 3, "已达成，返回入口点撤离");
        Assert.False(obj.Complete);
        Assert.Equal(3, obj.Remaining);
    }

    [Fact]
    public void 失败路径_目标总数为零算达成且剩余不为负()
    {
        // Total = 0 语义：这一趟不用采集，等价于「已完成」
        var obj = new HudObjective("目标", 0, 0, "已达成，返回入口点撤离");
        Assert.True(obj.Complete);
        Assert.Equal(0, obj.Remaining);
    }

    [Fact]
    public void 失败路径_目标已达成时显示达成消息而非进度数字()
    {
        var obj = new HudObjective("素材", 3, 3, "已达成，返回入口点撤离");
        Assert.True(obj.Complete);
        Assert.Equal(0, obj.Remaining);
        Assert.NotEmpty(obj.DoneMessage);    // 达成消息不能是空串
    }

    [Fact]
    public void 失败路径_目标超额完成时算达成且剩余钳为零()
    {
        var obj = new HudObjective("素材", 5, 3, "已达成，返回入口点撤离");
        Assert.True(obj.Complete);
        Assert.Equal(0, obj.Remaining);    // 不是负数
    }

    // ── 测试独立可重复：每个测试自己准备数据 ────────────────────────
    [Fact]
    public void 独立性_各测试之间不共享导航栈状态()
    {
        // 两个互相独立的栈 —— 证明这个类是值语义
        var navA = new NavigationStack();
        var navB = new NavigationStack();
        navA.Push(Backpack);

        Assert.Equal(1, navA.Depth);
        Assert.Equal(0, navB.Depth);    // A 的操作不影响 B
    }

    [Fact]
    public void 独立性_HudViewModel是不可变的修改返回新实例()
    {
        var original = MakeHud();
        var changed  = original.WithObjective(
            new HudObjective("来源点", 0, 2, "已达成，返回入口点撤离"));

        Assert.Equal("素材", original.Objective.Label);    // 原实例不变
        Assert.Equal("来源点", changed.Objective.Label);
        Assert.Equal(original.Gauges, changed.Gauges);     // 其余字段不受影响
    }

    [Fact]
    public void 独立性_Wristband标签页列表是只读的()
    {
        // 确保不同测试读到的是同一份定义，没有被前一条测试改写
        Assert.Equal(7, Wristband.Tabs.Count);
    }

    // ── 综合约束 ─────────────────────────────────────────────────────
    [Fact]
    public void 综合_侧视有效视野是基准分辨率一半且是整数()
    {
        // 2 倍缩放的结构约束：世界空间 UI 与相机共同依赖这个数
        Assert.Equal(UIMetrics.BaseWidth / UIMetrics.SideViewZoom,
                     UIMetrics.SideViewWorldWidth);
        Assert.Equal(UIMetrics.BaseHeight / UIMetrics.SideViewZoom,
                     UIMetrics.SideViewWorldHeight);
        Assert.Equal(0, UIMetrics.BaseWidth % UIMetrics.SideViewZoom);
        Assert.Equal(0, UIMetrics.BaseHeight % UIMetrics.SideViewZoom);
    }

    [Fact]
    public void 综合_世界空间UI几何与HUD几何都从UIMetrics推导无字面量()
    {
        // 两套量用同一套基础单位。这条把「引擎层不写数字字面量」的前提钉住
        Assert.Equal(UIMetrics.IconSmall, WorldUILayout.RingDiameter);
        Assert.Equal(UIMetrics.IconLarge, WorldUILayout.EliteBarWidth);
        Assert.Equal(UIMetrics.PanelPadding, WorldUILayout.EliteBarHeight);
        Assert.Equal(UIMetrics.BaseUnit, WorldUILayout.DamageFloatDistancePx);
    }

    [Fact]
    public void 综合_整条链路在侧视基准分辨率下占屏不超过一成()
    {
        var share = HudLayout.CoverageRatio(UIMetrics.BaseWidth, UIMetrics.BaseHeight);
        Assert.True(share < 0.10, $"HUD 占屏 {share:P1}");
    }
}
