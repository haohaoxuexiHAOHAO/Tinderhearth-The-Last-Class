namespace Tinderhearth.Rules.UI;

/// <summary>HUD 的几块内容。一块一个放置，块内靠容器排。哪个场合有哪几块见 <see cref="HudLayout.BlocksIn"/>。</summary>
public enum HudBlock
{
    /// <summary>目标进度，只在关卡里有。它要始终可见，进度为 0 或已达成时也不隐藏。</summary>
    Objective,

    /// <summary>主角资源加状态位。关卡里是生命、体力与法力，下面一行状态位；基地里是竖着的一支精力，右边两行两列状态位。</summary>
    Resources,

    /// <summary>
    /// 那条横排格子：关卡里装技能位，基地里装随身栏。两边是同一条画好的栏（<c>hud/actionbar_frame</c>），
    /// 同宽、格位落在同一批横坐标上，换场景时眼睛不用重新找。
    /// </summary>
    /// <remarks>
    /// 两种内容的格数同源（<see cref="CarrySlotBar.SlotCount"/> 取的就是 <see cref="InputActions.Skills"/>
    /// 的条数），所以「同宽」由算式保证，不是两处各写一个常量再指望它们一直相等。
    /// </remarks>
    ActionBar,

    /// <summary>队友状态，只在关卡里有，最多容纳编队上限那么多名学生；一个都没有时整块收起。</summary>
    Teammates,
}

/// <summary>一块贴在屏幕的哪个角。引擎层用的就是这个，它翻译成锚点预设加偏移。</summary>
/// <remarks>
/// 粒度只到「贴哪个角」、没有绝对坐标，因为视口按 expand 拉伸时逻辑宽度是个变量（实测
/// 3840×2130 的窗口得到的逻辑尺寸是 649×360），任何横向坐标都会在宽窗口上错位。
///
/// 这里没有「居中」这个取值。留一条实测事实免得将来有人重新发明它：逻辑宽度为奇数时，居中块的
/// 横坐标会落在半个逻辑像素上。两倍缩放下那正好是一个物理像素、没有后果；三倍下是一个半，素材
/// 边界就不再落在物理像素格上。后半句是推断、没实测，验法是在三倍窗口下看居中块的边缘有没有糊掉半格。
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

/// <summary>屏幕上的一个整数矩形。只用来算占屏、可读区与纵向账，不用来摆节点。</summary>
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

/// <summary>一块贴哪个角、离那个角的两条边各多远（已含安全边距）。引擎层照它摆，不另算一遍。</summary>
/// <param name="Anchor">贴哪个角。</param>
/// <param name="InsetX">离那一侧竖边多远：贴左的从左边量，贴右的从右边量。</param>
/// <param name="InsetY">离那一侧横边多远：贴上的从上边量，贴下的从下边量。</param>
public readonly record struct HudPlacement(HudAnchor Anchor, int InsetX, int InsetY);

/// <summary>HUD 的排版尺寸与放置，按场合分两套。尺寸从 <see cref="UIMetrics"/> 与画好的件推，不另定一套。</summary>
/// <remarks>
/// 两套是因为两种视角对 HUD 的要求不一样（设计仓 design/界面系统.md 的「常驻那一层：战斗与经营的
/// 抬头显示」）。**关卡**：战斗片段就是一屏、人能站到任何横坐标上，而纵向账只把顶上那一段留给背景与
/// HUD，所以关卡那一套全部收在顶上（<see cref="LevelTopBandHeight"/>）。**基地**：俯视、相机跟着人走，
/// 照旧贴四角，判据是跟随死区那块可读区（<see cref="ActorBand"/>）。
///
/// 这些数放在规则层，是因为它们之间有判得出来的关系：块不许越过顶上那一段、不许压到可读区、两两
/// 不重叠、块宽是偶数。放这里就能用单元测试盯住，引擎层也因此一个数字字面量都不需要。
///
/// 这里没有任何玩法数值。生命上限、冷却时长归设计仓 design/数值模型.md，由视图模型传入。
///
/// 块尺寸是定值而不是随内容伸缩。定值让 <see cref="RectOf"/> 算出来的等于实际摆出来的，于是占屏、
/// 可读区与纵向账都算得出来、也测得出来。内容比框长时截断，目标进度那一行因此按最长文案定宽。
/// </remarks>
public static class HudLayout
{
    /// <summary>编队上限：常规出征是主角带这么多名学生。</summary>
    public const int MaxTeammates = 4;

    /// <summary>关卡里留给背景与 HUD 的顶上那一段有多高，界面像素。关卡那一套每一块的底边都不许越过它。</summary>
    /// <remarks>
    /// 出处是设计仓 canon/gameplay/战斗与关卡.md 的「可行走纵深是怎么算出来的」那笔纵向账：下面 24 的
    /// 落脚余量、240 的角色带、上面这一段，加起来正好是逻辑分辨率的高度。那张表一换，这个数要回去
    /// 重抄；纵深带改宽改窄时的复核记在设计仓台账 <c>ENG-24</c> 那一条里。
    /// </remarks>
    public const int LevelTopBandHeight = 96;

    /// <summary>资源条的槽高几像素。这是画好的那张 <c>hud/bar_track</c> 的高，有测试拿素材清单对它。</summary>
    public const int GaugeBarHeight = 10;

    /// <summary>资源条的填充放在槽里这一点上，横竖各这么多。照画好的那张 <c>hud/bar_track</c>。</summary>
    public const int GaugeFillInset = 2;

    /// <summary>资源条的填充部分宽几个基础单位。</summary>
    /// <remarks>
    /// 下限受可读性管着：条太短时一格像素代表的量太大，玩家看不出变化。上限受占屏管着。
    /// </remarks>
    public const int GaugeBarUnits = 4;

    /// <summary>队友头像框边长。这是画好的那张 <c>hud/portrait_frame</c> 的尺寸，有测试拿素材清单对它。</summary>
    public const int PortraitSize = 20;

    /// <summary>那条横排格子两头的框边宽：第一格从这么远开始。照画好的那张 <c>hud/actionbar_frame</c>。</summary>
    public const int ActionBarCapX = 7;

    /// <summary>那条横排格子上下的框边高。照画好的那张 <c>hud/actionbar_frame</c>：格子上下各让这么多。</summary>
    public const int ActionBarCapY = 5;

    /// <summary>那条横排格子里格与格之间空几像素。照画好的那张 <c>hud/actionbar_frame</c>（格距 28 减去格宽）。</summary>
    public const int ActionBarCellGap = 6;

    /// <summary>基地那支竖的精力条宽。这是画好的那张 <c>hud/vigor_tube_track</c> 的宽，有测试拿素材清单对它。</summary>
    public const int VigorTubeWidth = 13;

    /// <summary>基地那一块的状态位排几列：放在精力管右边，两列。</summary>
    public const int BaseStatusColumns = 2;

    /// <summary>基地那一块的状态位排几行：两行，与关卡那一行一样能放四个。</summary>
    public const int BaseStatusRows = 2;

    /// <summary>精力条的液面上下各空几行透明：画好的那张 <c>hud/vigor_tube_fill</c> 只在中间那一段有像素（实测）。</summary>
    /// <remarks>
    /// 比例对的是有像素的那一段、不是整张图的高。对整张图的话，精力剩一点时露出来的全是透明行、看起来已经见底，
    /// 快满时又提前顶到管顶。作者重画那张液面要回来对这个数，验法是满精力时液面正好整段露出、精力为零时一行都不露。
    /// </remarks>
    public const int VigorLiquidInset = 2;

    /// <summary>目标进度那一行最长几个全宽汉字，按达成后那句最长的文案定。</summary>
    /// <remarks>
    /// 按最长那句定宽而不是按常态那句，因为达成态那句更长；按短的定宽会让它在最需要看清的时刻
    /// 被截断，而进度要始终可见。
    /// </remarks>
    public const int ObjectiveMaxChars = 11;

    /// <summary>资源条前面那个标签多大：一个迷你图标（<c>hud/gauge_icon_*</c>），不写字。</summary>
    public static int GaugeLabelSize => UIMetrics.Mini;

    /// <summary>资源条填充部分的宽。</summary>
    public static int GaugeBarWidth => GaugeBarUnits * UIMetrics.BaseUnit;

    /// <summary>资源条填充的高：槽高减去上下两段。画好的那几张填充就这么高，有测试拿素材清单对它。</summary>
    public static int GaugeFillHeight => GaugeBarHeight - (GaugeFillInset * 2);

    /// <summary>一根宽 <paramref name="width"/> 的资源条，填充最长画多长：从放填充那一点一直到槽的右边。</summary>
    /// <remarks>
    /// 画到槽的右边而不是槽里那一片的右边：那一片右端是弯的，条头是斜的，按哪一行算都会在别的行上漏一角。
    /// 出界的像素由槽里那一片裁掉，所以满的时候那一片正好铺满。
    /// </remarks>
    public static int GaugeFillMaxLength(int width) => width - GaugeFillInset;

    /// <summary>资源条按这个比例画多长。比例先钳在 0–1，取整只在这里做。</summary>
    public static int GaugeFillLength(double ratio, int width) =>
        (int)Math.Round(Math.Clamp(ratio, 0.0, 1.0) * GaugeFillMaxLength(width));

    /// <summary>一条资源占的高。取行高，好让标签与条在同一条基线节奏上。</summary>
    public static int GaugeRowHeight => UIMetrics.LineHeight;

    /// <summary>状态位那一行的高：一行小图标。这一轮只留位置，里面挂什么跟状态系统走。</summary>
    public static int StatusRowHeight => UIMetrics.LineHeight;

    /// <summary>资源块的内容宽：标签、间距、条。基地那一块照它取，两种场合的资源块一样宽。</summary>
    public static int ResourcesContentWidth => GaugeLabelSize + UIMetrics.ItemGap + GaugeBarWidth;

    /// <summary>那条横排格子有几格。与技能位同一个数，也就是随身栏的格数。</summary>
    public static int ActionBarCellCount => InputActions.Skills.Count;

    /// <summary>随身栏有几格。与技能位同一个数，所以两种内容同宽由算式保证。</summary>
    public static int CarrySlotCount => CarrySlotBar.SlotCount;

    /// <summary>基地那一块两行两列状态位占的宽：每格一个小图标，格与格之间一个组内间距。</summary>
    public static int BaseStatusGridWidth =>
        (BaseStatusColumns * UIMetrics.IconSmall) + ((BaseStatusColumns - 1) * UIMetrics.ItemGap);

    /// <summary>基地那一块两行两列状态位占的高。</summary>
    public static int BaseStatusGridHeight =>
        (BaseStatusRows * UIMetrics.IconSmall) + ((BaseStatusRows - 1) * UIMetrics.ItemGap);

    /// <summary>基地那支竖的精力条拉成多高：与右边那两行状态位一样高。</summary>
    /// <remarks>
    /// 画好的那两张中段按行平铺（空管中间那一段每一行都一样，液面中间那一段首尾接得上），所以多高都画得出来，
    /// 不用照画布的高。平铺那一段在素材资源那一格的九宫格边距里。
    /// </remarks>
    public static int VigorTubeHeight => BaseStatusGridHeight;

    /// <summary>精力条液面那一段有多高：管高减去上下两段透明。</summary>
    public static int VigorLiquidHeight => VigorTubeHeight - (VigorLiquidInset * 2);

    /// <summary>精力条按这个比例该从管子底边往上露出多高。引擎层把液面装进一个这么高、贴着底边的裁剪框。</summary>
    /// <remarks>
    /// 露出的高度含底下那段透明，所以比例为 0 时露的正好是那段透明、液面一行都不显示，为 1 时液面整段露出。
    /// 比例先钳在 0–1，取整只在这里做。
    /// </remarks>
    public static int VigorShownHeight(double ratio) =>
        VigorLiquidInset + (int)Math.Round(Math.Clamp(ratio, 0.0, 1.0) * VigorLiquidHeight);

    /// <summary>某个场合下资源块装哪几条，顺序就是从上到下。</summary>
    /// <remarks>
    /// 关卡里是生命、体力与法力；基地里只有精力。精力是当日预算，关卡里时钟停着，它不在一次挥刀之间
    /// 变；那三条在基地里没有动作消耗它们（设计仓 design/界面系统.md 同一节）。
    /// </remarks>
    public static IReadOnlyList<HudGaugeKind> GaugesIn(UIContext context) => context switch
    {
        UIContext.Level => [HudGaugeKind.Health, HudGaugeKind.Stamina, HudGaugeKind.Mana],
        UIContext.Base => [HudGaugeKind.DailyVigor],
        _ => throw new ArgumentOutOfRangeException(nameof(context), $"没有这个场合：{context}"),
    };

    /// <summary>某个场合下有哪几块。目标进度与队友只在关卡里有。</summary>
    public static IReadOnlyList<HudBlock> BlocksIn(UIContext context) => context switch
    {
        UIContext.Level => [HudBlock.Resources, HudBlock.Objective, HudBlock.Teammates, HudBlock.ActionBar],
        UIContext.Base => [HudBlock.Resources, HudBlock.ActionBar],
        _ => throw new ArgumentOutOfRangeException(nameof(context), $"没有这个场合：{context}"),
    };

    /// <summary>取一块的内容区尺寸，不含框边。</summary>
    public static HudRect ContentSizeOf(HudBlock block, UIContext context)
    {
        RequireIn(block, context);
        return (block, context) switch
        {
            (HudBlock.Objective, _) => new HudRect(
                X: 0, Y: 0,
                Width: UIMetrics.IconSmall + UIMetrics.ItemGap + (ObjectiveMaxChars * UIMetrics.FontSize),
                Height: UIMetrics.LineHeight),

            // 关卡：三条各占一行，下面一行状态位。
            (HudBlock.Resources, UIContext.Level) => new HudRect(
                X: 0, Y: 0,
                Width: ResourcesContentWidth,
                Height: (GaugesIn(context).Count * GaugeRowHeight) + StatusRowHeight),

            // 基地：标签加那支竖的精力条，隔一个栅格是两行两列状态位；精力条与状态位一样高。与关卡那一块同宽。
            (HudBlock.Resources, UIContext.Base) => new HudRect(
                X: 0, Y: 0,
                Width: ResourcesContentWidth,
                Height: VigorTubeHeight),

            // 一行格子，格距照画好的那条栏。
            (HudBlock.ActionBar, _) => new HudRect(
                X: 0, Y: 0,
                Width: (ActionBarCellCount * UIMetrics.Cell) + ((ActionBarCellCount - 1) * ActionBarCellGap),
                Height: UIMetrics.Cell),

            (HudBlock.Teammates, _) => new HudRect(
                X: 0, Y: 0,
                Width: (MaxTeammates * PortraitSize) + ((MaxTeammates - 1) * UIMetrics.ItemGap),
                Height: PortraitSize + GaugeBarHeight),

            _ => throw new ArgumentOutOfRangeException(nameof(block), $"没有这一块：{block}"),
        };
    }

    /// <summary>一块的框边：内容离外框多远，横向与纵向各一个数。</summary>
    /// <remarks>
    /// 那条横排格子坐在画好的栏上，框边就是那张图的两头与上下（<see cref="ActionBarCapX"/>、
    /// <see cref="ActionBarCapY"/>）；其余几块坐在细框上，框边是面板内边距。
    /// </remarks>
    public static (int X, int Y) InsetOf(HudBlock block) => block == HudBlock.ActionBar
        ? (ActionBarCapX, ActionBarCapY)
        : (UIMetrics.PanelPadding, UIMetrics.PanelPadding);

    /// <summary>取一块的外框尺寸：内容区加两边框边。这是它在屏幕上真正占的地方。</summary>
    public static HudRect SizeOf(HudBlock block, UIContext context)
    {
        var content = ContentSizeOf(block, context);
        var (x, y) = InsetOf(block);
        return new HudRect(0, 0, content.Width + (x * 2), content.Height + (y * 2));
    }

    /// <summary>这一块贴哪个角、离两条边各多远。引擎层只用这个，不用坐标。</summary>
    /// <remarks>
    /// 关卡那一套：资源左上，目标进度接在它右边隔一个栅格；队友右上，技能栏在队友下面隔一个栅格。
    /// **技能栏的位置按队友块的尺寸算、与队友块显不显示无关** —— 单人出征时队友那一块收起，技能栏
    /// 不跟着上移，位置固定才有肌肉记忆。基地那一套：资源左下，随身栏右下。两种场合的那条格子都贴右，
    /// 宽度又一样，所以横坐标对齐。
    /// </remarks>
    public static HudPlacement PlacementOf(HudBlock block, UIContext context)
    {
        RequireIn(block, context);
        var margin = UIMetrics.SafeMargin;
        return (context, block) switch
        {
            (UIContext.Level, HudBlock.Resources) => new HudPlacement(HudAnchor.TopLeft, margin, margin),
            (UIContext.Level, HudBlock.Objective) => new HudPlacement(
                HudAnchor.TopLeft, margin + SizeOf(HudBlock.Resources, context).Width + UIMetrics.Grid, margin),
            (UIContext.Level, HudBlock.Teammates) => new HudPlacement(HudAnchor.TopRight, margin, margin),
            (UIContext.Level, HudBlock.ActionBar) => new HudPlacement(
                HudAnchor.TopRight, margin, margin + SizeOf(HudBlock.Teammates, context).Height + UIMetrics.Grid),
            (UIContext.Base, HudBlock.Resources) => new HudPlacement(HudAnchor.BottomLeft, margin, margin),
            (UIContext.Base, HudBlock.ActionBar) => new HudPlacement(HudAnchor.BottomRight, margin, margin),
            _ => throw new ArgumentOutOfRangeException(nameof(block), $"{context} 里没有这一块：{block}"),
        };
    }

    /// <summary>算一块在给定视口下占哪个矩形。它只是一把尺，节点位置由放置决定、不由这里设。</summary>
    /// <remarks>
    /// 这里按放置算出它应该在哪，引擎层按同一份放置摆，两条路径独立，没有东西自动核对它们一致。
    /// 失效的样子是：锚点摆错在窄窗口上看不出来，只在宽窗口上错位。所以实机确认 HUD 时要拉一下
    /// 窗口宽度，别只在默认尺寸下看。
    /// </remarks>
    public static HudRect RectOf(HudBlock block, UIContext context, int viewportWidth, int viewportHeight)
    {
        var size = SizeOf(block, context);
        var placement = PlacementOf(block, context);
        var x = placement.Anchor is HudAnchor.TopLeft or HudAnchor.BottomLeft
            ? placement.InsetX
            : viewportWidth - placement.InsetX - size.Width;
        var y = placement.Anchor is HudAnchor.TopLeft or HudAnchor.TopRight
            ? placement.InsetY
            : viewportHeight - placement.InsetY - size.Height;
        return new HudRect(x, y, size.Width, size.Height);
    }

    /// <summary>某个场合下每一块各占哪个矩形。</summary>
    public static IReadOnlyList<(HudBlock Block, HudRect Rect)> RectsOf(
        UIContext context, int viewportWidth, int viewportHeight) =>
        [.. BlocksIn(context).Select(b => (b, RectOf(b, context, viewportWidth, viewportHeight)))];

    /// <summary>某个场合下各块合起来占视口的几成。</summary>
    public static double CoverageRatio(UIContext context, int viewportWidth, int viewportHeight) =>
        (double)RectsOf(context, viewportWidth, viewportHeight).Sum(r => r.Rect.Area)
        / (viewportWidth * viewportHeight);

    /// <summary>这个矩形的底边越没越过关卡里留给 HUD 的顶上那一段。</summary>
    public static bool ExceedsTopBand(HudRect rect) => rect.Bottom > LevelTopBandHeight;

    /// <summary>关卡那一套里有哪几块越过了顶上那一段。这条有单元测试盯着，必须是空的。</summary>
    public static IReadOnlyList<HudBlock> BlocksBelowTopBand(int viewportWidth, int viewportHeight) =>
        [.. RectsOf(UIContext.Level, viewportWidth, viewportHeight)
            .Where(r => ExceedsTopBand(r.Rect)).Select(r => r.Block)];

    /// <summary>基地里主角可能出现的那一块。HUD 压到它就是压掉了该看的东西。</summary>
    /// <remarks>
    /// 它算的不是「角色现在在哪」，而是「相机死区允许他跑到哪」：相机把角色留在死区内不动镜头，所以
    /// 角色相对屏幕中心的偏移上限就是死区的半宽半高，再各向外扩半个本体。死区那两个数本来就是屏幕
    /// 像素（见 <see cref="CameraFeel"/>）；两种视角的相机都不缩放（设计仓 canon/gameplay/玩法定位.md 的
    /// 「像素基准」），俯视本体两个基础单位就是屏上两个基础单位，所以这里不乘缩放。
    /// </remarks>
    public static HudRect ActorBand(int viewportWidth, int viewportHeight)
    {
        var halfW = CameraFeel.DeadzoneHalfWidthScreenPx + (UIMetrics.IconLarge / 2);
        var halfH = CameraFeel.DeadzoneHalfHeightScreenPx + (UIMetrics.IconLarge / 2);
        return new HudRect((viewportWidth / 2) - halfW, (viewportHeight / 2) - halfH, halfW * 2, halfH * 2);
    }

    /// <summary>基地那一套里有没有哪一块压到了 <see cref="ActorBand"/>。这条有单元测试盯着，必须是空的。</summary>
    public static IReadOnlyList<HudBlock> BlocksOverActorBand(int viewportWidth, int viewportHeight)
    {
        var band = ActorBand(viewportWidth, viewportHeight);
        return [.. RectsOf(UIContext.Base, viewportWidth, viewportHeight)
                   .Where(r => r.Rect.Overlaps(band)).Select(r => r.Block)];
    }

    /// <summary>
    /// 含屏幕中心、占满整幅宽度、且一块 HUD 都不压的最高那条横带。
    /// </summary>
    /// <remarks>
    /// 用「整幅宽的横带」而不是「最大空白面积」，因为玩家要读的是一条水平走廊，左右两端的敌人和平台
    /// 跟正中间一样要紧。有块横跨中心线时返回高度 0，那说明这套放置把可读区切断了。
    /// </remarks>
    public static HudRect ClearBand(UIContext context, int viewportWidth, int viewportHeight)
    {
        var centerY = viewportHeight / 2;
        var top = 0;
        var bottom = viewportHeight;
        foreach (var (_, rect) in RectsOf(context, viewportWidth, viewportHeight))
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

    private static void RequireIn(HudBlock block, UIContext context)
    {
        if (!BlocksIn(context).Contains(block))
        {
            throw new ArgumentOutOfRangeException(nameof(block), $"{context} 里没有这一块：{block}");
        }
    }
}
