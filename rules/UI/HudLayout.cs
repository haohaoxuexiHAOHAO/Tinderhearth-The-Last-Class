namespace Tinderhearth.Rules.UI;

/// <summary>HUD 的几块内容。一块一个锚点，块内靠容器排。</summary>
public enum HudBlock
{
    /// <summary>目标进度。它要始终可见，进度为 0 或已达成时也不隐藏。</summary>
    Objective,

    /// <summary>主角资源：生命、体力、法力与精力。</summary>
    Resources,

    /// <summary>底边右侧那条横排格子。装什么由场景决定，尺寸和锚点两边完全一样。</summary>
    /// <remarks>
    /// 关卡里装技能位（带冷却表现和当前修饰键组的提示），经营场景里装随身栏（图标加件数）。
    /// 俯视场景没有技能位，所以那一块在那里正好空着给随身栏。
    ///
    /// 两种内容共用这一块，不是两块。它们的格数同源（<see cref="CarrySlotBar.SlotCount"/> 取的
    /// 就是 <see cref="InputActions.Skills"/> 的条数），所以「同宽」由算式保证，不是两处各写一个
    /// 常量再指望它们一直相等。于是 <see cref="BlocksOverActorBand"/> 的输入不受影响。
    ///
    /// 将来关卡里要让技能位和随身栏两行并存时，这一块的高度会变，那时要重算一遍占屏。现在不做，
    /// 因为关卡里一个道具都还没有。
    /// </remarks>
    ActionBar,

    /// <summary>队友状态，最多容纳编队上限那么多名学生；一个都没有时整块收起。</summary>
    Teammates,
}

/// <summary>一块贴在屏幕的哪个角。引擎层用的就是这个，它翻译成锚点预设加安全边距偏移。</summary>
/// <remarks>
/// 粒度只到「贴哪个角」、没有坐标，因为视口按 expand 拉伸时逻辑宽度是个变量（实测
/// 3840×2130 的窗口得到的逻辑尺寸是 649×360），任何横向坐标都会在宽窗口上错位。
///
/// 这里没有「居中」这个取值。另一套候选是「底边一条、技能居中」，选的是四角这套。留一条实测
/// 事实免得将来有人重新发明它：逻辑宽度为奇数时，居中块的横坐标会落在半个逻辑像素上。两倍缩放
/// 下那正好是一个物理像素、没有后果；三倍下是一个半，素材边界就不再落在物理像素格上。
/// 后半句是推断、没实测，验法是在三倍窗口下看居中块的边缘有没有糊掉半格。
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

/// <summary>屏幕上的一个整数矩形。只用来算占屏比例和可读区，不用来摆节点。</summary>
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

/// <summary>HUD 的排版尺寸与放置。尺寸全部从 <see cref="UIMetrics"/> 推，不另定一套。</summary>
/// <remarks>
/// 这些数放在规则层，是因为它们之间有判得出来的关系：块高是行高的整数倍、块不许压到角色所在的
/// 可读区、居中偏移是不是整数。放这里就能用单元测试盯住，引擎层也因此一个数字字面量都不需要。
///
/// 这里没有任何玩法数值。生命上限、冷却时长归设计仓 design/数值模型.md，由
/// <see cref="HudViewModel"/> 传入。本文件里的数全是排版量：几个格子、多宽、贴哪条边。
///
/// 块尺寸是定值而不是随内容伸缩。定值让 <see cref="RectOf"/> 算出来的等于实际摆出来的，于是占屏
/// 比例和可读区算得出来、也测得出来。内容比框长时截断，目标进度那一行因此按最长文案定宽。
/// </remarks>
public static class HudLayout
{
    /// <summary>编队上限：常规出征是主角带这么多名学生。</summary>
    public const int MaxTeammates = 4;

    /// <summary>资源条的条数：生命、体力、法力，再加经营侧的当日预算一条。</summary>
    public const int GaugeCount = 4;

    /// <summary>资源条标签占几个全宽汉字。最长的标签是「体力」和「精力」，都是两个字。</summary>
    /// <remarks>
    /// 西文在这款字体里宽 8 像素，两个字母比两个汉字窄，所以标签列按汉字算就够宽。
    /// </remarks>
    public const int GaugeLabelChars = 2;

    /// <summary>资源条的填充部分宽几个基础单位。</summary>
    /// <remarks>
    /// 下限受可读性管着：条太短时一格像素代表的量太大，玩家看不出变化。上限受占屏管着：资源块
    /// 再宽就开始挤中间那块可读区。
    /// </remarks>
    public const int GaugeBarUnits = 4;

    /// <summary>资源条高几像素。这个数是素材 bar-track.png 的高，不是随手取的。</summary>
    public const int GaugeBarHeight = 6;

    /// <summary>队友头像框边长几像素。这个数是素材 portrait-frame.png 的尺寸，头像加一圈框。</summary>
    public const int PortraitSize = 20;

    /// <summary>目标进度那一行最长几个全宽汉字，按达成后那句最长的文案定。</summary>
    /// <remarks>
    /// 按最长那句定宽而不是按常态那句，因为达成态那句更长；按短的定宽会让它在最需要看清的时刻
    /// 被截断，而进度要始终可见。
    /// </remarks>
    public const int ObjectiveMaxChars = 11;

    /// <summary>一组修饰键覆盖几个技能位，与 <see cref="InputActions.SkillsPerGroup"/> 同源。</summary>
    public static int SkillsPerGroup => InputActions.SkillsPerGroup;

    /// <summary>技能位分几组。算出来正好对上两个扳机。</summary>
    public static int SkillGroupCount => InputActions.Skills.Count / SkillsPerGroup;

    /// <summary>资源条标签列宽。</summary>
    public static int GaugeLabelWidth => GaugeLabelChars * UIMetrics.FontSize;

    /// <summary>资源条填充部分的宽。</summary>
    public static int GaugeBarWidth => GaugeBarUnits * UIMetrics.BaseUnit;

    /// <summary>一条资源占的高。取行高，好让标签与条在同一条基线节奏上。</summary>
    public static int GaugeRowHeight => UIMetrics.LineHeight;

    /// <summary>一个技能位单元的高：手柄按键提示叠在图标内，不另占一行。</summary>
    public static int SkillCellHeight => UIMetrics.IconSmall;

    /// <summary>随身栏有几格。与技能位同一个数，所以两种内容同宽由算式保证。</summary>
    /// <remarks>
    /// 它读 <see cref="CarrySlotBar.SlotCount"/>，而那个又读 <see cref="InputActions.Skills"/> 的
    /// 条数，一条链到底，中间一处都没有抄出来的数字。抄一个的后果是将来改格数时两块宽度悄悄分叉，
    /// 而分叉只在拉宽窗口时看得出来。
    /// </remarks>
    public static int CarrySlotCount => CarrySlotBar.SlotCount;

    /// <summary>随身栏那几格紧邻横排占的宽。它不含两组之间那道分界，因为随身栏没有组。</summary>
    /// <remarks>
    /// 所以它比 <see cref="HudBlock.ActionBar"/> 那一块的内容窄一个栅格。随身栏沿用同一个框，
    /// 多出来的那一格空隙留在中间、与技能位那道分界对齐，这样两种内容的格子落在同一批横坐标上，
    /// 换场景时格位不跳。
    /// </remarks>
    public static int CarryRowWidth => CarrySlotCount * UIMetrics.IconSmall;

    /// <summary>一组技能位横排占的宽：几个紧邻的图标格。</summary>
    /// <remarks>
    /// 技能位排成一行，不另给修饰键开一列记号。那一列在键鼠下永远是空的，因为键鼠没有扳机这一层。
    ///
    /// 手柄要辨「哪几个归哪个扳机」靠三样：两组之间那一个栅格的间隔（看得出是均分两组）、按住扳机
    /// 时那一组连着高亮、以及每个图标内叠的面键记号。左组对左扳机、右组对右扳机是位置约定，按住
    /// 任一扳机时的高亮会当场教会玩家。这一条要作者实机看。
    /// </remarks>
    public static int SkillGroupWidth => SkillsPerGroup * UIMetrics.IconSmall;

    /// <summary>取一块的内容区尺寸，不含面板内边距。</summary>
    /// <remarks>
    /// 内容区和外框分开算，因为每块都坐在一张面板底上：面板给一圈描边加内边距，好让小字压在杂乱
    /// 的关卡背景上仍读得清。两个尺寸分开算，占屏比例才不会把内边距漏掉。读不读得清要作者实机看。
    /// </remarks>
    public static HudRect ContentSizeOf(HudBlock block) => block switch
    {
        HudBlock.Objective => new HudRect(
            X: 0, Y: 0,
            Width: UIMetrics.IconSmall + UIMetrics.ItemGap
                   + (ObjectiveMaxChars * UIMetrics.FontSize),
            Height: UIMetrics.LineHeight),

        HudBlock.Resources => new HudRect(
            X: 0, Y: 0,
            Width: GaugeLabelWidth + UIMetrics.ItemGap + GaugeBarWidth,
            Height: GaugeCount * GaugeRowHeight),

        // 格子一行横排，两组之间空一个栅格。关卡里那道空隙是「这一组归左扳机、那一组归右扳机」
        // 的视觉分界。经营侧的随身栏没有组，但它沿用同一个框，这样两种内容长得像同一条栏，
        // 玩家换场景时不用重新学它是什么。
        HudBlock.ActionBar => new HudRect(
            X: 0, Y: 0,
            Width: (SkillGroupCount * SkillGroupWidth)
                   + ((SkillGroupCount - 1) * UIMetrics.Grid),
            Height: SkillCellHeight),

        HudBlock.Teammates => new HudRect(
            X: 0, Y: 0,
            Width: (MaxTeammates * PortraitSize) + ((MaxTeammates - 1) * UIMetrics.ItemGap),
            Height: PortraitSize + GaugeBarHeight),

        _ => throw new ArgumentOutOfRangeException(nameof(block), $"没有这一块：{block}"),
    };

    /// <summary>取一块的外框尺寸：内容区加两边内边距。这是它在屏幕上真正占的地方。</summary>
    public static HudRect SizeOf(HudBlock block)
    {
        var content = ContentSizeOf(block);
        var both = UIMetrics.PanelPadding * 2;
        return new HudRect(0, 0, content.Width + both, content.Height + both);
    }

    /// <summary>这一块贴哪个角。引擎层只用这个，不用坐标。</summary>
    /// <remarks>
    /// 四角贴边，从两套候选里选定的（另一套是「底边一条、技能居中」）。四角这套有两条算得出来的
    /// 好处：中间留出一条贯通左右的可读横带（高度由 <see cref="ClearBand"/> 算），而且一个居中锚点
    /// 都不用，于是任何逻辑宽度下各块都落在整数像素上（见 <see cref="HudAnchor"/>）。
    /// </remarks>
    public static HudAnchor AnchorOf(HudBlock block) => block switch
    {
        // 目标进度在左上：它是唯一「一直要能扫到」的东西，而左上是阅读起点。
        HudBlock.Objective => HudAnchor.TopLeft,

        // 队友在右上：与目标进度同属「偶尔扫一眼」，摆在上边一行两端。
        HudBlock.Teammates => HudAnchor.TopRight,

        // 资源在左下：手放在键盘左手区，眼睛往左下扫最短。
        HudBlock.Resources => HudAnchor.BottomLeft,

        // 那条横排格子在右下：与资源同在底边，两者构成「我还有多少、我能用什么」这一对。
        // 关卡里是技能、经营侧是随身栏，两边同一个角，换场景时眼睛不用换地方找。
        HudBlock.ActionBar => HudAnchor.BottomRight,

        _ => throw new ArgumentOutOfRangeException(nameof(block), $"没有这一块：{block}"),
    };

    /// <summary>算一块在给定视口下占哪个矩形。它只是一把尺，节点位置由锚点决定、不由这里设。</summary>
    /// <remarks>
    /// 这里按锚点和安全边距算出它应该在哪，引擎层按锚点摆，两条路径独立，没有东西自动核对它们
    /// 一致。失效的样子是：锚点摆错在窄窗口上看不出来，只在宽窗口上错位。所以实机确认 HUD 时要
    /// 拉一下窗口宽度，别只在默认尺寸下看。
    /// </remarks>
    public static HudRect RectOf(HudBlock block, int viewportWidth, int viewportHeight)
    {
        var size = SizeOf(block);
        var margin = UIMetrics.SafeMargin;
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

    /// <summary>每一块各占哪个矩形。</summary>
    public static IReadOnlyList<(HudBlock Block, HudRect Rect)> RectsOf(
        int viewportWidth, int viewportHeight) =>
        [.. Enum.GetValues<HudBlock>()
                .Select(b => (b, RectOf(b, viewportWidth, viewportHeight)))];

    /// <summary>各块合起来占视口的几成。队友区收起时不算它，那时它真的不占地方。</summary>
    public static double CoverageRatio(int viewportWidth, int viewportHeight) =>
        (double)RectsOf(viewportWidth, viewportHeight).Sum(r => r.Rect.Area)
        / (viewportWidth * viewportHeight);

    /// <summary>主角可能出现的那一块。HUD 压到它就是压掉了该看的东西。</summary>
    /// <remarks>
    /// 它算的不是「角色现在在哪」，而是「相机死区允许他跑到哪」。相机把角色留在死区内不动镜头，
    /// 所以角色相对屏幕中心的偏移上限就是死区的半宽半高，再各向外扩一个精灵格。
    ///
    /// 死区那两个数本来就是屏幕像素（见 <see cref="CameraFeel"/>），不用换算缩放；精灵格是世界
    /// 像素，要乘侧视的缩放倍数。
    /// </remarks>
    public static HudRect ActorBand(int viewportWidth, int viewportHeight)
    {
        var halfW = CameraFeel.DeadzoneHalfWidthScreenPx
                    + (UIMetrics.IconLarge * UIMetrics.SideViewZoom / 2);
        var halfH = CameraFeel.DeadzoneHalfHeightScreenPx
                    + (UIMetrics.IconLarge * UIMetrics.SideViewZoom / 2);
        return new HudRect((viewportWidth / 2) - halfW, (viewportHeight / 2) - halfH,
                           halfW * 2, halfH * 2);
    }

    /// <summary>有没有哪一块压到了 <see cref="ActorBand"/>。这条有单元测试盯着，必须是空的。</summary>
    public static IReadOnlyList<HudBlock> BlocksOverActorBand(int viewportWidth, int viewportHeight)
    {
        var band = ActorBand(viewportWidth, viewportHeight);
        return [.. RectsOf(viewportWidth, viewportHeight)
                   .Where(r => r.Rect.Overlaps(band)).Select(r => r.Block)];
    }

    /// <summary>
    /// 含屏幕中心、占满整幅宽度、且一块 HUD 都不压的最高那条横带。比较放置方案时比的就是这个数。
    /// </summary>
    /// <remarks>
    /// 用「整幅宽的横带」而不是「最大空白面积」，因为侧视关卡里玩家要读的是一条水平走廊，左右
    /// 两端的敌人和平台跟正中间一样要紧。面积大但被切成两块的空白，读起来不如一条通的横带。
    /// 有块横跨中心线时返回高度 0，那说明这套方案把可读区切断了。
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
