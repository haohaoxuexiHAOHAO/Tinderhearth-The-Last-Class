using System.Diagnostics.CodeAnalysis;

namespace Tinderhearth.Rules.Economy;

/// <summary>一片地：一块矩形里的每一格地块，按格坐标找。</summary>
/// <remarks>
/// 它是世界状态（<see cref="WorldState"/>）的一部分，所以每日结算复制世界时它整份跟着复制。哪几格算这一片地
/// 由场景定：作者在农场里摆的位置与大小，经引擎那一侧交进来。
/// </remarks>
public sealed class PlotGrid
{
    private readonly Dictionary<GridCell, Plot> _plots;

    private PlotGrid(GridCell origin, int width, int height, Dictionary<GridCell, Plot> plots)
    {
        Origin = origin;
        Width = width;
        Height = height;
        _plots = plots;
    }

    /// <summary>左上角那一格。</summary>
    public GridCell Origin { get; }

    /// <summary>几格宽。</summary>
    public int Width { get; }

    /// <summary>几格高。</summary>
    public int Height { get; }

    /// <summary>按左上角那一格与宽高铺一片地，每一格从同一个状态起步。</summary>
    /// <param name="origin">左上角那一格。</param>
    /// <param name="width">几格宽，至少 1。</param>
    /// <param name="height">几格高，至少 1。</param>
    /// <param name="initial">每一格从哪个状态起步；只许没有作物的那几个（见 <see cref="Plot"/> 的构造）。</param>
    public static PlotGrid Rectangle(GridCell origin, int width, int height, PlotState initial)
    {
        RequireSize(width, height);
        return FromRows(origin, width, height, [.. Enumerable.Range(0, width * height).Select(_ => new Plot(initial))]);
    }

    /// <summary>按左上角那一格与宽高，把一串地块从上到下、每行从左到右摆进去。</summary>
    /// <remarks>读档用：地块是按存档里记的样子恢复出来的。块数要正好是宽乘高。</remarks>
    public static PlotGrid FromRows(GridCell origin, int width, int height, IReadOnlyList<Plot> rowByRow)
    {
        ArgumentNullException.ThrowIfNull(rowByRow);
        RequireSize(width, height);
        if (rowByRow.Count != width * height)
        {
            throw new ArgumentException(
                $"一片 {width}×{height} 的地要 {width * height} 格，交进来 {rowByRow.Count} 格", nameof(rowByRow));
        }

        var plots = new Dictionary<GridCell, Plot>();
        for (var i = 0; i < rowByRow.Count; i++)
        {
            plots[new GridCell(origin.X + (i % width), origin.Y + (i / width))] = rowByRow[i];
        }

        return new PlotGrid(origin, width, height, plots);
    }

    /// <summary>这一片地有几格。</summary>
    public int Count => _plots.Count;

    /// <summary>全部格子，从上到下、每行从左到右排好。</summary>
    /// <remarks>排好是为了每次列出来的先后都一样：打日志、写存档与比两片地都靠它，而字典本身不保证先后。</remarks>
    public IEnumerable<(GridCell Cell, Plot Plot)> Cells =>
        _plots.OrderBy(p => p.Key.Y).ThenBy(p => p.Key.X).Select(p => (p.Key, p.Value));

    /// <summary>这一格在不在这一片地里，在的话是哪一块地。</summary>
    public bool TryGet(GridCell cell, [MaybeNullWhen(false)] out Plot plot) => _plots.TryGetValue(cell, out plot);

    /// <summary>整份复制：每一格各复制一份，与原件不共用任何一块地。</summary>
    public PlotGrid Clone() => new(Origin, Width, Height, _plots.ToDictionary(p => p.Key, p => p.Value.Clone()));

    private static void RequireSize(int width, int height)
    {
        if (width < 1 || height < 1)
        {
            throw new ArgumentOutOfRangeException(
                width < 1 ? nameof(width) : nameof(height), $"一片地两个方向都要至少 1 格，实际 {width}×{height}");
        }
    }
}
