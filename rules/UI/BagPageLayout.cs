namespace Tinderhearth.Rules.UI;

/// <summary>手环里背包那一页怎么排：深色厚框里几行几列、格与格隔多宽、整页多高。</summary>
/// <remarks>
/// 一行几格、几行，就是背包网格几格宽、几格高：页上一格对着网格里一格，背包升了级只多几行，不用重摆。全部用画好的件拼，
/// 代码照这几个数摆，不另画、也不靠目测调位置。配置里每一级放不放得进手环的内容区，由测试判。
///
/// 页上先排随身栏那一排、再排网格，格子从左到右、从上到下排：网格宽与随身栏同格数时，随身栏那一排正好是页上第一行。
/// 一格 16 像素、格间不留缝的那一页还没有，归设计仓台账 UI-67。
/// </remarks>
public static class BagPageLayout
{
    /// <summary>格与格之间隔几像素：选中框画在格子外面一圈，挨着的两格各画一圈也不打架。</summary>
    public const int CellGap = 2 * UIMetrics.SelectOutset;

    /// <summary>深色厚框到格子多远。与手环外框同一档：那张厚框的圆角占得深，挨近了格子会压在角上。</summary>
    public const int FramePadding = WristbandLayout.FramePadding;

    /// <summary>品质点落在格子左下角，离格子的边这么远：与图标那一格的左下角对齐。</summary>
    public const int PipInset = UIMetrics.CellInset;

    /// <summary>格子那一片多宽：一行 <paramref name="columns"/> 格（背包网格几格宽）。</summary>
    public static int GridWidth(int columns) => Span(columns, nameof(columns));

    /// <summary>格子那一片多高：<paramref name="rows"/> 行（背包网格几格高）。</summary>
    public static int GridHeight(int rows) => Span(rows, nameof(rows));

    /// <summary>深色厚框多宽。</summary>
    public static int FrameWidth(int columns) => GridWidth(columns) + (2 * FramePadding);

    /// <summary>深色厚框多高。</summary>
    public static int FrameHeight(int rows) => GridHeight(rows) + (2 * FramePadding);

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
    public static int PageHeight(int rows) =>
        Math.Max(FrameHeight(rows), ControlsHeight) + UIMetrics.ItemGap + UIMetrics.LineHeight;

    /// <summary>丢之前那一问一行放几个全宽字：问句里有名称、品质与件数，放不下就折行。</summary>
    public const int ConfirmLineChars = 20;

    /// <summary>那一问的框多宽：一行字加两边的框内边距（与手环外框同一档）。</summary>
    public const int ConfirmWidth = (ConfirmLineChars * UIMetrics.FontSize) + (2 * WristbandLayout.FramePadding);

    /// <summary>几格排成一行或一列有多长：每格一个格子边长，格与格之间隔一个格距。</summary>
    private static int Span(int cells, string name)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(cells, 1, name);
        return (cells * UIMetrics.Cell) + ((cells - 1) * CellGap);
    }
}
