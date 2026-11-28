using Tinderhearth.Rules.Combat;
using Tinderhearth.Rules.Economy;
using Tinderhearth.Rules.Foundation.Actors;
using Tinderhearth.Rules.Progression;
using Tinderhearth.Rules.Tests.Progression;
using Xunit;

namespace Tinderhearth.Rules.Tests.Combat;

/// <summary>走格子：八向、不许斜穿、同伴可穿不可停、贴着敌人多花、找路每次取同一条。</summary>
public class MovementMapTests
{
    private static readonly MovementConfig Costs = new() { StepCost = 1, ExtraBesideEnemy = 1 };

    private static readonly GridCell Here = new(5, 5);

    [Fact]
    public void 八个方向都能走_斜着也是一格()
    {
        var map = Compute(Room(), [], Here, budget: 1);

        Assert.Equal(8, map.Reachable.Count);
        Assert.All(map.Reachable.Values, cost => Assert.Equal(1, cost));
        Assert.Equal(1, map.CostTo(new GridCell(6, 6)));
    }

    [Fact]
    public void 行动力花完的那一圈之外走不到()
    {
        var map = Compute(Room(), [], Here, budget: 2);

        Assert.Equal(24, map.Reachable.Count);
        Assert.Null(map.CostTo(new GridCell(8, 5)));
        Assert.Null(map.PathTo(new GridCell(8, 5)));
        Assert.Null(map.CostTo(Here));
    }

    [Fact]
    public void 两面墙斜着挨着时不能从中间斜穿()
    {
        var map = Compute(Room(walls: [new(6, 5), new(5, 6)]), [], Here, budget: 4);

        Assert.True(map.CostTo(new GridCell(6, 6)) > 1);
    }

    [Fact]
    public void 只有一边挡着时照样斜着走()
    {
        var map = Compute(Room(walls: [new(6, 5)]), [], Here, budget: 1);

        Assert.Equal(1, map.CostTo(new GridCell(6, 6)));
    }

    [Fact]
    public void 墙与对面的人斜着挨着也不能斜穿_同伴不算挡路()
    {
        var room = Room(walls: [new(5, 6)]);

        var besideEnemy = Compute(room, [(new GridCell(6, 5), CombatSide.Enemy)], Here, budget: 6);
        var besideAlly = Compute(room, [(new GridCell(6, 5), CombatSide.Ally)], Here, budget: 1);

        Assert.True(besideEnemy.CostTo(new GridCell(6, 6)) > 1);
        Assert.Equal(1, besideAlly.CostTo(new GridCell(6, 6)));
    }

    [Fact]
    public void 能从同伴那一格穿过去_不能停在那一格()
    {
        var map = Compute(Corridor(), [(new GridCell(1, 0), CombatSide.Ally)], new GridCell(0, 0), budget: 3);

        Assert.Null(map.CostTo(new GridCell(1, 0)));
        Assert.Equal(2, map.CostTo(new GridCell(2, 0)));
        Assert.Equal([new GridCell(1, 0), new GridCell(2, 0)], map.PathTo(new GridCell(2, 0)));
    }

    [Fact]
    public void 墙与对面的人挡路()
    {
        var byEnemy = Compute(Corridor(), [(new GridCell(1, 0), CombatSide.Enemy)], new GridCell(0, 0), budget: 5);
        var byWall = Compute(Corridor(walls: [new(2, 0)]), [], new GridCell(0, 0), budget: 5);

        Assert.Empty(byEnemy.Reachable);
        Assert.Equal([new GridCell(1, 0)], byWall.Reachable.Keys);
    }

    [Fact]
    public void 敌人那边走的人把我方当成挡路的()
    {
        var map = Compute(Corridor(), [(new GridCell(1, 0), CombatSide.Ally)], new GridCell(0, 0), budget: 5, side: CombatSide.Enemy);

        Assert.Empty(map.Reachable);
    }

    [Fact]
    public void 走之前和走之后都挨着敌人那一步花2点_挨着几个都只多1点()
    {
        var oneEnemy = Compute(Room(), [(new GridCell(6, 6), CombatSide.Enemy)], Here, budget: 4);
        var twoEnemies = Compute(Room(), [(new GridCell(6, 6), CombatSide.Enemy), (new GridCell(4, 6), CombatSide.Enemy)], Here, budget: 4);

        Assert.Equal(2, oneEnemy.CostTo(new GridCell(6, 5)));   // 起点与终点都挨着
        Assert.Equal(1, oneEnemy.CostTo(new GridCell(4, 4)));   // 终点不挨着
        Assert.Equal(2, twoEnemies.CostTo(new GridCell(5, 6)));
    }

    [Fact]
    public void 走开再绕回来比贴着走省时取省的那条()
    {
        // 从 (5,5) 到 (7,5)：贴着 (6,6) 的敌人走两步各花 2 点，从上面绕开那条只花 2 点。
        var map = Compute(Room(), [(new GridCell(6, 6), CombatSide.Enemy)], Here, budget: 4);

        Assert.Equal(2, map.CostTo(new GridCell(7, 5)));
        Assert.DoesNotContain(new GridCell(6, 5), map.PathTo(new GridCell(7, 5))!);
    }

    [Fact]
    public void 花得一样少的路有几条时_同一局面每次取同一条_与站着的人交进来的次序无关()
    {
        (GridCell, CombatSide)[] standing =
        [
            (new GridCell(7, 4), CombatSide.Ally),
            (new GridCell(3, 7), CombatSide.Enemy),
            (new GridCell(8, 8), CombatSide.Ally),
        ];
        var target = new GridCell(8, 6);

        var first = Compute(Room(), standing, Here, budget: 4).PathTo(target);
        var again = Compute(Room(), standing, Here, budget: 4).PathTo(target);
        var reversed = Compute(Room(), [.. standing.Reverse()], Here, budget: 4).PathTo(target);

        Assert.NotNull(first);
        Assert.Equal(first, again);
        Assert.Equal(first, reversed);
        Assert.Equal(target, first[^1]);
    }

    [Fact]
    public void 路上每一步都挨着上一步()
    {
        var path = Compute(Room(walls: [new(6, 4), new(6, 5), new(6, 6)]), [], Here, budget: 6).PathTo(new GridCell(7, 5))!;

        var previous = Here;
        foreach (var cell in path)
        {
            Assert.True(Math.Max(Math.Abs(cell.X - previous.X), Math.Abs(cell.Y - previous.Y)) == 1, $"{previous} 到 {cell} 不是一步");
            previous = cell;
        }
    }

    [Fact]
    public void 出发的那一格不是地面_行动力为负_走的人是中立_都当场报错()
    {
        var room = Room();

        Assert.Throws<ArgumentException>(() => MovementMap.Compute(room, new Dictionary<GridCell, CombatSide>(), new GridCell(99, 99), CombatSide.Ally, 1, Costs));
        Assert.Throws<ArgumentOutOfRangeException>(() => MovementMap.Compute(room, new Dictionary<GridCell, CombatSide>(), Here, CombatSide.Ally, -1, Costs));
        Assert.Throws<ArgumentException>(() => MovementMap.Compute(room, new Dictionary<GridCell, CombatSide>(), Here, CombatSide.Neutral, 1, Costs));
    }

    private static MovementMap Compute(BattleRoom room, (GridCell Cell, CombatSide Side)[] standing, GridCell start, int budget, CombatSide side = CombatSide.Ally)
    {
        var map = new Dictionary<GridCell, CombatSide> { [start] = side };
        foreach (var (cell, who) in standing)
        {
            map[cell] = who;
        }

        return MovementMap.Compute(room, map, start, side, budget, Costs);
    }

    /// <summary>11×11 的地面，去掉给定的几格当墙。</summary>
    private static BattleRoom Room(GridCell[]? walls = null) => Make(
        [.. Enumerable.Range(0, 11).SelectMany(x => Enumerable.Range(0, 11).Select(y => new GridCell(x, y)))], walls ?? []);

    /// <summary>一格宽的走廊：第 0 行，第 0 到 5 列。</summary>
    private static BattleRoom Corridor(GridCell[]? walls = null) => Make(
        [.. Enumerable.Range(0, 6).Select(x => new GridCell(x, 0))], walls ?? []);

    private static BattleRoom Make(GridCell[] cells, GridCell[] walls)
    {
        var floor = cells.Except(walls).ToList();
        Dictionary<string, CharacterDefinition> characters = new()
        {
            ["jin"] = CharacterDefinition.Parse(TestCharacters.Json("jin", "character.jin.name"), "测试"),
            ["xuesheng"] = CharacterDefinition.Parse(TestCharacters.Json("xuesheng", "character.xuesheng.name"), "测试"),
            ["diren"] = CharacterDefinition.Parse(TestCharacters.Json("diren", "character.diren.name"), "测试"),
        };
        StartingPosition[] positions =
        [
            new(floor[0], "jin", CombatSide.Ally),
            new(floor[1], "xuesheng", CombatSide.Ally),
            new(floor[2], "diren", CombatSide.Enemy),
        ];
        return BattleRoom.Create(floor, walls, positions, characters);
    }
}
