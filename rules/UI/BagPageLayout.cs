namespace Tinderhearth.Rules.UI;

/// <summary>手环里背包那一页怎么排：深色厚框里一行几格、格与格隔多宽、整页多高。</summary>
/// <remarks>
/// 照作者给的参考图：深色厚框里格子一格挨一格，一行四格，格数多了往下加行（设计仓 spec/prd-items-in-bag.md 的
/// 「界面与表现考虑」一节）。全部用画好的件拼，代码照这几个数摆，不另画、也不靠目测调位置；背包升了级多出几格，
/// 只多几行，不用重摆。配置里格数最多的那一级放不放得进手环的内容区，由测试判。
///
/// 格子从左到右、从上到下按格号排，与背包的格号一致：随身栏是背包开头那几格，两处看到的先后是同一个。
/// </remarks>
public static class BagPageLayout
{
    /// <summary>一行几格。</summary>
    public const int SlotsPerRow = 4;

    /// <summary>格与格之间隔几像素：选中框画在格子外面一圈，挨着的两格各画一圈也不打架。</summary>
    public const int CellGap = 2 * UIMetrics.SelectOutset;

    /// <summary>深色厚框到格子多远。与手环外框同一档：那张厚框的圆角占得深，挨近了格子会压在角上。</summary>
    public const int FramePadding = WristbandLayout.FramePadding;

    /// <summary>品质点落在格子左下角，离格子的边这么远：与图标那一格的左下角对齐。</summary>
    public const int PipInset = UIMetrics.CellInset;

    /// <summary>几格要几行：一行 <see cref="SlotsPerRow"/> 格，最后一行可以不满。</summary>
    public static int RowsFor(int slotCount)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(slotCount, 1);
        return (slotCount + SlotsPerRow - 1) / SlotsPerRow;
    }

    /// <summary>格子那一片多宽。</summary>
    public static int GridWidth => (SlotsPerRow * UIMetrics.Cell) + ((SlotsPerRow - 1) * CellGap);

    /// <summary>格子那一片多高。</summary>
    public static int GridHeight(int slotCount)
    {
        var rows = RowsFor(slotCount);
        return (rows * UIMetrics.Cell) + ((rows - 1) * CellGap);
    }

    /// <summary>深色厚框多宽。</summary>
    public static int FrameWidth => GridWidth + (2 * FramePadding);

    /// <summary>深色厚框多高。</summary>
    public static int FrameHeight(int slotCount) => GridHeight(slotCount) + (2 * FramePadding);

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
    public static int PageHeight(int slotCount) =>
        Math.Max(FrameHeight(slotCount), ControlsHeight) + UIMetrics.ItemGap + UIMetrics.LineHeight;

    /// <summary>丢之前那一问一行放几个全宽字：问句里有名称、品质与件数，放不下就折行。</summary>
    public const int ConfirmLineChars = 20;

    /// <summary>那一问的框多宽：一行字加两边的框内边距（与手环外框同一档）。</summary>
    public const int ConfirmWidth = (ConfirmLineChars * UIMetrics.FontSize) + (2 * WristbandLayout.FramePadding);
}
