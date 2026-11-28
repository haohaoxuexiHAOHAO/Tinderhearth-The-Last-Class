using Tinderhearth.Rules.UI;

namespace Tinderhearth.Rules.Economy;

/// <summary>
/// 背包页此刻按方向键怎么走：焦点停在页上哪一格时按一下，走到页上哪一格。背包变了、筛选换了、拿起或放下了，都重算一份。
/// </summary>
/// <remarks>
/// 每按一下，指着的东西换一样：没拿东西时一堆占几格都只停一下，一格空地停一下；拿着一堆时，拿起来的那一块里每一格各停一下，
/// 别的那几块照旧一块停一下。被筛掉的格走不到。先找同一行（列）上的，被筛掉的跳过去接着找；这一行（列）上没有了才去那一边
/// 最近的那一样；那一边一样都没有就是走出网格。为什么这样，在设计仓 design/界面系统.md 的「游戏菜单上：背包」一节。
///
/// 不用 Godot 自带的找法。实测：格子挨着不留缝时，正旁边那一格被筛掉，引擎就走到斜对角那一格（它按两格之间的空隙量远近，
/// 挨着的都是零）；大东西的几格又各是一个焦点，在一块里按一下走到同一块的另一格，框不动。
/// </remarks>
public sealed class BagPageFocus
{
    /// <summary>去那一边最近的那一样时，顺着方向差一段比横着差一段远几倍（都先平方）：取 Android 找焦点那一处（FocusFinder）的数。</summary>
    /// <remarks>它让「再往前一列、只偏一行」赢过「就在下一列、却偏出好几行」：按的是右，走到的就该主要是右边。</remarks>
    private const int AlongWeight = 13;

    private readonly int _columns;
    private readonly int _rows;
    private readonly IReadOnlyList<(int Column, int Row)> _positions;
    private readonly IReadOnlyList<bool> _shown;
    private readonly IReadOnlyList<Area> _areas;
    private readonly Dictionary<(int Column, int Row), int> _indexAt;

    private BagPageFocus(
        int columns, int rows, IReadOnlyList<(int Column, int Row)> positions, IReadOnlyList<bool> shown, IReadOnlyList<Area> areas)
    {
        _columns = columns;
        _rows = rows;
        _positions = positions;
        _shown = shown;
        _areas = areas;
        _indexAt = positions.Select((at, index) => (at, index)).ToDictionary(p => p.at, p => p.index);
    }

    /// <summary>照背包、当前筛选与手上那一堆算一份。</summary>
    /// <param name="bag">背包。</param>
    /// <param name="controls">页上的控件：哪几格被筛掉了看它。</param>
    /// <param name="held">手上那一堆是从页上第几格拿起的（与 <see cref="BagControls.PlaceOf"/> 同一个号）；没拿东西是 <c>null</c>。</param>
    public static BagPageFocus Of(Bag bag, BagControls controls, int? held)
    {
        ArgumentNullException.ThrowIfNull(bag);
        ArgumentNullException.ThrowIfNull(controls);

        int? heldBlock = null;
        if (held is { } grabbed)
        {
            heldBlock = BagPage.AnchorIndexOf(bag, grabbed)
                ?? throw new ArgumentException($"页上第 {grabbed} 格空着，手上不可能拿着它", nameof(held));
        }

        var blocks = BagPage.BlocksOf(bag).ToDictionary(b => b.Index);
        var count = bag.CarryCount + bag.SlotCount;
        var positions = new (int Column, int Row)[count];
        var shown = new bool[count];
        var areas = new Area[count];
        for (var i = 0; i < count; i++)
        {
            positions[i] = BagPage.PositionOf(bag, i);
            shown[i] = controls.Shows(bag, i);

            // 被一堆盖着的格算那一堆；拿起来的那一堆例外，它的每一格各算一样：指着哪一格，放下的地方就差那么一格。
            areas[i] = BagPage.AnchorIndexOf(bag, i) is { } anchor && anchor != heldBlock
                ? Area.Of(blocks[anchor])
                : Area.Cell(positions[i]);
        }

        var rows = BagPageLayout.PageRows(bag.GridHeight, bag.CarryCount, bag.GridWidth);
        return new BagPageFocus(bag.GridWidth, rows, positions, shown, areas);
    }

    /// <summary>
    /// 焦点停在页上第 <paramref name="from"/> 格时往 <paramref name="direction"/> 按一下，走到页上第几格；
    /// 那一边网格里一样都没有了是 <c>null</c>，由页面接着往网格外面走。
    /// </summary>
    public int? Next(int from, FocusDirection direction)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(from);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(from, _shown.Count);
        if (!_shown[from])
        {
            throw new InvalidOperationException($"页上第 {from} 格被当前筛选筛掉了 —— 焦点停不上去，也就不会从它出发");
        }

        var source = _areas[from];
        var at = _positions[from];

        // 同一行（列）上越过现在这一样的那一段：第一个没被筛掉的格就是下一样，落在它对着这一行（列）的那一格上。
        foreach (var position in Line(source, at, direction))
        {
            if (_indexAt.TryGetValue(position, out var index) && _shown[index])
            {
                return index;
            }
        }

        // 这一行（列）上没有了：那一边最近的那一样。中线按半格量才落在整数上，所以顺着的那一段也乘 2 再平方。
        int? best = null;
        var bestDistance = int.MaxValue;
        for (var i = 0; i < _areas.Count; i++)
        {
            if (!_shown[i] || Gap(source, _areas[i], direction) is not { } gap)
            {
                continue;
            }

            var along = 2 * gap;
            var across = Across(_areas[i], at, direction);
            var distance = (AlongWeight * along * along) + (across * across);
            var landing = Landing(_areas[i], at, direction);
            if (distance < bestDistance || (distance == bestDistance && landing < best))
            {
                best = landing;
                bestDistance = distance;
            }
        }

        return best;
    }

    /// <summary>
    /// 焦点停在页上第 <paramref name="from"/> 格时，现在这一样朝 <paramref name="direction"/> 那一边的边上、对着焦点那一行（列）的那一格。
    /// 一格大的就是它自己。
    /// </summary>
    /// <remarks>那一边网格里没有了、要往网格外面找时，从这一格找：从一块大东西中间那一格往外找，先碰上的是同一块里的另一格。</remarks>
    public int EdgeToward(int from, FocusDirection direction)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(from);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(from, _areas.Count);

        var source = _areas[from];
        var at = _positions[from];
        return _indexAt[direction switch
        {
            FocusDirection.Left => (source.Left, at.Row),
            FocusDirection.Right => (source.Right - 1, at.Row),
            FocusDirection.Up => (at.Column, source.Top),
            FocusDirection.Down => (at.Column, source.Bottom - 1),
            _ => throw new ArgumentOutOfRangeException(nameof(direction), direction, "方向键只有上下左右"),
        }];
    }

    /// <summary>同一行（列）上、越过现在这一样的那一段格位，由近到远；有的格位上没有格（随身栏那一排不满一行时）。</summary>
    private IEnumerable<(int Column, int Row)> Line(Area source, (int Column, int Row) at, FocusDirection direction) =>
        direction switch
        {
            FocusDirection.Left => Steps(source.Left - 1, -1).Select(column => (column, at.Row)),
            FocusDirection.Right => Steps(source.Right, _columns).Select(column => (column, at.Row)),
            FocusDirection.Up => Steps(source.Top - 1, -1).Select(row => (at.Column, row)),
            FocusDirection.Down => Steps(source.Bottom, _rows).Select(row => (at.Column, row)),
            _ => throw new ArgumentOutOfRangeException(nameof(direction), direction, "方向键只有上下左右"),
        };

    /// <summary>从 <paramref name="first"/> 一格一格数到 <paramref name="end"/> 之前，往哪边数看两者谁大。</summary>
    private static IEnumerable<int> Steps(int first, int end)
    {
        var step = end >= first ? 1 : -1;
        for (var i = first; i != end; i += step)
        {
            yield return i;
        }
    }

    /// <summary>那一样整个在那一边时，离现在这一样隔几格；不整个在那一边是 <c>null</c>。</summary>
    private static int? Gap(Area source, Area area, FocusDirection direction) => direction switch
    {
        FocusDirection.Left when area.Right <= source.Left => source.Left - area.Right,
        FocusDirection.Right when area.Left >= source.Right => area.Left - source.Right,
        FocusDirection.Up when area.Bottom <= source.Top => source.Top - area.Bottom,
        FocusDirection.Down when area.Top >= source.Bottom => area.Top - source.Bottom,
        _ => null,
    };

    /// <summary>那一样的中线离焦点那一格的中线横着差多少，按半格计。</summary>
    private static int Across(Area area, (int Column, int Row) at, FocusDirection direction) =>
        direction is FocusDirection.Left or FocusDirection.Right
            ? Math.Abs(area.Top + area.Bottom - ((2 * at.Row) + 1))
            : Math.Abs(area.Left + area.Right - ((2 * at.Column) + 1));

    /// <summary>走进那一样时落在哪一格：它朝这边的那一列（行），尽量对着焦点原来那一行（列）。</summary>
    private int Landing(Area area, (int Column, int Row) at, FocusDirection direction)
    {
        var row = Math.Clamp(at.Row, area.Top, area.Bottom - 1);
        var column = Math.Clamp(at.Column, area.Left, area.Right - 1);
        return _indexAt[direction switch
        {
            FocusDirection.Left => (area.Right - 1, row),
            FocusDirection.Right => (area.Left, row),
            FocusDirection.Up => (column, area.Bottom - 1),
            FocusDirection.Down => (column, area.Top),
            _ => throw new ArgumentOutOfRangeException(nameof(direction), direction, "方向键只有上下左右"),
        }];
    }

    /// <summary>页上一样东西占的那一片：左上角那一格的列与行，加宽与高，都以格计。</summary>
    private readonly record struct Area(int Left, int Top, int Width, int Height)
    {
        /// <summary>右边那一列再往右一列，不算在里面。</summary>
        public int Right => Left + Width;

        /// <summary>下边那一行再往下一行，不算在里面。</summary>
        public int Bottom => Top + Height;

        public static Area Of(BagBlock block) => new(block.Column, block.Row, block.Width, block.Height);

        public static Area Cell((int Column, int Row) at) => new(at.Column, at.Row, 1, 1);
    }
}
