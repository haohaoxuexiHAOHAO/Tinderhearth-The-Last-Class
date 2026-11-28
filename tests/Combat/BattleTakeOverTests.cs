using Tinderhearth.Rules.Combat;
using Tinderhearth.Rules.Economy;
using Tinderhearth.Rules.Foundation.Actors;
using Tinderhearth.Rules.Progression;
using Xunit;

namespace Tinderhearth.Rules.Tests.Combat;

/// <summary>接管学生：在烬的回合里点一名学生，接管他这一轮；每轮一人，不用掉烬那一件占回合的事，下一轮不点就回到 AI。</summary>
public class BattleTakeOverTests
{
    [Fact]
    public void 在烬的回合里点学生就接管他这一轮_不用掉烬那一件占回合的事()
    {
        var battle = Party();
        var (jin, first) = (battle.Current, Get(battle, "xuesheng"));

        Assert.True(battle.TakeOver(first));

        Assert.Same(first, battle.TakenOver);
        Assert.Same(jin, battle.Current);
        Assert.Equal(5, battle.ActionPoints);
        Assert.False(battle.TurnActionUsed);
    }

    [Fact]
    public void 每轮只能接管一人()
    {
        var battle = Party();
        var (first, second) = (Get(battle, "xuesheng"), Get(battle, "xuesheng2"));

        battle.TakeOver(first);

        Assert.False(battle.TakeOver(second));
        Assert.False(battle.TakeOver(first));
        Assert.Same(first, battle.TakenOver);
    }

    [Fact]
    public void 不在烬的回合里接管不了_烬与敌人也接管不了()
    {
        var battle = Party();
        var (jin, second, enemy) = (battle.Current, Get(battle, "xuesheng2"), Get(battle, "diren1"));

        Assert.False(battle.TakeOver(jin));
        Assert.False(battle.TakeOver(enemy));

        battle.EndTurn();
        Assert.False(battle.TakeOver(second));
        Assert.Null(battle.TakenOver);
    }

    [Fact]
    public void 被接管的学生轮到他时由玩家操作_AI不替他动()
    {
        var battle = Party();
        var first = Get(battle, "xuesheng");
        battle.TakeOver(first);

        battle.EndTurn();

        Assert.Same(first, battle.Current);
        Assert.Throws<InvalidOperationException>(() => BattleAi.PlayTurn(battle));
        Assert.NotNull(battle.MoveTo(new GridCell(1, 2)));
    }

    [Fact]
    public void 接管只管这一轮_下一轮不点就回到AI()
    {
        var battle = Party();
        var first = Get(battle, "xuesheng");
        battle.TakeOver(first);

        do
        {
            battle.EndTurn();
        }
        while (battle.Round == 1);

        Assert.Null(battle.TakenOver);
        battle.EndTurn();
        Assert.Same(first, battle.Current);
        Assert.NotEmpty(BattleAi.PlayTurn(battle));
    }

    [Fact]
    public void 倒下的学生接管不了()
    {
        var oneHit = TestBattles.Config(
            panel: TestBattles.FlatPanel(hp: 100, attack: 1000, defense: 0, hitRate: 1, dodgeRate: 0, critChance: 0, critMultiplier: 1));
        var battle = TestBattles.Start(
            6,
            3,
            [
                (TestBattles.Character("jin", agility: 9), CombatSide.Ally, new GridCell(0, 0)),
                (TestBattles.Character("xuesheng", agility: 6), CombatSide.Ally, new GridCell(0, 2)),
                (TestBattles.Character("xuesheng2", agility: 5), CombatSide.Ally, new GridCell(4, 2)),
                (TestBattles.Character("diren1", agility: 5), CombatSide.Enemy, new GridCell(1, 1)),
            ],
            static () => 0.0,
            oneHit);
        var (first, second, enemy) = (Get(battle, "xuesheng"), Get(battle, "xuesheng2"), Get(battle, "diren1"));
        battle.EndTurn();
        battle.EndTurn();
        battle.EndTurn();
        battle.UseSkill(enemy.BasicAttack, first);
        battle.EndTurn();
        Assert.True(battle.Current.IsProtagonist);

        Assert.False(battle.TakeOver(first));
        Assert.True(battle.TakeOver(second));
    }

    /// <summary>
    /// 8 × 5：烬 (0,0) 敏捷 9、学生 (0,2) 敏捷 6、学生二 (0,4) 敏捷 5，敌人 (7,2) 离得远。出手顺序烬、学生、学生二、敌人。
    /// </summary>
    private static Battle Party() => TestBattles.Start(
        8,
        5,
        [
            (TestBattles.Character("jin", agility: 9), CombatSide.Ally, new GridCell(0, 0)),
            (TestBattles.Character("xuesheng", agility: 6), CombatSide.Ally, new GridCell(0, 2)),
            (TestBattles.Character("xuesheng2", agility: 5), CombatSide.Ally, new GridCell(0, 4)),
            (TestBattles.Character("diren1", agility: 5), CombatSide.Enemy, new GridCell(7, 2)),
        ]);

    private static Combatant Get(Battle battle, string id) => battle.Combatants.Single(c => c.Character.Id == id);
}
