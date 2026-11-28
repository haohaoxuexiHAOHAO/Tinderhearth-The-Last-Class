using Tinderhearth.Rules.Combat;
using Tinderhearth.Rules.Economy;
using Tinderhearth.Rules.Foundation.Actors;
using Tinderhearth.Rules.Progression;
using Xunit;

namespace Tinderhearth.Rules.Tests.Combat;

/// <summary>倒下与胜负：HP 到 0 的我方倒下、还占着那一格，敌人死亡、从格子上拿掉；胜负一出，不再有人行动。</summary>
/// <remarks>每个人一下就能打倒对面的人：HP 100、攻击 1000、防御 0，随机源总给 0，所以总打中、从不暴击（暴击几率 0）。</remarks>
public class BattleOutcomeTests
{
    private static readonly CombatConfig OneHit = TestBattles.Config(
        panel: TestBattles.FlatPanel(hp: 100, attack: 1000, defense: 0, hitRate: 1, dodgeRate: 0, critChance: 0, critMultiplier: 1));

    [Fact]
    public void 学生HP到0倒下_还占着那一格_下一轮不排进出手顺序()
    {
        var battle = Squad();
        var (jin, first, second, near) = (Get(battle, "jin"), Get(battle, "xuesheng"), Get(battle, "xuesheng2"), Get(battle, "diren1"));
        EndTurns(battle, 3);
        Assert.Same(near, battle.Current);

        battle.UseSkill(near.BasicAttack, first);

        Assert.Equal(0, first.Hp);
        Assert.True(first.IsDown);
        Assert.True(first.OccupiesCell);
        Assert.Equal(new GridCell(0, 2), first.Cell);
        Assert.Null(battle.Outcome);

        EndTurns(battle, 2);
        Assert.Equal(2, battle.Round);
        Assert.DoesNotContain(first, battle.Order);
        Assert.Same(jin, battle.Current);
        Assert.DoesNotContain(first.Cell, battle.Reach().WithRemaining);   // 同一边的人那一格只许穿过去
        EndTurns(battle, 1);
        Assert.Same(second, battle.Current);
    }

    [Fact]
    public void 敌人HP到0死亡_从格子上拿掉_这一轮还没轮到他的也跳过()
    {
        var battle = Squad();
        var (jin, near, far) = (battle.Current, Get(battle, "diren1"), Get(battle, "diren2"));

        battle.UseSkill(jin.BasicAttack, near);

        Assert.True(near.IsDown);
        Assert.False(near.OccupiesCell);
        Assert.Null(battle.Outcome);
        Assert.Contains(near.Cell, battle.Reach().WithRemaining);

        EndTurns(battle, 3);
        Assert.Same(far, battle.Current);
    }

    [Fact]
    public void 敌人全部死亡判胜利_胜负一出不再有人行动()
    {
        var battle = Duel();
        var (jin, student, enemy) = (battle.Current, Get(battle, "xuesheng"), Get(battle, "diren1"));

        battle.UseSkill(jin.BasicAttack, enemy);

        Assert.Equal(BattleOutcome.Victory, battle.Outcome);
        Assert.Null(battle.MoveTo(new GridCell(1, 0)));
        Assert.Equal(new GridCell(0, 0), jin.Cell);
        Assert.Equal(SkillRefusal.BattleOver, battle.UseSkill(jin.Defend, jin).Refusal);
        Assert.False(battle.TakeOver(student));
        battle.EndTurn();
        Assert.Same(jin, battle.Current);
        Assert.Empty(BattleAi.PlayTurn(battle));
    }

    [Fact]
    public void 烬倒下判失败()
    {
        var battle = Duel();
        var jin = battle.Current;
        EndTurns(battle, 2);
        var enemy = battle.Current;

        battle.UseSkill(enemy.BasicAttack, jin);

        Assert.True(jin.IsDown);
        Assert.Equal(BattleOutcome.Defeat, battle.Outcome);
        battle.EndTurn();
        Assert.Same(enemy, battle.Current);
    }

    [Fact]
    public void 学生全部倒下才判失败_还剩一个站着就接着打()
    {
        var battle = Squad();
        var (first, second, near) = (Get(battle, "xuesheng"), Get(battle, "xuesheng2"), Get(battle, "diren1"));
        EndTurns(battle, 3);
        battle.UseSkill(near.BasicAttack, first);
        Assert.Null(battle.Outcome);

        EndTurns(battle, 4);
        Assert.Same(near, battle.Current);
        battle.UseSkill(near.BasicAttack, second);

        Assert.Equal(BattleOutcome.Defeat, battle.Outcome);
    }

    [Fact]
    public void 倒下的人选不中_瞄准时也不写数()
    {
        var battle = Squad();
        var (first, near) = (Get(battle, "xuesheng"), Get(battle, "diren1"));
        EndTurns(battle, 3);
        battle.UseSkill(near.BasicAttack, first);

        EndTurns(battle, 4);
        Assert.Same(near, battle.Current);

        Assert.Equal(SkillRefusal.WrongTarget, battle.RefusalFor(near.BasicAttack, first));
        Assert.Null(battle.Preview(near.BasicAttack, first));
    }

    [Fact]
    public void HP上限算出来是0的人上不了场()
    {
        var config = TestBattles.Config(panel: TestBattles.FlatPanel(hp: 0));

        var error = Assert.Throws<InvalidDataException>(() => TestBattles.Start(
            6,
            3,
            [Jin(new GridCell(0, 0)), Character("xuesheng", 6, new GridCell(0, 2)), Foe("diren1", 5, new GridCell(1, 1))],
            config: config));

        Assert.Contains("HP 上限算出来是 0", error.Message, StringComparison.Ordinal);
    }

    /// <summary>6 × 3：烬 (0,0)、学生 (0,2)、敌人 (1,1) 斜着挨着两人。出手顺序烬、学生、敌人。</summary>
    private static Battle Duel() => TestBattles.Start(
        6,
        3,
        [Jin(new GridCell(0, 0)), Character("xuesheng", 6, new GridCell(0, 2)), Foe("diren1", 5, new GridCell(1, 1))],
        static () => 0.0,
        OneHit);

    /// <summary>
    /// 6 × 4：烬 (0,0)、学生 (0,2)、学生二 (2,0)、敌人甲 (1,1) 挨着他们三个、敌人乙 (5,3) 离得远。
    /// 出手顺序烬、学生、学生二、敌人甲、敌人乙。
    /// </summary>
    private static Battle Squad() => TestBattles.Start(
        6,
        4,
        [
            Jin(new GridCell(0, 0)),
            Character("xuesheng", 6, new GridCell(0, 2)),
            Character("xuesheng2", 5, new GridCell(2, 0)),
            Foe("diren1", 5, new GridCell(1, 1)),
            Foe("diren2", 4, new GridCell(5, 3)),
        ],
        static () => 0.0,
        OneHit);

    private static (CharacterDefinition, CombatSide, GridCell) Jin(GridCell cell) => (TestBattles.Character("jin", agility: 9), CombatSide.Ally, cell);

    private static (CharacterDefinition, CombatSide, GridCell) Character(string id, int agility, GridCell cell) =>
        (TestBattles.Character(id, agility: agility), CombatSide.Ally, cell);

    private static (CharacterDefinition, CombatSide, GridCell) Foe(string id, int agility, GridCell cell) =>
        (TestBattles.Character(id, agility: agility), CombatSide.Enemy, cell);

    private static Combatant Get(Battle battle, string id) => battle.Combatants.Single(c => c.Character.Id == id);

    private static void EndTurns(Battle battle, int count)
    {
        for (var i = 0; i < count; i++)
        {
            battle.EndTurn();
        }
    }
}
