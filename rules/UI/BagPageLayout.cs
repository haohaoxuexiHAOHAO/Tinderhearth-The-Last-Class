namespace Tinderhearth.Rules.UI;

/// <summary>游戏菜单里背包那一页怎么排：深色厚框里几行几列、每一块多大、图放在哪、整页多高。</summary>
/// <remarks>
/// 页上一格对着背包里一格。每格是一张格子框（<see cref="UIMetrics.Cell"/>），框里正中是一个基础单位的图；格子框挨着格子框、
/// 不留缝。占不止一格的东西，那几格并成一个大框（同一张格子框按九宫格拉开），图照占地的大小画、放在大框正中、不缩放
/// （设计仓 production/界面绘制约定.md 的「尺寸阶梯」一节）。全部用画好的件拼，代码照这几个数摆，不另画、也不靠目测调位置；
/// 配置里每一级放不放得进游戏菜单的内容区，由测试判。
///
/// 页上先排随身栏那一排，再排网格，都从左到右、从上到下。随身栏那几格不够一行也占一整行，网格从下一行开始：
/// 一块大东西要按网格的行列画，不能跟随身栏那几格挤在同一行里。
/// </remarks>
public static class BagPageLayout
{
    /// <summary>深色厚框到格子多远。与游戏菜单外框同一档：那张厚框的圆角占得深，挨近了格子会压在角上。</summary>
    public const int FramePadding = GameMenuLayout.FramePadding;

    /// <summary>品质点落在一块的左下角，离那一块的边这么远：与 1 格那一块里图的左下角对齐。</summary>
    public const int PipInset = UIMetrics.CellInset;

    /// <summary>
    /// 拿着一堆指着别处时，要占的那一块上画的预览离那一块的边多远：1 格那一块上正好是当前操作格那张件的原尺寸
    /// （它比一格地大一圈），大的那几块把它按九宫格拉开。
    /// </summary>
    public const int PreviewInset = (UIMetrics.Cell - (UIMetrics.BaseUnit + (WorldUILayout.CellCursorOutset * 2))) / 2;

    /// <summary>随身栏那几格占几行：几格不够一行也占一整行。</summary>
    public static int CarryRows(int carryCount, int columns)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(carryCount);
        ArgumentOutOfRangeException.ThrowIfLessThan(columns, 1);
        return (carryCount + columns - 1) / columns;
    }

    /// <summary>页上一共几行：随身栏那几行，加网格几格高。</summary>
    public static int PageRows(int gridHeight, int carryCount, int columns)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(gridHeight, 1);
        return CarryRows(carryCount, columns) + gridHeight;
    }

    /// <summary>格子那一片多宽：一行 <paramref name="columns"/> 格（背包网格几格宽）。</summary>
    public static int GridWidth(int columns) => Span(columns, nameof(columns));

    /// <summary>格子那一片多高：页上 <paramref name="rows"/> 行（随身栏那几行加网格几格高，见 <see cref="PageRows"/>）。</summary>
    public static int GridHeight(int rows) => Span(rows, nameof(rows));

    /// <summary>深色厚框多宽。</summary>
    public static int FrameWidth(int columns) => GridWidth(columns) + (2 * FramePadding);

    /// <summary>深色厚框多高。</summary>
    public static int FrameHeight(int rows) => GridHeight(rows) + (2 * FramePadding);

    /// <summary>页上第 <paramref name="column"/> 列、第 <paramref name="row"/> 行那一格的左上角，相对格子那一片的左上角。</summary>
    public static PixelPoint CellOrigin(int column, int row)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(column);
        ArgumentOutOfRangeException.ThrowIfNegative(row);
        return new PixelPoint(column * UIMetrics.Cell, row * UIMetrics.Cell);
    }

    /// <summary>宽 <paramref name="width"/>、高 <paramref name="height"/> 格的一块多大：几格就几张格子框那么大，挨着不留缝。</summary>
    public static PixelSize BlockSize(int width, int height) =>
        new(Span(width, nameof(width)), Span(height, nameof(height)));

    /// <summary>那一块里的图多大：占地乘基础单位，与图标核对同一个数（代码仓 rules/Economy/ItemIconCheck.cs）。</summary>
    public static PixelSize PictureSize(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);
        return new PixelSize(width * UIMetrics.BaseUnit, height * UIMetrics.BaseUnit);
    }

    /// <summary>那一块里的图离那一块左上角多远：放在正中。1 格那一块上就是图标在格子里的那个位置。</summary>
    public static PixelPoint PictureOffset(int width, int height)
    {
        var block = BlockSize(width, height);
        var picture = PictureSize(width, height);
        return new PixelPoint((block.Width - picture.Width) / 2, (block.Height - picture.Height) / 2);
    }

    /// <summary>带字的按钮多高：一行字加上下的内容边距（像素主题里带字的按钮那一项取面板内边距）。</summary>
    public const int ButtonHeight = UIMetrics.LineHeight + (2 * UIMetrics.PanelPadding);

    /// <summary>厚框右边那一列有几行按钮：排序一行、筛选一行、一键整理与丢弃一行。</summary>
    public const int ControlRows = 3;

    /// <summary>厚框右边那一列多高：几行按钮，最下面是还剩几个空格那一行，行与行隔一个组内间距。</summary>
    /// <remarks>
    /// 与代码仓 <c>src/UI/BagPageView.cs</c> 里那一列的排法逐项对应：那边加一行或改了间距，这里要跟着改，
    /// 否则「放得下」那条测试判的就不是画出来的那一页。
    /// </remarks>
    public const int ControlsHeight =
        (ControlRows * ButtonHeight) + UIMetrics.LineHeight + (ControlRows * UIMetrics.ItemGap);

    /// <summary>整页多高：厚框与右边那一列取高的那个，下面隔一个组内间距是写那一格是什么的那一行。</summary>
    /// <param name="rows">页上几行（随身栏那几行加网格几格高，见 <see cref="PageRows"/>）。</param>
    public static int PageHeight(int rows) =>
        Math.Max(FrameHeight(rows), ControlsHeight) + UIMetrics.ItemGap + UIMetrics.LineHeight;

    /// <summary>丢之前那一问一行放几个全宽字：问句里有名称、品质与件数，放不下就折行。</summary>
    public const int ConfirmLineChars = 20;

    /// <summary>那一问的框多宽：一行字加两边的框内边距（与游戏菜单外框同一档）。</summary>
    public const int ConfirmWidth = (ConfirmLineChars * UIMetrics.FontSize) + (2 * GameMenuLayout.FramePadding);

    /// <summary>几格排成一行或一列有多长：每格一张格子框，挨着不留缝。</summary>
    private static int Span(int cells, string name)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(cells, 1, name);
        return cells * UIMetrics.Cell;
    }
}
