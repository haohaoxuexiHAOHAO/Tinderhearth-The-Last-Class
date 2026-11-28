using Tinderhearth.Rules.Combat;
using Tinderhearth.Rules.Economy;
using Tinderhearth.Rules.Foundation.Actors;
using Tinderhearth.Rules.Progression;
using Xunit;

namespace Tinderhearth.Rules.Tests.Combat;

/// <summary>一场仗：一轮两个阶段与出手顺序，行动力回满，点一格走过去，换成再走满一次，自动结束与结束回合。</summary>
public class BattleTests
{
    private static readonly GridCell JinStart = new(0, 0);

    [Fact]
    public void 我方阶段烬第一个_学生按敏捷从高到低_敌方阶段敌人按敏捷从高到低()
    {
        var battle = Standard();

        Assert.Equal(["jin", "xuesheng2", "xuesheng", "diren2", "diren1"], Ids(battle.Order));
        Assert.Equal(1, battle.Round);
        Assert.Equal(BattlePhase.Ally, battle.Phase);
        Assert.True(battle.Current.IsProtagonist);
    }

    [Fact]
    public void 敏捷一样时按站位在场景里的先后_不掷骰()
    {
        (CharacterDefinition, CombatSide, GridCell)[] positions =
        [
            (TestBattles.Character("jin"), CombatSide.Ally, JinStart),
            (TestBattles.Character("xuesheng"), CombatSide.Ally, new GridCell(0, 2)),
            (TestBattles.Character("xuesheng2"), CombatSide.Ally, new GridCell(0, 4)),
            (TestBattles.Character("diren1"), CombatSide.Enemy, new GridCell(9, 0)),
            (TestBattles.Character("diren1"), CombatSide.Enemy, new GridCell(9, 4)),
        ];

        var inSceneOrder = TestBattles.Start(10, 5, positions);
        var reversed = TestBattles.Start(10, 5, [.. positions.Reverse()]);

        Assert.Equal(["jin", "xuesheng", "xuesheng2", "diren1", "diren1"], Ids(inSceneOrder.Order));
        Assert.Equal([new GridCell(9, 0), new GridCell(9, 4)], inSceneOrder.Order.Skip(3).Select(c => c.Cell));
        Assert.Equal(["jin", "xuesheng2", "xuesheng", "diren1", "diren1"], Ids(reversed.Order));
        Assert.Equal([new GridCell(9, 4), new GridCell(9, 0)], reversed.Order.Skip(3).Select(c => c.Cell));
    }

    [Fact]
    public void 我方的人都过完了换敌方阶段_敌方也过完了开始下一轮_顺序每轮开始排一次()
    {
        var battle = Standard();
        var firstRound = battle.Order;

        EndTurns(battle, 2);
        Assert.Equal(BattlePhase.Ally, battle.Phase);
        Assert.Equal("xuesheng", battle.Current.Character.Id);

        EndTurns(battle, 1);
        Assert.Equal(BattlePhase.Enemy, battle.Phase);
        Assert.Equal("diren2", battle.Current.Character.Id);
        Assert.Equal(firstRound, battle.Order);

        EndTurns(battle, 2);
        Assert.Equal(2, battle.Round);
        Assert.Equal(BattlePhase.Ally, battle.Phase);
        Assert.True(battle.Current.IsProtagonist);
        Assert.Equal(firstRound, battle.Order);
    }

    [Fact]
    public void 自己的回合开始时行动力回满_没用完的不留到下一回合()
    {
        var battle = Standard();
        var jin = battle.Current;

        battle.MoveTo(new GridCell(2, 0));
        Assert.Equal(3, battle.ActionPoints);

        EndTurns(battle, 2);
        Assert.Equal("xuesheng", battle.Current.Character.Id);
        Assert.Equal(4, battle.ActionPoints);

        EndTurns(battle, 3);
        Assert.Same(jin, battle.Current);
        Assert.Equal(5, battle.ActionPoints);
        Assert.False(battle.TurnActionUsed);
    }

    [Fact]
    public void 点一格走得到就沿算出来的那条路走过去_扣掉花的行动力()
    {
        var battle = Standard();
        var jin = battle.Current;
        var target = new GridCell(3, 1);
        var expected = MovementMap.Compute(battle.Room, StandardStanding(), JinStart, CombatSide.Ally, 5, TestBattles.Costs).PathTo(target);

        var path = battle.MoveTo(target);

        Assert.NotNull(expected);
        Assert.Equal(expected, path);
        Assert.Equal(target, jin.Cell);
        Assert.Equal(2, battle.ActionPoints);
        Assert.False(battle.TurnActionUsed);
        Assert.Same(jin, battle.Current);
    }

    [Fact]
    public void 贴着敌人走那一步多花_扣的照多花的算()
    {
        var battle = TestBattles.Start(
            6,
            3,
            [
                (TestBattles.Character("jin"), CombatSide.Ally, JinStart),
                (TestBattles.Character("xuesheng"), CombatSide.Ally, new GridCell(0, 2)),
                (TestBattles.Character("diren1"), CombatSide.Enemy, new GridCell(1, 1)),
            ]);

        Assert.Equal(2, battle.Reach().CostTo(new GridCell(1, 0)));

        battle.MoveTo(new GridCell(1, 0));

        Assert.Equal(3, battle.ActionPoints);
    }

    [Theory]
    [InlineData(13, 6)]   // 换成再走满一次也够不着
    [InlineData(0, 2)]    // 同伴站着的那一格
    [InlineData(12, 0)]   // 敌人站着的那一格
    [InlineData(0, 0)]    // 自己脚下那一格
    [InlineData(-1, 0)]   // 屋子外面
    public void 走不到的点了不动_行动力不扣_那一件也不用掉(int x, int y)
    {
        var battle = Standard();
        var jin = battle.Current;

        Assert.Null(battle.MoveTo(new GridCell(x, y)));

        Assert.Equal(JinStart, jin.Cell);
        Assert.Equal(5, battle.ActionPoints);
        Assert.False(battle.TurnActionUsed);
        Assert.Same(jin, battle.Current);
    }

    [Fact]
    public void 算得出用剩下的行动力走得到的格子_与换成再走满一次才走得到的格子()
    {
        var battle = Standard();
        var withFive = MovementMap.Compute(battle.Room, StandardStanding(), JinStart, CombatSide.Ally, 5, TestBattles.Costs);
        var withTen = MovementMap.Compute(battle.Room, StandardStanding(), JinStart, CombatSide.Ally, 10, TestBattles.Costs);

        var reach = battle.Reach();

        Assert.Equal(Sorted(withFive.Reachable.Keys), Sorted(reach.WithRemaining));
        Assert.Equal(Sorted(withTen.Reachable.Keys.Except(withFive.Reachable.Keys)), Sorted(reach.OnlyByConverting));
        Assert.Empty(reach.WithRemaining.Intersect(reach.OnlyByConverting));
        Assert.Contains(new GridCell(5, 0), reach.WithRemaining);
        Assert.Contains(new GridCell(6, 0), reach.OnlyByConverting);
        Assert.Equal(6, reach.CostTo(new GridCell(6, 0)));
        Assert.Equal(withTen.PathTo(new GridCell(6, 0)), reach.PathTo(new GridCell(6, 0)));
        Assert.Null(reach.PathTo(new GridCell(13, 6)));
    }

    [Fact]
    public void 行动力用完_还没做占回合的事时_可以换成再走满一次()
    {
        var battle = Standard();
        var jin = battle.Current;

        battle.MoveTo(new GridCell(5, 0));

        Assert.Equal(0, battle.ActionPoints);
        Assert.False(battle.TurnActionUsed);
        Assert.Same(jin, battle.Current);
        var reach = battle.Reach();
        Assert.Empty(reach.WithRemaining);
        Assert.Contains(new GridCell(10, 0), reach.OnlyByConverting);

        Assert.NotNull(battle.MoveTo(new GridCell(8, 0)));

        Assert.Equal(new GridCell(8, 0), jin.Cell);
        Assert.Equal(2, battle.ActionPoints);
        Assert.True(battle.TurnActionUsed);
        Assert.Empty(battle.Reach().OnlyByConverting);
    }

    [Fact]
    public void 行动力没用完时点换了才走得到的格子_换了再走_剩下的接着走()
    {
        var battle = Standard();
        var jin = battle.Current;

        battle.MoveTo(new GridCell(7, 0));

        Assert.Equal(new GridCell(7, 0), jin.Cell);
        Assert.Equal(3, battle.ActionPoints);
        Assert.True(battle.TurnActionUsed);
        Assert.Same(jin, battle.Current);

        Assert.NotNull(battle.MoveTo(new GridCell(9, 0)));
        Assert.Equal(1, battle.ActionPoints);
    }

    [Fact]
    public void 行动力用完_那一件也用掉了_这一回合自动结束()
    {
        var battle = Standard();
        var jin = battle.Current;

        battle.MoveTo(new GridCell(10, 0));

        Assert.Equal(new GridCell(10, 0), jin.Cell);
        Assert.Equal("xuesheng2", battle.Current.Character.Id);
        Assert.Equal(5, battle.ActionPoints);
        Assert.False(battle.TurnActionUsed);
    }

    [Fact]
    public void 点结束回合也结束_剩下的行动力作废()
    {
        var battle = Standard();
        battle.MoveTo(new GridCell(1, 0));

        battle.EndTurn();

        Assert.Equal("xuesheng2", battle.Current.Character.Id);
        Assert.Equal(5, battle.ActionPoints);
        Assert.False(battle.TurnActionUsed);
    }

    [Fact]
    public void 同伴那一格穿得过去_敌方阶段的人把我方当挡路的()
    {
        // 一格宽的走廊：学生在 (0,0)，烬在 (2,0)，敌人在 (5,0)。
        var battle = TestBattles.Start(
            6,
            1,
            [
                (TestBattles.Character("jin"), CombatSide.Ally, new GridCell(2, 0)),
                (TestBattles.Character("xuesheng"), CombatSide.Ally, new GridCell(0, 0)),
                (TestBattles.Character("diren1"), CombatSide.Enemy, new GridCell(5, 0)),
            ]);

        battle.EndTurn();
        Assert.Equal("xuesheng", battle.Current.Character.Id);
        Assert.Equal([new GridCell(1, 0), new GridCell(3, 0), new GridCell(4, 0)], Sorted(battle.Reach().WithRemaining));

        battle.EndTurn();
        Assert.Equal(BattlePhase.Enemy, battle.Phase);
        var enemyReach = battle.Reach();
        Assert.Equal([new GridCell(3, 0), new GridCell(4, 0)], Sorted(enemyReach.WithRemaining));
        Assert.Empty(enemyReach.OnlyByConverting);
    }

    [Fact]
    public void 屋子里的角色不在打仗那份内容里_开打被拒并点名()
    {
        var characters = new[] { TestBattles.Character("jin"), TestBattles.Character("xuesheng"), TestBattles.Character("diren1") }
            .ToDictionary(c => c.Id);
        StartingPosition[] positions =
        [
            new(JinStart, "jin", CombatSide.Ally),
            new(new GridCell(1, 0), "xuesheng", CombatSide.Ally),
            new(new GridCell(3, 0), "diren1", CombatSide.Enemy),
        ];
        var room = BattleRoom.Create(TestBattles.Floor(4, 1), [], positions, characters);
        var content = TestBattles.Content(characters.Where(c => c.Key != "diren1").ToDictionary(c => c.Key, c => c.Value));

        var error = Assert.Throws<ArgumentException>(() => Battle.Start(room, content, TestBattles.NoRolls));

        Assert.Contains("diren1", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// 14 × 7 的一间屋，照这个先后摆：烬 (0,0) 敏捷 1；学生 (0,2) 敏捷 4、行动力 4；敌人甲 (12,0) 敏捷 2；
    /// 学生二 (0,4) 敏捷 7；敌人乙 (12,4) 敏捷 6。其余行动力都是 5。
    /// </summary>
    private static Battle Standard() => TestBattles.Start(
        14,
        7,
        [
            (TestBattles.Character("jin", agility: 1), CombatSide.Ally, JinStart),
            (TestBattles.Character("xuesheng", agility: 4, actionPoints: 4), CombatSide.Ally, new GridCell(0, 2)),
            (TestBattles.Character("diren1", agility: 2), CombatSide.Enemy, new GridCell(12, 0)),
            (TestBattles.Character("xuesheng2", agility: 7), CombatSide.Ally, new GridCell(0, 4)),
            (TestBattles.Character("diren2", agility: 6), CombatSide.Enemy, new GridCell(12, 4)),
        ]);

    /// <summary><see cref="Standard"/> 开打时谁站在哪一格、是哪一边的。</summary>
    private static Dictionary<GridCell, CombatSide> StandardStanding() => new()
    {
        [JinStart] = CombatSide.Ally,
        [new GridCell(0, 2)] = CombatSide.Ally,
        [new GridCell(12, 0)] = CombatSide.Enemy,
        [new GridCell(0, 4)] = CombatSide.Ally,
        [new GridCell(12, 4)] = CombatSide.Enemy,
    };

    private static void EndTurns(Battle battle, int count)
    {
        for (var i = 0; i < count; i++)
        {
            battle.EndTurn();
        }
    }

    private static string[] Ids(IEnumerable<Combatant> combatants) => [.. combatants.Select(c => c.Character.Id)];

    private static GridCell[] Sorted(IEnumerable<GridCell> cells) => [.. cells.OrderBy(c => c.Y).ThenBy(c => c.X)];
}
