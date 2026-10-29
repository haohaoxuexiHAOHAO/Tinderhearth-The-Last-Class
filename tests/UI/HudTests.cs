using Tinderhearth.Rules.UI;
using Xunit;

namespace Tinderhearth.Rules.Tests.UI;

/// <summary>HUD 的测试：两种场合各自的排版关系与放置、视图模型契约与像素字体取值。</summary>
/// <remarks>
/// 这些要有测试，因为失效方式全都不报错。块高从行高倍数变成随手取的数，只表现为某一行文字与
/// 旁边的条差半格；关卡里一块越过顶上那一段，只表现为人站到那一排时被挡住；视图模型少给一条资源，
/// 只表现为界面上少一根条，而没人记得本来有几根。
///
/// 这里不含任何玩法数值，测的是排版量之间的关系和界面契约，不是「生命上限该是几」。视图模型里
/// 的数字都是当场编的假值，用来撞契约。
/// </remarks>
public class HudTests
{
    // ── 一份最小可用的假视图模型。数字全是编的，只为撞契约 ──
    private const int FakeMax = 40;

    /// <summary>实测过的那个宽窗口的逻辑宽度（3840×2130 的窗口得到 649×360）。</summary>
    private const int Wide = 649;

    private static List<HudGauge> FakeGauges(UIContext context) =>
        [.. HudLayout.GaugesIn(context).Select(k => new HudGauge(k, k.ToString(), FakeMax / 2, FakeMax))];

    private static HudViewModel Fake(int teammates = HudLayout.MaxTeammates) => new(
        gauges: FakeGauges(UIContext.Level),
        skills: [.. InputActions.Skills.Select(a => new HudSkillSlot(a, a, true, 0.0))],
        objective: new HudObjective("素材", 1, 3, "已达成"),
        teammates: [.. Enumerable.Range(0, teammates)
                                 .Select(_ => new HudTeammate(FakeMax, FakeMax, Down: false))]);

    // ── 排版关系 ────────────────────────────────────────────────────

    [Theory]
    [InlineData(UIContext.Level)]
    [InlineData(UIContext.Base)]
    public void 每一块的宽高都是正整数(UIContext context)
    {
        foreach (var block in HudLayout.BlocksIn(context))
        {
            var size = HudLayout.SizeOf(block, context);
            Assert.True(size.Width > 0, $"{context} 的 {block} 宽 {size.Width}");
            Assert.True(size.Height > 0, $"{context} 的 {block} 高 {size.Height}");
        }
    }

    [Fact]
    public void 关卡资源块每条一行再加一行状态位()
    {
        // 不成倍数的话标签基线与条会差半格，而那种错只在并排看两块时才发现。
        var content = HudLayout.ContentSizeOf(HudBlock.Resources, UIContext.Level);
        Assert.Equal(0, content.Height % UIMetrics.LineHeight);
        Assert.Equal(HudLayout.GaugesIn(UIContext.Level).Count * HudLayout.GaugeRowHeight,
                     content.Height - HudLayout.StatusRowHeight);
    }

    [Fact]
    public void 基地资源块是那支精力条加一行状态位且与关卡那一块同宽()
    {
        // 同宽是为了状态位那一行两边一样长：同一个状态在两种场合里落在同一处。
        var content = HudLayout.ContentSizeOf(HudBlock.Resources, UIContext.Base);
        Assert.Equal(HudLayout.VigorTubeHeight + HudLayout.StatusRowHeight, content.Height);
        Assert.Equal(HudLayout.ContentSizeOf(HudBlock.Resources, UIContext.Level).Width, content.Width);

        // 标签、间距与那支管子放得进那一段。
        Assert.True(HudLayout.GaugeLabelSize + UIMetrics.ItemGap + HudLayout.VigorTubeWidth <= content.Width);
    }

    [Theory]
    [InlineData(UIContext.Level)]
    [InlineData(UIContext.Base)]
    public void 外框等于内容加两边框边(UIContext context)
    {
        // 占屏比例算的是外框。漏掉框边会让「HUD 占几成」偏小，而那个数正是选放置方案的依据。
        foreach (var block in HudLayout.BlocksIn(context))
        {
            var content = HudLayout.ContentSizeOf(block, context);
            var outer = HudLayout.SizeOf(block, context);
            var (x, y) = HudLayout.InsetOf(block);
            Assert.Equal(content.Width + (x * 2), outer.Width);
            Assert.Equal(content.Height + (y * 2), outer.Height);
        }
    }

    [Fact]
    public void 资源条与标签都比行高矮且能在一行里居中()
    {
        // 差值为奇数时居中会落在半格上。
        foreach (var height in (int[])[HudLayout.GaugeBarHeight, HudLayout.GaugeLabelSize])
        {
            Assert.True(height < UIMetrics.LineHeight);
            Assert.Equal(0, (UIMetrics.LineHeight - height) % 2);
        }

        Assert.Equal(0, (HudLayout.VigorTubeHeight - HudLayout.GaugeLabelSize) % 2);
    }

    [Fact]
    public void 那条横排格子照画好的栏排()
    {
        // 两头的框边、每格一个格子、格与格之间一段格距。格数与技能位同源。
        var outer = HudLayout.SizeOf(HudBlock.ActionBar, UIContext.Level);
        var cells = HudLayout.ActionBarCellCount;
        Assert.Equal(InputActions.Skills.Count, cells);
        Assert.Equal((HudLayout.ActionBarCapX * 2) + (cells * UIMetrics.Cell) + ((cells - 1) * HudLayout.ActionBarCellGap),
                     outer.Width);
        Assert.Equal((HudLayout.ActionBarCapY * 2) + UIMetrics.Cell, outer.Height);
    }

    [Fact]
    public void 选中框那一圈不出那条栏也不压到邻格()
    {
        // 选中框画在格子外面一圈。出了栏就压到栏外的画面上，压到邻格就分不清选中的是哪一格。
        Assert.True(UIMetrics.SelectOutset <= HudLayout.ActionBarCapX, "选中框横着出了那条栏");
        Assert.True(UIMetrics.SelectOutset <= HudLayout.ActionBarCapY, "选中框竖着出了那条栏");
        Assert.True(UIMetrics.SelectOutset * 2 <= HudLayout.ActionBarCellGap, "挨着的两格各画一圈选中框会叠在一起");
    }

    [Fact]
    public void 队友块容纳编队上限四名()
    {
        var content = HudLayout.ContentSizeOf(HudBlock.Teammates, UIContext.Level);
        var expected = (HudLayout.MaxTeammates * HudLayout.PortraitSize)
                       + ((HudLayout.MaxTeammates - 1) * UIMetrics.ItemGap);
        Assert.Equal(expected, content.Width);
        Assert.Equal(HudLayout.PortraitSize + HudLayout.GaugeBarHeight, content.Height);
        Assert.Equal(4, HudLayout.MaxTeammates);        // 正典：主角 + 1 至 4 名学生
    }

    [Fact]
    public void 目标块按最长那句定宽而不是按常态()
    {
        // 「已达成，返回入口点撤离」11 个全宽汉字。按短的定宽会让达成态被截断，
        // 而正典要求进度始终可见 —— 达成那句正是最需要看清的时刻。
        var content = HudLayout.ContentSizeOf(HudBlock.Objective, UIContext.Level);
        Assert.True(content.Width >= "已达成，返回入口点撤离".Length * UIMetrics.FontSize);
        Assert.Equal(UIMetrics.LineHeight, content.Height);
    }

    [Theory]
    [InlineData(-0.5, HudLayout.VigorLiquidInset)]
    [InlineData(0.0, HudLayout.VigorLiquidInset)]
    [InlineData(1.0, HudLayout.VigorTubeHeight - HudLayout.VigorLiquidInset)]
    [InlineData(2.0, HudLayout.VigorTubeHeight - HudLayout.VigorLiquidInset)]
    public void 精力条露出的高度按液面那一段算且钳在两端(double ratio, int expected)
    {
        // 为 0 时露的只有底下那段透明，为 1 时液面整段露出、管顶那段透明照旧不露。
        Assert.Equal(expected, HudLayout.VigorShownHeight(ratio));
    }

    [Fact]
    public void 精力条每多一成露出的只多不少()
    {
        var shown = Enumerable.Range(0, 11).Select(i => HudLayout.VigorShownHeight(i / 10.0)).ToList();
        Assert.Equal(shown.OrderBy(h => h), shown);
        Assert.Equal(HudLayout.VigorLiquidHeight, shown[^1] - shown[0]);
    }

    // ── 关卡那一套：全部收在顶上那一段 ────────────────────────────────

    [Theory]
    [InlineData(UIMetrics.BaseWidth)]
    [InlineData(Wide)]
    public void 关卡那一套每一块都不越过顶上那一段(int viewportWidth)
    {
        // 战斗片段就是一屏、人能站到任何横坐标上，纵向账只给 HUD 留了顶上那一段。
        Assert.Empty(HudLayout.BlocksBelowTopBand(viewportWidth, UIMetrics.BaseHeight));
    }

    [Fact]
    public void 反证_关卡任一块挪进纵深带就被判出来()
    {
        // 上面那条用的就是这个判定。它得真能判出越界，否则上面那条只会说「通过」。
        foreach (var (block, rect) in HudLayout.RectsOf(UIContext.Level, UIMetrics.BaseWidth, UIMetrics.BaseHeight))
        {
            Assert.False(HudLayout.ExceedsTopBand(rect), $"{block} 本来就越界了");

            var flush = rect with { Y = HudLayout.LevelTopBandHeight - rect.Height };
            Assert.False(HudLayout.ExceedsTopBand(flush), $"{block} 底边贴着那一段的下沿不算越界");

            var intruding = flush with { Y = flush.Y + 1 };
            Assert.True(HudLayout.ExceedsTopBand(intruding), $"{block} 挪进纵深带一像素没被判出来");
        }
    }

    [Fact]
    public void 关卡那一套全贴顶边()
    {
        foreach (var block in HudLayout.BlocksIn(UIContext.Level))
        {
            Assert.True(HudLayout.PlacementOf(block, UIContext.Level).Anchor is HudAnchor.TopLeft or HudAnchor.TopRight,
                        $"关卡里的 {block} 没贴顶边");
        }
    }

    [Fact]
    public void 技能栏的位置只按队友块的尺寸算()
    {
        // 单人出征时队友块收起，技能栏不跟着上移：位置固定才有肌肉记忆。放置只吃尺寸、不吃显隐，
        // 所以它等于「安全边距、队友块的高、一个栅格」，与队友块显不显示无关。
        var placement = HudLayout.PlacementOf(HudBlock.ActionBar, UIContext.Level);
        var teammates = HudLayout.PlacementOf(HudBlock.Teammates, UIContext.Level);
        Assert.Equal(teammates.Anchor, placement.Anchor);
        Assert.Equal(teammates.InsetX, placement.InsetX);
        Assert.Equal(teammates.InsetY + HudLayout.SizeOf(HudBlock.Teammates, UIContext.Level).Height + UIMetrics.Grid,
                     placement.InsetY);
    }

    // ── 基地那一套：照旧贴四角，判据是跟随死区 ────────────────────────

    [Theory]
    [InlineData(UIMetrics.BaseWidth)]
    [InlineData(Wide)]
    [InlineData(1280)]
    public void 基地那一套一块都不压角色可能出现的那块(int viewportWidth)
    {
        Assert.Empty(HudLayout.BlocksOverActorBand(viewportWidth, UIMetrics.BaseHeight));
    }

    [Fact]
    public void 角色可读区取死区加一个本体且不乘缩放()
    {
        // 两种视角的相机都不缩放，俯视本体两个基础单位在屏上就是两个基础单位。
        var band = HudLayout.ActorBand(UIMetrics.BaseWidth, UIMetrics.BaseHeight);
        Assert.Equal((CameraFeel.DeadzoneHalfWidthScreenPx * 2) + UIMetrics.IconLarge, band.Width);
        Assert.Equal((CameraFeel.DeadzoneHalfHeightScreenPx * 2) + UIMetrics.IconLarge, band.Height);
        Assert.Equal(UIMetrics.BaseWidth / 2, band.X + (band.Width / 2));
        Assert.Equal(UIMetrics.BaseHeight / 2, band.Y + (band.Height / 2));
    }

    [Fact]
    public void 基地那一套贴的角各不相同()
    {
        var anchors = HudLayout.BlocksIn(UIContext.Base)
            .Select(b => HudLayout.PlacementOf(b, UIContext.Base).Anchor).ToList();
        Assert.Equal(anchors.Count, anchors.Distinct().Count());
    }

    [Fact]
    public void 横跨屏幕中心线时可读横带判成零()
    {
        // 反证：上面那条检查不能只会说「通过」。矩形重叠判定本身也顺带撞一次。
        var band = new HudRect(0, UIMetrics.BaseHeight / 2, UIMetrics.BaseWidth, 0);
        Assert.Equal(0, band.Height);
        var center = new HudRect(0, (UIMetrics.BaseHeight / 2) - 1, UIMetrics.BaseWidth, 2);
        Assert.True(center.Overlaps(HudLayout.ActorBand(
            UIMetrics.BaseWidth, UIMetrics.BaseHeight)));
    }

    // ── 两种场合都要守的 ────────────────────────────────────────────

    [Theory]
    [InlineData(UIContext.Level)]
    [InlineData(UIContext.Base)]
    public void 留出一条通到两端的可读横带(UIContext context)
    {
        // 左右两端的敌人与平台跟正中间一样要紧，所以量的是「整幅宽的横带」而不是「最大空白面积」。
        // 下限取五分之三：它必须比角色可读区宽出一截，否则一点排版变动就把余量吃光。
        var band = HudLayout.ClearBand(context, UIMetrics.BaseWidth, UIMetrics.BaseHeight);
        Assert.Equal(UIMetrics.BaseWidth, band.Width);
        Assert.True(band.Height >= UIMetrics.BaseHeight * 3 / 5, $"{context} 的可读横带只有 {band.Height}px");
        Assert.True(band.Height > HudLayout.ActorBand(UIMetrics.BaseWidth, UIMetrics.BaseHeight).Height);
    }

    [Theory]
    [InlineData(UIContext.Level, UIMetrics.BaseWidth)]
    [InlineData(UIContext.Level, Wide)]
    [InlineData(UIContext.Base, UIMetrics.BaseWidth)]
    [InlineData(UIContext.Base, Wide)]
    public void 占屏不超过一成(UIContext context, int viewportWidth)
    {
        var share = HudLayout.CoverageRatio(context, viewportWidth, UIMetrics.BaseHeight);
        Assert.True(share < 0.10, $"{context} 的 HUD 占屏 {share:P1}，超过一成就开始挤中间那块");
    }

    [Theory]
    [InlineData(UIContext.Level)]
    [InlineData(UIContext.Base)]
    public void 逻辑宽度撑开时左锚点不动右锚点跟着走(UIContext context)
    {
        // 视口按 expand 拉伸时逻辑宽度是个变量。这条把「用锚点而不是写死坐标」变成算得出来的性质。
        var delta = Wide - UIMetrics.BaseWidth;

        foreach (var block in HudLayout.BlocksIn(context))
        {
            var narrow = HudLayout.RectOf(block, context, UIMetrics.BaseWidth, UIMetrics.BaseHeight);
            var stretched = HudLayout.RectOf(block, context, Wide, UIMetrics.BaseHeight);
            var expected = HudLayout.PlacementOf(block, context).Anchor switch
            {
                HudAnchor.TopLeft or HudAnchor.BottomLeft => 0,
                HudAnchor.TopRight or HudAnchor.BottomRight => delta,
                _ => throw new InvalidOperationException(),
            };
            Assert.Equal(expected, stretched.X - narrow.X);
            Assert.Equal(narrow.Y, stretched.Y);        // 高度锁在 360，纵向不该动
        }
    }

    [Theory]
    [InlineData(UIContext.Level)]
    [InlineData(UIContext.Base)]
    public void 没有居中锚点且块宽都是偶数(UIContext context)
    {
        // 这条挡的是「有人把居中加回来」：加回来会让它失败，逼人先读 HudAnchor 的注释（奇数逻辑宽度下
        // 居中块落在半格上）。块宽一律偶数，所以将来真要居中时也只在逻辑宽度为奇数时才落到半格。
        Assert.DoesNotContain("Center", string.Join(",", Enum.GetNames<HudAnchor>()));

        foreach (var block in HudLayout.BlocksIn(context))
        {
            Assert.Equal(0, HudLayout.SizeOf(block, context).Width % 2);
        }
    }

    [Theory]
    [InlineData(UIContext.Level, UIMetrics.BaseWidth)]
    [InlineData(UIContext.Level, Wide)]
    [InlineData(UIContext.Base, UIMetrics.BaseWidth)]
    [InlineData(UIContext.Base, Wide)]
    public void 每一块都在屏幕内且留着安全边距(UIContext context, int viewportWidth)
    {
        foreach (var (block, rect) in HudLayout.RectsOf(context, viewportWidth, UIMetrics.BaseHeight))
        {
            Assert.True(rect.X >= UIMetrics.SafeMargin, $"{block} 左边越过安全边距");
            Assert.True(rect.Y >= UIMetrics.SafeMargin, $"{block} 上边越过安全边距");
            Assert.True(rect.Right <= viewportWidth - UIMetrics.SafeMargin, $"{block} 右边越过安全边距");
            Assert.True(rect.Bottom <= UIMetrics.BaseHeight - UIMetrics.SafeMargin, $"{block} 下边越过安全边距");
        }
    }

    [Theory]
    [InlineData(UIContext.Level)]
    [InlineData(UIContext.Base)]
    public void 各块两两不重叠(UIContext context)
    {
        var rects = HudLayout.RectsOf(context, UIMetrics.BaseWidth, UIMetrics.BaseHeight);
        for (var i = 0; i < rects.Count; i++)
        {
            for (var j = i + 1; j < rects.Count; j++)
            {
                Assert.False(rects[i].Rect.Overlaps(rects[j].Rect),
                             $"{context} 里 {rects[i].Block} 与 {rects[j].Block} 重叠");
            }
        }
    }

    [Theory]
    [InlineData(UIMetrics.BaseWidth)]
    [InlineData(Wide)]
    public void 技能栏与随身栏同宽都贴右且横坐标对齐(int viewportWidth)
    {
        // 换场景时格位不跳：两种内容是同一条栏、落在同一批横坐标上，只是纵向一个在顶上、一个在底边。
        var level = HudLayout.RectOf(HudBlock.ActionBar, UIContext.Level, viewportWidth, UIMetrics.BaseHeight);
        var home = HudLayout.RectOf(HudBlock.ActionBar, UIContext.Base, viewportWidth, UIMetrics.BaseHeight);
        Assert.Equal(level.Width, home.Width);
        Assert.Equal(level.X, home.X);
        Assert.Equal(HudLayout.ActionBarCellCount, HudLayout.CarrySlotCount);

        Assert.Equal(HudAnchor.TopRight, HudLayout.PlacementOf(HudBlock.ActionBar, UIContext.Level).Anchor);
        Assert.Equal(HudAnchor.BottomRight, HudLayout.PlacementOf(HudBlock.ActionBar, UIContext.Base).Anchor);
    }

    [Fact]
    public void 场合里没有的块要不到尺寸也要不到放置()
    {
        // 静默给一个尺寸的话，基地里会多出一块没人画的空框，而占屏那笔账照算不误。
        Assert.Throws<ArgumentOutOfRangeException>(() => HudLayout.PlacementOf(HudBlock.Objective, UIContext.Base));
        Assert.Throws<ArgumentOutOfRangeException>(() => HudLayout.SizeOf(HudBlock.Teammates, UIContext.Base));
    }

    // ── 视图模型契约 ────────────────────────────────────────────────

    [Fact]
    public void 关卡资源块恰好生命体力法力基地恰好精力()
    {
        Assert.Equal((HudGaugeKind[])[HudGaugeKind.Health, HudGaugeKind.Stamina, HudGaugeKind.Mana],
                     HudLayout.GaugesIn(UIContext.Level));
        Assert.Equal((HudGaugeKind[])[HudGaugeKind.DailyVigor], HudLayout.GaugesIn(UIContext.Base));
        Assert.Single(new BaseHudViewModel(FakeGauges(UIContext.Base)).Gauges);
    }

    [Fact]
    public void 资源条数或种类对不上场合时抛()
    {
        // 只核条数不够：「生命、体力、精力」也是三条，界面照画，玩家看到的法力就不见了。
        var full = Fake();
        var wrongKind = full.Gauges.Take(2).Append(new HudGauge(HudGaugeKind.DailyVigor, "精力", 1, 2)).ToList();
        Assert.Throws<ArgumentException>(() => full.WithGauges(wrongKind));
        Assert.Throws<ArgumentException>(() => full.WithGauges([.. full.Gauges, .. FakeGauges(UIContext.Base)]));

        Assert.Throws<ArgumentException>(() => new BaseHudViewModel(FakeGauges(UIContext.Level)));
        Assert.Throws<ArgumentException>(() => new BaseHudViewModel([]));
    }

    [Fact]
    public void 资源上限为零时不除零也不显示成满格()
    {
        var gauge = new HudGauge(HudGaugeKind.Mana, "MP", Current: 0, Max: 0);
        Assert.Equal(0.0, gauge.Ratio);
    }

    [Theory]
    [InlineData(-5, 0.0)]
    [InlineData(0, 0.0)]
    [InlineData(20, 0.5)]
    [InlineData(40, 1.0)]
    [InlineData(99, 1.0)]
    public void 资源比例钳在零到一之间(int current, double expected)
    {
        Assert.Equal(expected, new HudGauge(HudGaugeKind.Health, "HP", current, FakeMax).Ratio);
    }

    [Theory]
    [InlineData(0, 0, true)]        // 总数为 0 也算达成 —— 没有要采的东西就等于不用采
    [InlineData(0, 3, false)]
    [InlineData(3, 3, true)]
    [InlineData(4, 3, true)]
    public void 目标进度为零或已达成时都算达成(int done, int total, bool complete)
    {
        var objective = new HudObjective("素材", done, total, "已达成");
        Assert.Equal(complete, objective.Complete);
        Assert.True(objective.Remaining >= 0);
    }

    [Fact]
    public void 队友为零时该区收起而不是留空槽()
    {
        // 空槽会让「单人采集」看起来像「三个队友没加载出来」。
        Assert.False(Fake(teammates: 0).ShowTeammates);
        Assert.True(Fake(teammates: 1).ShowTeammates);
    }

    [Fact]
    public void 队友超过编队上限时抛()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Fake(teammates: HudLayout.MaxTeammates + 1));
    }

    [Fact]
    public void 资源或技能条数不对时抛而不是静默少画一格()
    {
        var full = Fake();
        Assert.Throws<ArgumentException>(() => full.WithGauges([.. full.Gauges.Skip(1)]));
        Assert.Throws<ArgumentException>(() => full.WithSkills([.. full.Skills.Skip(1)]));
    }

    [Fact]
    public void 换一块不影响其余几块()
    {
        var full = Fake();
        var changed = full.WithObjective(new HudObjective("来源点", 0, 2, "已达成"));
        Assert.Equal(full.Gauges, changed.Gauges);
        Assert.Equal(full.Teammates, changed.Teammates);
        Assert.Equal("来源点", changed.Objective.Label);
    }

    [Theory]
    [InlineData(-1.0, 0.0)]
    [InlineData(0.0, 0.0)]
    [InlineData(0.4, 0.4)]
    [InlineData(2.0, 1.0)]
    public void 冷却遮罩比例钳在零到一之间(double remaining, double expected)
    {
        var slot = new HudSkillSlot(InputActions.Skills[0], "标记", true, remaining);
        Assert.Equal(expected, slot.MaskRatio);
    }

    [Fact]
    public void 冷却中的技能位不算可放()
    {
        var action = InputActions.Skills[0];
        Assert.True(new HudSkillSlot(action, "标记", true, 0.0).Ready);
        Assert.False(new HudSkillSlot(action, "标记", true, 0.5).Ready);
        Assert.False(new HudSkillSlot(action, "", false, 0.0).Ready);    // 没解锁也不算可放
    }

    // ── 按键提示 ────────────────────────────────────────────────────

    [Fact]
    public void 键鼠的技能提示取该技能位自己的按键绑定()
    {
        for (var i = 0; i < InputActions.Skills.Count; i++)
        {
            var action = InputActions.Skills[i];
            var bound = InputBindings.For(action, InputDeviceKind.KeyboardMouse)[0].Symbol;
            Assert.Equal(InputHints.LabelOf(bound),
                         InputHints.SkillLabel(action, InputDeviceKind.KeyboardMouse));
        }
    }

    [Fact]
    public void 手柄的技能提示取它对应的那个面键()
    {
        // 技能位在手柄上没有自己的绑定（由修饰键组合解算发出，绑定表里登记了豁免理由），
        // 所以提示必须从面键推。写死一份「技能 1 = X 键」的表就会在改键那天教错玩家。
        for (var i = 0; i < InputActions.Skills.Count; i++)
        {
            var face = InputActions.ShadowedByModifier[i % InputActions.SkillsPerGroup];
            var bound = InputBindings.For(face, InputDeviceKind.Gamepad)[0].Symbol;
            Assert.Equal(InputHints.LabelOf(bound),
                         InputHints.SkillLabel(InputActions.Skills[i], InputDeviceKind.Gamepad));
        }
    }

    [Fact]
    public void 技能位前三个归左扳机后三个归右扳机()
    {
        for (var i = 0; i < InputActions.Skills.Count; i++)
        {
            var expected = i < InputActions.SkillsPerGroup ? SkillGroup.Left : SkillGroup.Right;
            Assert.Equal(expected, InputHints.GroupOfSkill(InputActions.Skills[i]));
        }

        Assert.Equal(InputHints.None, InputHints.GroupLabel(SkillGroup.None));
    }

    [Fact]
    public void 手柄提示是修饰键加面键两个记号()
    {
        var combo = InputHints.SkillCombo(InputActions.Skills[0], InputDeviceKind.Gamepad);
        Assert.Equal(InputHints.GroupLabel(SkillGroup.Left)
                     + InputHints.SkillLabel(InputActions.Skills[0], InputDeviceKind.Gamepad),
                     combo);

        // 键鼠上没有修饰键，所以就是那一个技能键（第 1 个技能位在 H 上）。
        Assert.Equal("H", InputHints.SkillCombo(InputActions.Skills[0],
                                                InputDeviceKind.KeyboardMouse));
    }

    [Fact]
    public void 面键记号用方位而不是字母()
    {
        // 任天堂手柄的 A 与 B 位置与 Xbox 相反，按字母写迟早在某个手柄上教错。
        Assert.Equal("下", InputHints.LabelOf(InputSymbol.PadFaceBottom));
        Assert.Equal("上", InputHints.LabelOf(InputSymbol.PadFaceTop));
        Assert.Equal("左", InputHints.LabelOf(InputSymbol.PadFaceLeft));
        Assert.Equal("右", InputHints.LabelOf(InputSymbol.PadFaceRight));
    }

    [Fact]
    public void 没有短记号的物理位抛而不是返回空串()
    {
        // 静默返回空会让「忘了写记号」和「这个位故意不显示」长得一模一样。
        Assert.Throws<ArgumentOutOfRangeException>(
            () => InputHints.LabelOf(InputSymbol.KeyW));
    }

    // ── 占位色板 ────────────────────────────────────────────────────

    [Fact]
    public void 四条资源色的明度两两分得开()
    {
        // 色觉差异最常见的是红绿难分，而 HP 是红、精力是绿。明度也一样的话，
        // 对一部分玩家来说这两条就是同一根。
        var lums = Enum.GetValues<HudGaugeKind>()
            .Select(k => HudPalette.LuminanceOf(HudPalette.ColorOf(k)))
            .OrderBy(v => v).ToList();
        for (var i = 1; i < lums.Count; i++)
        {
            Assert.True(lums[i] - lums[i - 1] >= 0.08,
                        $"相邻两档明度只差 {lums[i] - lums[i - 1]:F3}");
        }
    }

    [Fact]
    public void 界面颜色没有透明度这个字段()
    {
        // 半透明遮罩画上去就是屏幕上的插值像素，与半透明素材同一后果
        // （[像素绘制原则 §9] 的绝对规则）。类型里没有那个字段，就没人能顺手加。
        Assert.DoesNotContain(typeof(PixelColor).GetProperties(),
                              p => p.Name is "A" or "Alpha");
        Assert.Equal("#9E3A32", HudPalette.Health.ToString());
    }

    [Fact]
    public void 每条资源都有配色而且缺一条会抛()
    {
        foreach (var kind in Enum.GetValues<HudGaugeKind>())
        {
            Assert.NotEqual(default, HudPalette.ColorOf(kind));
        }

        Assert.Throws<ArgumentOutOfRangeException>(
            () => HudPalette.ColorOf((HudGaugeKind)(-1)));
    }

    // ── 像素字体的期望取值 ──────────────────────────────────────────

    [Fact]
    public void 像素字体的十项取值与ADR0008一致()
    {
        // 这一条把期望值钉住。真正生效的设置在 .ttf.import 里，引擎层读回实际值与本表比对 ——
        // 改设置会让启动时的检查失败，改本表会让这条测试失败。两边都动不了才叫钉住。
        Assert.Equal(FontAntialiasing.None, PixelFont.Antialiasing);
        Assert.Equal(FontHinting.None, PixelFont.Hinting);
        Assert.Equal(FontSubpixelPositioning.Disabled, PixelFont.SubpixelPositioning);
        Assert.False(PixelFont.MultichannelSignedDistanceField);
        Assert.False(PixelFont.GenerateMipmaps);
        Assert.False(PixelFont.ForceAutohinter);
        Assert.True(PixelFont.DisableEmbeddedBitmaps);
        Assert.False(PixelFont.KeepRoundingRemainders);
        Assert.Equal(1.0f, PixelFont.Oversampling);
        Assert.False(PixelFont.AllowSystemFallback);     // 有意让缺字显豆腐块
        Assert.Equal(10, PixelFont.PropertyCount);
    }

    [Fact]
    public void 字体文件与许可证都指在同一个目录下()
    {
        // OFL 第 2 条要求每份拷贝都带许可证。放在一起是为了让「漏了它」看得出来，
        // 真正检查导出包里有没有许可证的是 tools/verify.py 的解包清单那一步。
        var dir = PixelFont.ResourcePath[..PixelFont.ResourcePath.LastIndexOf('/')];
        Assert.StartsWith(dir, PixelFont.LicensePath);
        Assert.EndsWith(".ttf", PixelFont.ResourcePath);
    }

    [Fact]
    public void 字体版本与内容都钉死了()
    {
        Assert.Equal("2026.08.11", PixelFont.UpstreamVersion);
        Assert.Equal(64, PixelFont.Sha256.Length);
        Assert.Equal(6995400, PixelFont.FileBytes);
        // 没做子集化也没改字形，所以沿用保留字体名是合规的（OFL 第 3 条）。
        Assert.Contains("Fusion Pixel", PixelFont.ReservedFamilyName);
    }
}
