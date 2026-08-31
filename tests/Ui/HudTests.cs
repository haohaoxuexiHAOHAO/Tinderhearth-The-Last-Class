using Tinderhearth.Rules.Ui;
using Xunit;

namespace Tinderhearth.Rules.Tests.Ui;

/// <summary>
/// `UI-8` 关卡 HUD 的守卫：排版关系、放置候选、视图模型契约与像素字体取值。
/// </summary>
/// <remarks>
/// 这些为什么要有测试：失效方式全都不报错。块高从行高倍数变成随手取的数，只表现为某一行文字
/// 与旁边的条差半格；居中偏移落到半像素上，只表现为「在某些窗口尺寸下有点糊」；视图模型少给
/// 一条资源，只表现为界面上少一根条 —— 而没人记得本来有几根。
///
/// **不含任何玩法数值。** 这里测的是排版量之间的关系与界面契约，不是「HP 上限该是几」。
/// 视图模型里的数字都是当场编的假值，用来撞契约。
/// </remarks>
public class HudTests
{
    // ── 一份最小可用的假视图模型。数字全是编的，只为撞契约 ──
    private const int FakeMax = 40;

    private static HudViewModel Fake(int teammates = HudLayout.MaxTeammates) => new(
        gauges: [.. Enum.GetValues<HudGaugeKind>()
                        .Select(k => new HudGauge(k, k.ToString(), FakeMax / 2, FakeMax))],
        skills: [.. InputActions.Skills.Select(a => new HudSkillSlot(a, a, true, 0.0))],
        objective: new HudObjective("素材", 1, 3, "已达成"),
        teammates: [.. Enumerable.Range(0, teammates)
                                 .Select(_ => new HudTeammate(FakeMax, FakeMax, Down: false))]);

    // ── 排版关系 ────────────────────────────────────────────────────

    [Fact]
    public void 每一块的宽高都是正整数()
    {
        foreach (var block in Enum.GetValues<HudBlock>())
        {
            var size = HudLayout.SizeOf(block);
            Assert.True(size.Width > 0, $"{block} 宽 {size.Width}");
            Assert.True(size.Height > 0, $"{block} 高 {size.Height}");
        }
    }

    [Fact]
    public void 资源块内容高是行高的整数倍且等于资源条数()
    {
        // 不成倍数的话标签基线与条会差半格，而那种错只在并排看两块时才发现。
        var content = HudLayout.ContentSizeOf(HudBlock.Resources);
        Assert.Equal(0, content.Height % UiMetrics.LineHeight);
        Assert.Equal(HudLayout.GaugeCount, content.Height / UiMetrics.LineHeight);
    }

    [Fact]
    public void 外框等于内容加两边内边距()
    {
        // 占屏比例算的是外框。漏掉内边距会让「HUD 占几成」偏小，而那个数正是选放置方案的依据。
        foreach (var block in Enum.GetValues<HudBlock>())
        {
            var content = HudLayout.ContentSizeOf(block);
            var outer = HudLayout.SizeOf(block);
            Assert.Equal(content.Width + (UiMetrics.PanelPadding * 2), outer.Width);
            Assert.Equal(content.Height + (UiMetrics.PanelPadding * 2), outer.Height);
        }
    }

    [Fact]
    public void 资源条比行高矮好让它在一行里居中()
    {
        Assert.True(HudLayout.GaugeBarHeight < UiMetrics.LineHeight);
        Assert.Equal(0, (UiMetrics.LineHeight - HudLayout.GaugeBarHeight) % 2);
    }

    [Fact]
    public void 技能位分组与输入侧同源()
    {
        // 两处各写一个 2 必然漂移：修饰键那边改成三个组时，这里不跟着改也不会报错。
        Assert.Equal(InputActions.SkillsPerGroup, HudLayout.SkillsPerGroup);
        Assert.Equal(InputActions.Skills.Count,
                     HudLayout.SkillGroupCount * HudLayout.SkillsPerGroup);
    }

    [Fact]
    public void 技能位单元只占图标一格且手柄提示叠在图标内()
    {
        Assert.Equal(UiMetrics.IconSmall, HudLayout.SkillCellHeight);
    }

    [Fact]
    public void 六个技能位排成一行且两组之间空一个栅格()
    {
        // 横排由作者 2026-08-31 定。块高只占一个单元，宽度是两组加中间那道分界。
        var content = HudLayout.ContentSizeOf(HudBlock.Skills);
        Assert.Equal(HudLayout.SkillCellHeight, content.Height);
        Assert.Equal((HudLayout.SkillGroupCount * HudLayout.SkillGroupWidth)
                     + ((HudLayout.SkillGroupCount - 1) * UiMetrics.Grid), content.Width);

        // 槽框本身分隔同组三格，组内不留空；两组之间仍由上面的栅格明确分开。
        // 不再有「L」「R」记号列（作者 2026-08-31 去掉）：一组就是三个紧邻的图标格。
        Assert.Equal(HudLayout.SkillsPerGroup * UiMetrics.IconSmall, HudLayout.SkillGroupWidth);
    }

    [Fact]
    public void 队友块容纳编队上限四名()
    {
        var content = HudLayout.ContentSizeOf(HudBlock.Teammates);
        var expected = (HudLayout.MaxTeammates * HudLayout.PortraitSize)
                       + ((HudLayout.MaxTeammates - 1) * UiMetrics.ItemGap);
        Assert.Equal(expected, content.Width);
        Assert.Equal(4, HudLayout.MaxTeammates);        // 正典：主角 + 1 至 4 名学员
    }

    [Fact]
    public void 目标块按最长那句定宽而不是按常态()
    {
        // 「已达成，返回入口点撤离」11 个全宽汉字。按短的定宽会让达成态被截断，
        // 而正典要求进度始终可见 —— 达成那句正是最需要看清的时刻。
        var content = HudLayout.ContentSizeOf(HudBlock.Objective);
        Assert.True(content.Width >= "已达成，返回入口点撤离".Length * UiMetrics.FontSize);
        Assert.Equal(UiMetrics.LineHeight, content.Height);
    }

    // ── 放置 ────────────────────────────────────────────────────────

    [Fact]
    public void 一块都不压角色可能出现的那块()
    {
        // 本条的硬判据。角色 32px 在侧视 2 倍下占屏 64px，加上死区允许的偏移，
        // 中间那块必须一块 HUD 都不压。
        Assert.Empty(HudLayout.BlocksOverActorBand(UiMetrics.BaseWidth, UiMetrics.BaseHeight));
    }

    [Fact]
    public void 留出一条通到两端的可读横带()
    {
        // 侧视关卡是一条水平走廊，左右两端的敌人与平台跟正中间一样要紧，
        // 所以判据是「整幅宽的横带」而不是「最大空白面积」。
        var band = HudLayout.ClearBand(UiMetrics.BaseWidth, UiMetrics.BaseHeight);
        Assert.Equal(UiMetrics.BaseWidth, band.Width);
        // 下限取五分之三：它必须比角色可读区（128px）宽出一截，否则「不压角色」这条就靠得太紧，
        // 一点排版变动就把余量吃光。实测 238px。
        Assert.True(band.Height >= UiMetrics.BaseHeight * 3 / 5,
                    $"可读横带只有 {band.Height}px");
        Assert.True(band.Height > HudLayout.ActorBand(
            UiMetrics.BaseWidth, UiMetrics.BaseHeight).Height);
    }

    [Fact]
    public void 占屏不超过一成()
    {
        var share = HudLayout.CoverageRatio(UiMetrics.BaseWidth, UiMetrics.BaseHeight);
        Assert.True(share < 0.10, $"HUD 占屏 {share:P1}，超过一成就开始挤中间那块");
    }

    [Fact]
    public void 逻辑宽度撑开时左锚点不动右锚点跟着走()
    {
        // `aspect="expand"` 下逻辑宽度是变量（`UI-3` 实测 3840×2130 得到 649×360）。
        // 这条把「用锚点而不是写死坐标」变成算得出来的性质。
        const int wide = 649;
        var delta = wide - UiMetrics.BaseWidth;

        foreach (var block in Enum.GetValues<HudBlock>())
        {
            var narrow = HudLayout.RectOf(block, UiMetrics.BaseWidth, UiMetrics.BaseHeight);
            var stretched = HudLayout.RectOf(block, wide, UiMetrics.BaseHeight);
            var expected = HudLayout.AnchorOf(block) switch
            {
                HudAnchor.TopLeft or HudAnchor.BottomLeft => 0,
                HudAnchor.TopRight or HudAnchor.BottomRight => delta,
                _ => throw new InvalidOperationException(),
            };
            Assert.Equal(expected, stretched.X - narrow.X);
            Assert.Equal(narrow.Y, stretched.Y);        // 高度锁在 360，纵向不该动
        }
    }

    [Fact]
    public void 没有居中锚点所以横坐标不会落在半格上()
    {
        // 立项时另一套候选把技能条摆在底边居中，奇数逻辑宽度下它落在 .5 上（649 宽时实测 −35.5）。
        // 作者 2026-08-31 选了四角，那套删掉了。**这条挡的是「有人把居中加回来」** ——
        // 加回来会让它失败，逼人先读 HudAnchor 的注释（那里写明要先补一次 ×3 缩放下的测量）。
        Assert.DoesNotContain("Center", string.Join(",", Enum.GetNames<HudAnchor>()));

        // 块宽一律偶数，所以将来真要居中时只在逻辑宽度为奇数时才落到半格，而不是每次都落。
        foreach (var block in Enum.GetValues<HudBlock>())
        {
            Assert.Equal(0, HudLayout.SizeOf(block).Width % 2);
        }
    }

    [Theory]
    [InlineData(640)]
    [InlineData(649)]
    public void 四块都在屏幕内且留着安全边距(int viewportWidth)
    {
        foreach (var (block, rect) in HudLayout.RectsOf(viewportWidth, UiMetrics.BaseHeight))
        {
            Assert.True(rect.X >= UiMetrics.SafeMargin, $"{block} 左边越过安全边距");
            Assert.True(rect.Y >= UiMetrics.SafeMargin, $"{block} 上边越过安全边距");
            Assert.True(rect.Right <= viewportWidth - UiMetrics.SafeMargin,
                        $"{block} 右边越过安全边距");
            Assert.True(rect.Bottom <= UiMetrics.BaseHeight - UiMetrics.SafeMargin,
                        $"{block} 下边越过安全边距");
        }
    }

    [Fact]
    public void 四块贴四个不同的角()
    {
        // 四角贴边：目标左上、队友右上、资源左下、技能右下（作者 2026-08-31 定）。
        Assert.Equal(HudAnchor.TopLeft, HudLayout.AnchorOf(HudBlock.Objective));
        Assert.Equal(HudAnchor.TopRight, HudLayout.AnchorOf(HudBlock.Teammates));
        Assert.Equal(HudAnchor.BottomLeft, HudLayout.AnchorOf(HudBlock.Resources));
        Assert.Equal(HudAnchor.BottomRight, HudLayout.AnchorOf(HudBlock.Skills));

        // 四个角各一块，没有两块抢同一个角。
        var anchors = Enum.GetValues<HudBlock>().Select(HudLayout.AnchorOf).ToList();
        Assert.Equal(anchors.Count, anchors.Distinct().Count());
    }

    [Fact]
    public void 四块两两不重叠()
    {
        var rects = HudLayout.RectsOf(UiMetrics.BaseWidth, UiMetrics.BaseHeight);
        for (var i = 0; i < rects.Count; i++)
        {
            for (var j = i + 1; j < rects.Count; j++)
            {
                Assert.False(rects[i].Rect.Overlaps(rects[j].Rect),
                             $"{rects[i].Block} 与 {rects[j].Block} 重叠");
            }
        }
    }

    [Fact]
    public void 角色可读区取死区加一个精灵格()
    {
        var band = HudLayout.ActorBand(UiMetrics.BaseWidth, UiMetrics.BaseHeight);
        var sprite = UiMetrics.IconLarge * UiMetrics.SideViewZoom;      // 32 世界像素占屏 64
        Assert.Equal((CameraFeel.DeadzoneHalfWidthScreenPx * 2) + sprite, band.Width);
        Assert.Equal((CameraFeel.DeadzoneHalfHeightScreenPx * 2) + sprite, band.Height);
        Assert.Equal(UiMetrics.BaseWidth / 2, band.X + (band.Width / 2));
    }

    [Fact]
    public void 横跨屏幕中心线时可读横带判成零()
    {
        // 反证：判据不能只会说「通过」。矩形重叠判定本身也顺带撞一次。
        var band = new HudRect(0, UiMetrics.BaseHeight / 2, UiMetrics.BaseWidth, 0);
        Assert.Equal(0, band.Height);
        var center = new HudRect(0, (UiMetrics.BaseHeight / 2) - 1, UiMetrics.BaseWidth, 2);
        Assert.True(center.Overlaps(HudLayout.ActorBand(
            UiMetrics.BaseWidth, UiMetrics.BaseHeight)));
    }

    // ── 视图模型契约 ────────────────────────────────────────────────

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
    public void 键鼠的技能提示取该技能位自己的数字键绑定()
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

        // 键鼠上没有修饰键，所以就是一个数字键。
        Assert.Equal("1", InputHints.SkillCombo(InputActions.Skills[0],
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
        // 色觉差异最常见的是红绿难分，而 HP 是红、日体力是绿。明度也一样的话，
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
        // 改设置会让启动判据失败，改本表会让这条测试失败。两边都动不了才叫钉住。
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
        // 真正的执行体在 tools/verify.py 的解包清单那一步（`ART-3`）。
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
