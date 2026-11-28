using Tinderhearth.Rules.Economy;
using Tinderhearth.Rules.Foundation.Actors;

namespace Tinderhearth.Rules.Combat;

/// <summary>一个人拿着一笔行动力能走到哪几格、每格最少花多少、走过去是哪一条路。</summary>
/// <remarks>
/// 规矩在工作区根 desc/战斗规则.md 的「格子」与「行动力」：八个方向都能走，斜着也算一格；两个挡路的东西斜着挨着时，
/// 不能从中间斜穿；同一边的人能穿过去、不能停在那一格；墙与对面的人挡路；这一步走之前和走之后都挨着敌人时多花几点。
/// 走一格花几点在配置里（<see cref="MovementConfig"/>）。
///
/// 做法照开源的格子战棋（GDQuest 的 2D 战棋移动演示）：先从起点按花费从小到大铺开，铺到的就是走得到的，再沿铺开时
/// 记下的上一格倒回去取路。铺开的先后定死（八个方向的次序固定，花费一样时先进先出，只在严格更省时才换上一格），
/// 所以同一局面每次取的是同一条路，与站着的人是按什么次序交进来的无关。
///
/// 这里只算，不挪人：谁真的走过去、行动力扣掉多少，归一场仗的状态。
/// </remarks>
public sealed class MovementMap
{
    /// <summary>八个方向：先上右下左，再四个斜角。</summary>
    private static readonly (int Dx, int Dy)[] Steps =
        [(0, -1), (1, 0), (0, 1), (-1, 0), (1, -1), (1, 1), (-1, 1), (-1, -1)];

    private readonly Dictionary<GridCell, GridCell> _cameFrom;

    private MovementMap(GridCell start, int budget, IReadOnlyDictionary<GridCell, int> reachable, Dictionary<GridCell, GridCell> cameFrom)
    {
        Start = start;
        Budget = budget;
        Reachable = reachable;
        _cameFrom = cameFrom;
    }

    /// <summary>从哪一格出发。</summary>
    public GridCell Start { get; }

    /// <summary>拿着多少行动力。</summary>
    public int Budget { get; }

    /// <summary>走得到、停得下的每一格与最少花多少。不含起点。</summary>
    public IReadOnlyDictionary<GridCell, int> Reachable { get; }

    /// <summary>走到这一格最少花多少；走不到、停不下的是 <c>null</c>。</summary>
    public int? CostTo(GridCell cell) => Reachable.TryGetValue(cell, out var cost) ? cost : null;

    /// <summary>走到这一格的那一条路，不含起点、含终点；走不到、停不下的是 <c>null</c>。</summary>
    public IReadOnlyList<GridCell>? PathTo(GridCell cell)
    {
        if (!Reachable.ContainsKey(cell))
        {
            return null;
        }

        var path = new List<GridCell>();
        for (var at = cell; at != Start; at = _cameFrom[at])
        {
            path.Add(at);
        }

        path.Reverse();
        return path;
    }

    /// <summary>铺开一次。</summary>
    /// <param name="room">那一间屋。</param>
    /// <param name="standing">谁站在哪一格、是哪一边的。走的那个人自己在不在里面都行。</param>
    /// <param name="start">从哪一格出发，要是地面。</param>
    /// <param name="side">走的那个人是哪一边的：我方或敌方。</param>
    /// <param name="budget">拿着多少行动力，不得为负。</param>
    /// <param name="costs">走一格花几点。</param>
    public static MovementMap Compute(
        BattleRoom room,
        IReadOnlyDictionary<GridCell, CombatSide> standing,
        GridCell start,
        CombatSide side,
        int budget,
        MovementConfig costs)
    {
        ArgumentNullException.ThrowIfNull(room);
        ArgumentNullException.ThrowIfNull(standing);
        ArgumentNullException.ThrowIfNull(costs);
        if (!room.IsFloor(start))
        {
            throw new ArgumentException($"出发的那一格 {start} 不是地面", nameof(start));
        }

        if (side == CombatSide.Neutral)
        {
            throw new ArgumentException("走的那个人只能是我方或敌方", nameof(side));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(budget);

        var opposite = side == CombatSide.Ally ? CombatSide.Enemy : CombatSide.Ally;

        bool Blocks(GridCell cell) =>
            !room.IsFloor(cell) || (cell != start && standing.TryGetValue(cell, out var who) && who != side);

        bool BesideEnemy(GridCell cell) =>
            Steps.Any(s => standing.TryGetValue(new GridCell(cell.X + s.Dx, cell.Y + s.Dy), out var who) && who == opposite);

        var cost = new Dictionary<GridCell, int> { [start] = 0 };
        var cameFrom = new Dictionary<GridCell, GridCell>();
        var queue = new PriorityQueue<GridCell, (int Cost, long Order)>();
        long order = 0;
        queue.Enqueue(start, (0, order++));

        while (queue.TryDequeue(out var cell, out var priority))
        {
            if (priority.Cost > cost[cell])
            {
                continue;
            }

            foreach (var (dx, dy) in Steps)
            {
                var next = new GridCell(cell.X + dx, cell.Y + dy);
                if (Blocks(next))
                {
                    continue;
                }

                if (dx != 0 && dy != 0 && Blocks(new GridCell(cell.X + dx, cell.Y)) && Blocks(new GridCell(cell.X, cell.Y + dy)))
                {
                    continue;
                }

                var step = costs.StepCost + (BesideEnemy(cell) && BesideEnemy(next) ? costs.ExtraBesideEnemy : 0);
                var total = cost[cell] + step;
                if (total > budget || (cost.TryGetValue(next, out var known) && total >= known))
                {
                    continue;
                }

                cost[next] = total;
                cameFrom[next] = cell;
                queue.Enqueue(next, (total, order++));
            }
        }

        // 停得下：不是起点，也没有站着人。同一边的人那一格只许穿过去。
        var reachable = cost
            .Where(c => c.Key != start && !standing.ContainsKey(c.Key))
            .ToDictionary(c => c.Key, c => c.Value);
        return new MovementMap(start, budget, reachable, cameFrom);
    }
}
