namespace Tinderhearth.Rules.Ui;

/// <summary>HUD 的四块内容。**一块一个锚点**，块内靠容器排。</summary>
public enum HudBlock
{
    /// <summary>目标进度。正典要求**始终可见**，为 0 或已达成时也不隐藏。</summary>
    Objective,

    /// <summary>主角资源：HP／SP／MP 与体力。</summary>
    Resources,

    /// <summary>6 个技能位，含冷却表现与当前修饰键组的提示。</summary>
    Skills,

    /// <summary>队友状态，容纳编队上限 4 名学员；为 0 时整块收起。</summary>
    Teammates,
}

/// <summary>
/// 一块贴在屏幕的哪个角。**这就是引擎层用的东西** —— 它翻译成锚点预设加安全边距偏移。
/// </summary>
/// <remarks>
/// 刻意只有「贴哪个角」这么粗的粒度，没有坐标：`aspect="expand"` 下逻辑宽度是变量
/// （`UI-3` 实测 3840×2130 的窗口得到 649×360），任何横向坐标都会在宽窗口上错位。
///
/// **刻意没有「居中」。** 立项时另有一套「底边一条、技能居中」的候选，作者 2026-08-31 选了四角
/// 这套，居中那套连带删掉。留下一条实测事实免得将来有人重新发明它：逻辑宽度为奇数时（649 就是），
/// 居中块的横坐标**落在半个逻辑像素上**（当时量到 −35.5）。×2 缩放下它正好是 1 物理像素、没有
/// 后果；×3 下是 1.5 —— 素材边界不再落在物理像素格上。**后半句是推断，没实测**：要验就照
/// `CameraProbe` 的办法，在 ×3 窗口下给居中块截图量最小同色跑长。所以将来真要用居中，
/// 先补那一次测量，再决定要不要显式取整。
/// </remarks>
public enum HudAnchor
{
    /// <summary>左上角。</summary>
    TopLeft,

    /// <summary>右上角。</summary>
    TopRight,

    /// <summary>左下角。</summary>
    BottomLeft,

    /// <summary>右下角。</summary>
    BottomRight,
}

/// <summary>屏幕上的一个整数矩形。**只用来算占屏与可读区**，不用来摆节点。</summary>
public readonly record struct HudRect(int X, int Y, int Width, int Height)
{
    /// <summary>右边界（不含）。</summary>
    public int Right => X + Width;

    /// <summary>下边界（不含）。</summary>
    public int Bottom => Y + Height;

    /// <summary>面积。</summary>
    public int Area => Width * Height;

    /// <summary>两个矩形有没有重叠。边贴边不算重叠。</summary>
    public bool Overlaps(HudRect other) =>
        X < other.Right && other.X < Right && Y < other.Bottom && other.Y < Bottom;
}

/// <summary>
/// HUD 的排版尺寸与放置（`UI-8`）。**尺寸全部从 <see cref="UiMetrics"/> 推，不新定一套。**
/// </summary>
/// <remarks>
/// 为什么这些数在规则层：与 <see cref="UiMetrics"/> 同一条理由 —— 它们之间有**可以判死的关系**
/// （块高是行高的整数倍、块不许压到角色所在的可读区、居中偏移是不是整数），放这里就能用单元测试
/// 盯住。更要紧的是引擎层因此**一个数字字面量都不需要**，于是「HUD 里没有写死的数」变成可以
/// 机器扫的规则（`tools/check_hud.py`），而不是一句自觉。
///
/// **这里没有任何玩法数值。** HP 上限、冷却时长归 `design/数值模型.md`，由视图模型传入
/// （<see cref="HudViewModel"/>）。本文件里的数全是**排版量**：几个格子、多宽、贴哪条边。
///
/// **块尺寸是定值而不是随内容伸缩**，这是有意的：定值让 <see cref="RectOf"/> 的预测等于实际，
/// 于是占屏比例与可读区算得出来、也测得出来。内容比框长时截断（目标进度那一行按最长文案定宽）。
/// </remarks>
public static class HudLayout
{
    /// <summary>编队上限。正典：常规出征编队是主角 + 1 至 4 名学员。</summary>
    public const int MaxTeammates = 4;

    /// <summary>资源条的条数：HP、SP、MP、体力。正典那张资源表三条，加日体力一条。</summary>
    public const int GaugeCount = 4;

    /// <summary>
    /// 资源条标签占几个全宽汉字。取 2 —— 最长的标签是「体力」。
    /// </summary>
    /// <remarks>
    /// HP／SP／MP 刻意沿用正典的缩写而不是另起中文名：给资源起中文名是文案的事（`DOC-2` 与
    /// 叙事侧），本条不顺手定。西文在这款字体里宽 8px，两个字母 16px，比「体力」的 24px 窄，
    /// 所以标签列按汉字算就够宽。
    /// </remarks>
    public const int GaugeLabelChars = 2;

    /// <summary>资源条的填充部分宽几个基础单位。</summary>
    /// <remarks>
    /// 取 4 个基础单位（64px）。下限受可读性管着：条太短时一格像素代表的量太大，玩家看不出变化；
    /// 上限受占屏管着 —— 资源块总宽 92px 已占逻辑宽度的 14%，再宽就开始挤中间那块可读区。
    /// </remarks>
    public const int GaugeBarUnits = 4;

    /// <summary>资源条高 6px。**这是素材 `bar-track.png` 的高**（登记表 4×6），不是随手取的。</summary>
    public const int GaugeBarHeight = 6;

    /// <summary>队友头像框边长 20px。**素材 `portrait-frame.png` 的尺寸**（16px 头像 + 2px 框）。</summary>
    public const int PortraitSize = 20;

    /// <summary>
    /// 目标进度那一行最长几个全宽汉字。取 11 ——「已达成，返回入口点撤离」。
    /// </summary>
    /// <remarks>
    /// 按**最长的那句**定宽，而不是按常态的「素材 3／5」。理由是达成态那句更长，按短的定宽会让
    /// 它在最需要看清的时刻被截断，而正典要求进度始终可见。
    /// </remarks>
    public const int ObjectiveMaxChars = 11;

    /// <summary>一组修饰键覆盖几个技能位，与 <see cref="InputActions.SkillsPerGroup"/> 同源。</summary>
    public static int SkillsPerGroup => InputActions.SkillsPerGroup;

    /// <summary>技能位分几组。6 个位 ÷ 每组 3 个 ＝ 2 组，正好对上两个扳机。</summary>
    public static int SkillGroupCount => InputActions.Skills.Count / SkillsPerGroup;

    /// <summary>资源条标签列宽。</summary>
    public static int GaugeLabelWidth => GaugeLabelChars * UiMetrics.FontSize;

    /// <summary>资源条填充部分的宽。</summary>
    public static int GaugeBarWidth => GaugeBarUnits * UiMetrics.BaseUnit;

    /// <summary>一条资源占的高。取行高，好让标签与条在同一条基线节奏上。</summary>
    public static int GaugeRowHeight => UiMetrics.LineHeight;

    /// <summary>一个技能位单元的高：手柄按键提示叠在图标内，不另占一行。</summary>
    public static int SkillCellHeight => UiMetrics.IconSmall;

    /// <summary>
    /// 一组技能位横排占的宽：三个紧邻的图标格。
    /// </summary>
    /// <remarks>
    /// 六个位**一行横排**由作者 2026-08-31 定（此前是两行三列）。演进有据可查：两行三列 80×76 →
    /// 横排带数字与「L」「R」记号列 160×40 → 作者实机后去掉键鼠数字 136×40 → 再去掉两组的「L」「R」
    /// 记号列、136×24 → **作者 2026-08-31 再看仍嫌 L／R 那两列空，去掉记号列后收到 112×24**。
    ///
    /// 记号列为什么能去：那两个 12px 的列在键鼠下永远是空的（键鼠没有扳机这一层），作者盯着的正是
    /// 这块空白。手柄要辨「哪三个归哪个扳机」不再靠这一列，而靠三样：**两组之间那一个栅格的间隔**
    /// （看得出是 3+3）、**按住扳机时那一组三格连着高亮**（`FR-17`，立即确认）、以及**每个图标内叠的
    /// 面键记号**。左组＝左扳机、右组＝右扳机是位置约定，按住任一扳机时的高亮会当场教会玩家。
    /// 手柄呈现效果由脚手架 `G` 键（不接手柄也能预览）人工验收，见 `UI-8` 待确认。
    /// </remarks>
    public static int SkillGroupWidth => SkillsPerGroup * UiMetrics.IconSmall;

    /// <summary>
    /// 取一块的**内容区**尺寸（不含面板内边距）。
    /// </summary>
    /// <remarks>
    /// 内容区与外框分开，是因为每块都坐在一张面板底上：面板给 1px 描边加一圈内边距，好让
    /// 12px 文字压在杂乱的关卡背景上仍读得清 —— 而「读不读得清」正是本条要作者实机判的那件事。
    /// 两个尺寸分开算，占屏比例才不会把内边距漏掉。
    /// </remarks>
    public static HudRect ContentSizeOf(HudBlock block) => block switch
    {
        HudBlock.Objective => new HudRect(
            X: 0, Y: 0,
            Width: UiMetrics.IconSmall + UiMetrics.ItemGap
                   + (ObjectiveMaxChars * UiMetrics.FontSize),
            Height: UiMetrics.LineHeight),

        HudBlock.Resources => new HudRect(
            X: 0, Y: 0,
            Width: GaugeLabelWidth + UiMetrics.ItemGap + GaugeBarWidth,
            Height: GaugeCount * GaugeRowHeight),

        // 六个位一行横排，两组之间空一个栅格。每组前面留一个记号列（手柄上是「L」「R」两个扳机）。
        HudBlock.Skills => new HudRect(
            X: 0, Y: 0,
            Width: (SkillGroupCount * SkillGroupWidth)
                   + ((SkillGroupCount - 1) * UiMetrics.Grid),
            Height: SkillCellHeight),

        HudBlock.Teammates => new HudRect(
            X: 0, Y: 0,
            Width: (MaxTeammates * PortraitSize) + ((MaxTeammates - 1) * UiMetrics.ItemGap),
            Height: PortraitSize + GaugeBarHeight),

        _ => throw new ArgumentOutOfRangeException(nameof(block), $"没有这一块：{block}"),
    };

    /// <summary>取一块的**外框**尺寸：内容区加两边内边距。这是它在屏幕上真正占的地方。</summary>
    public static HudRect SizeOf(HudBlock block)
    {
        var content = ContentSizeOf(block);
        var both = UiMetrics.PanelPadding * 2;
        return new HudRect(0, 0, content.Width + both, content.Height + both);
    }

    /// <summary>
    /// 这一块贴哪个角。**引擎层只用这个**，不用坐标。
    /// </summary>
    /// <remarks>
    /// **四角贴边**由作者 2026-08-31 从两套候选里选定（另一套是「底边一条、技能居中」）。
    /// 四角这套的两条好处是算得出来的：贯通两端的可读横带 640×238，而且**一个居中锚点都不用**，
    /// 于是任何逻辑宽度下四块都落在整数像素上（见 <see cref="HudAnchor"/>）。
    /// </remarks>
    public static HudAnchor AnchorOf(HudBlock block) => block switch
    {
        // 目标进度在左上：它是唯一「一直要能扫到」的东西，而左上是阅读起点。
        HudBlock.Objective => HudAnchor.TopLeft,

        // 队友在右上：与目标进度同属「偶尔扫一眼」，摆在上边一行两端。
        HudBlock.Teammates => HudAnchor.TopRight,

        // 资源在左下：手放在键盘左手区，眼睛往左下扫最短。
        HudBlock.Resources => HudAnchor.BottomLeft,

        // 技能位在右下：与资源同在底边，两者构成「我还有多少／我能放什么」这一对。
        HudBlock.Skills => HudAnchor.BottomRight,

        _ => throw new ArgumentOutOfRangeException(nameof(block), $"没有这一块：{block}"),
    };

    /// <summary>
    /// 算一块在给定视口下占哪个矩形。**只是量具** —— 节点位置由锚点决定，不由这里设。
    /// </summary>
    /// <remarks>
    /// 它与引擎层是两条独立的路径：这里按锚点与安全边距算出**应该**在哪，引擎层按锚点摆，
    /// 然后 <c>HudProbe</c> 把节点的实际屏幕矩形读回来与这里逐块比对。对不上就说明锚点摆错了，
    /// 而那种错在窄窗口上看不出来 —— 正是需要机器判的形状。
    /// </remarks>
    public static HudRect RectOf(HudBlock block, int viewportWidth, int viewportHeight)
    {
        var size = SizeOf(block);
        var margin = UiMetrics.SafeMargin;
        var (x, y) = AnchorOf(block) switch
        {
            HudAnchor.TopLeft => (margin, margin),
            HudAnchor.TopRight => (viewportWidth - margin - size.Width, margin),
            HudAnchor.BottomLeft => (margin, viewportHeight - margin - size.Height),
            HudAnchor.BottomRight => (viewportWidth - margin - size.Width,
                                      viewportHeight - margin - size.Height),
            _ => throw new ArgumentOutOfRangeException(nameof(block)),
        };
        return new HudRect(x, y, size.Width, size.Height);
    }

    /// <summary>四块的矩形。</summary>
    public static IReadOnlyList<(HudBlock Block, HudRect Rect)> RectsOf(
        int viewportWidth, int viewportHeight) =>
        [.. Enum.GetValues<HudBlock>()
                .Select(b => (b, RectOf(b, viewportWidth, viewportHeight)))];

    /// <summary>四块合起来占视口的几成。队友区收起时不算它 —— 那时它真的不占地方。</summary>
    public static double CoverageRatio(int viewportWidth, int viewportHeight) =>
        (double)RectsOf(viewportWidth, viewportHeight).Sum(r => r.Rect.Area)
        / (viewportWidth * viewportHeight);

    /// <summary>
    /// 主角**可能出现的那块**。HUD 压到它就是压掉了该看的东西。
    /// </summary>
    /// <remarks>
    /// 它不是「角色现在在哪」，而是「相机死区允许它跑到哪」：相机把角色留在死区内不动镜头，
    /// 所以角色相对屏幕中心的偏移上限就是死区半宽高，再各向外扩一个精灵格（角色本体占的地方）。
    /// 死区那两个数本来就是**屏幕像素**（见 <see cref="CameraFeel"/>），所以这里不必换算缩放；
    /// 精灵格要换算 —— 32 世界像素在侧视 2 倍下占屏 64。
    /// </remarks>
    public static HudRect ActorBand(int viewportWidth, int viewportHeight)
    {
        var halfW = CameraFeel.DeadzoneHalfWidthScreenPx
                    + (UiMetrics.IconLarge * UiMetrics.SideViewZoom / 2);
        var halfH = CameraFeel.DeadzoneHalfHeightScreenPx
                    + (UiMetrics.IconLarge * UiMetrics.SideViewZoom / 2);
        return new HudRect((viewportWidth / 2) - halfW, (viewportHeight / 2) - halfH,
                           halfW * 2, halfH * 2);
    }

    /// <summary>有没有哪一块压到了 <see cref="ActorBand"/>。**这是本条的硬判据。**</summary>
    public static IReadOnlyList<HudBlock> BlocksOverActorBand(int viewportWidth, int viewportHeight)
    {
        var band = ActorBand(viewportWidth, viewportHeight);
        return [.. RectsOf(viewportWidth, viewportHeight)
                   .Where(r => r.Rect.Overlaps(band)).Select(r => r.Block)];
    }

    /// <summary>
    /// 含屏幕中心、**整幅宽**、且一块 HUD 都不压的最高那条横带。选放置方案时比的就是这个数。
    /// </summary>
    /// <remarks>
    /// 为什么用「整幅宽的横带」而不是「最大空白面积」：侧视关卡里玩家要读的是一条水平走廊，
    /// 左右两端的敌人和平台跟正中间一样要紧。面积大但被切成两块的空白，读起来不如一条通的横带。
    /// 有块横跨中心线时返回高度 0 —— 那说明这套方案把可读区切断了。
    /// </remarks>
    public static HudRect ClearBand(int viewportWidth, int viewportHeight)
    {
        var centerY = viewportHeight / 2;
        var top = 0;
        var bottom = viewportHeight;
        foreach (var (_, rect) in RectsOf(viewportWidth, viewportHeight))
        {
            if (rect.Bottom <= centerY)
            {
                top = Math.Max(top, rect.Bottom);
            }
            else if (rect.Y >= centerY)
            {
                bottom = Math.Min(bottom, rect.Y);
            }
            else
            {
                return new HudRect(0, centerY, viewportWidth, 0);   // 横跨中心线，可读区被切断
            }
        }

        return new HudRect(0, top, viewportWidth, bottom - top);
    }
}
