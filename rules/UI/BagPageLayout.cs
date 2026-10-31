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

    /// <summary>整页多高：厚框，下面隔一个组内间距是选中那一格的那一行。还剩几个空格写在厚框右边，不占高。</summary>
    public static int PageHeight(int slotCount) => FrameHeight(slotCount) + UIMetrics.ItemGap + UIMetrics.LineHeight;
}
