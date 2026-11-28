using Tinderhearth.Rules.Combat;
using Tinderhearth.Rules.Economy;
using Tinderhearth.Rules.Foundation.Actors;
using Tinderhearth.Rules.Progression;
using Xunit;

namespace Tinderhearth.Rules.Tests.Combat;

/// <summary>一场仗里放招：施放距离、对象、一回合一件占回合的事、冷却、格挡，瞄准时写的数就是真打时用的。</summary>
/// <remarks>
/// 每个人的面板都一样：HP 100、攻击 40、防御 20、命中率 0.9、闪避率 0.2、暴击几率 0.25、暴击倍率 1.5。
/// 所以挨着打一下命中几率 0.8，打中 30 点，暴击 45 点，对方挂着「格挡」时 15 点。
/// </remarks>
public class BattleSkillTests
{
    private static readonly CombatConfig SamePanels = TestBattles.Config(
        panel: TestBattles.FlatPanel(hp: 100, attack: 40, defense: 20, hitRate: 0.9, dodgeRate: 0.2, critChance: 0.25, critMultiplier: 1.5));

    [Fact]
    public void 普通攻击打得着斜着挨着的敌人_打中扣掉HP()
    {
        var battle = Duel(TestBattles.Rolls(0.0, 0.99));
        var (jin, enemy) = (battle.Current, Enemy(battle));

        var use = battle.UseSkill(jin.BasicAttack, enemy);

        Assert.True(use.Used);
        Assert.Equal(new Strike(Hit: true, Critical: false, Damage: 30), use.Strike);
        Assert.Equal(70, enemy.Hp);
    }

    [Fact]
    public void 目标不在施放距离以内被拒_什么都不改也不掷骰()
    {
        var battle = Start([Jin(new GridCell(0, 0)), Student(new GridCell(0, 2)), Foe(new GridCell(2, 1))]);
        var (jin, enemy) = (battle.Current, Enemy(battle));

        var use = battle.UseSkill(jin.BasicAttack, enemy);

        Assert.Equal(new SkillUse(SkillRefusal.OutOfRange, null), use);
        AssertUntouched(battle, jin);
        Assert.Equal(100, enemy.Hp);
    }

    [Fact]
    public void 施放距离照走路的数法_中间隔着人也照样数()
    {
        // 一格宽的走廊：烬 (0,0)、学生 (1,0)、敌人 (2,0)；普通攻击的施放距离改成 2。
        var battle = Start(
            [Jin(new GridCell(0, 0)), Student(new GridCell(1, 0)), Foe(new GridCell(2, 0))],
            TestBattles.Rolls(0.0, 0.99),
            width: 3,
            height: 1,
            skills: [TestBattles.BasicAttack with { Range = 2 }]);

        var use = battle.UseSkill(battle.Current.BasicAttack, Enemy(battle));

        Assert.True(use.Used);
        Assert.True(use.Strike!.Value.Hit);
    }

    [Fact]
    public void 打人的招点了自己这一边的人被拒_对自己放的招点了别人也被拒()
    {
        var battle = Duel(TestBattles.NoRolls);
        var jin = battle.Current;
        var student = battle.Combatants.Single(c => c.Character.Id == "xuesheng");
        var enemy = Enemy(battle);

        Assert.Equal(SkillRefusal.WrongTarget, battle.UseSkill(jin.BasicAttack, student).Refusal);
        Assert.Equal(SkillRefusal.WrongTarget, battle.UseSkill(jin.BasicAttack, jin).Refusal);
        Assert.Equal(SkillRefusal.WrongTarget, battle.UseSkill(jin.Defend, enemy).Refusal);
        Assert.Equal(SkillRefusal.WrongTarget, battle.UseSkill(jin.Defend, student).Refusal);
        AssertUntouched(battle, jin);
        Assert.Empty(jin.Statuses);
    }

    [Fact]
    public void 一回合只放得了一招占回合的_第二招被拒_放完剩下的行动力减半_之后还能走()
    {
        var battle = Duel(TestBattles.Rolls(0.0, 0.99));
        var (jin, enemy) = (battle.Current, Enemy(battle));

        battle.UseSkill(jin.BasicAttack, enemy);

        Assert.Equal(2, battle.ActionPoints);
        Assert.True(battle.TurnActionUsed);
        Assert.Equal(SkillRefusal.TurnActionUsed, battle.UseSkill(jin.BasicAttack, enemy).Refusal);
        Assert.Equal(SkillRefusal.TurnActionUsed, battle.UseSkill(jin.Defend, jin).Refusal);
        Assert.Equal(70, enemy.Hp);
        Assert.Equal(2, battle.ActionPoints);
        Assert.Same(jin, battle.Current);

        // (0,0) 到 (1,0) 前后都挨着敌人，花 2 点，正好用完。
        Assert.NotNull(battle.MoveTo(new GridCell(1, 0)));
        Assert.Equal(new GridCell(1, 0), jin.Cell);
    }

    [Fact]
    public void 放完行动力用完_这一回合自动结束()
    {
        var battle = Start([Jin(new GridCell(0, 0)), Student(new GridCell(0, 1)), Foe(new GridCell(5, 0))], TestBattles.Rolls(0.0, 0.99));
        var jin = battle.Current;
        battle.MoveTo(new GridCell(4, 0));
        Assert.Equal(1, battle.ActionPoints);

        var use = battle.UseSkill(jin.BasicAttack, Enemy(battle));

        Assert.True(use.Used);
        Assert.Equal("xuesheng", battle.Current.Character.Id);
        Assert.Equal(5, battle.ActionPoints);
        Assert.False(battle.TurnActionUsed);
    }

    [Fact]
    public void 换成再走满一次之后_占回合的招被拒()
    {
        var battle = Start([Jin(new GridCell(0, 0)), Student(new GridCell(0, 1)), Foe(new GridCell(8, 0))], width: 10, height: 2);
        var jin = battle.Current;

        battle.MoveTo(new GridCell(7, 0));

        Assert.True(battle.TurnActionUsed);
        Assert.Equal(3, battle.ActionPoints);
        Assert.Equal(SkillRefusal.TurnActionUsed, battle.UseSkill(jin.BasicAttack, Enemy(battle)).Refusal);
        Assert.Equal(SkillRefusal.TurnActionUsed, battle.UseSkill(jin.Defend, jin).Refusal);
    }

    [Fact]
    public void 不占回合的招不用掉那一件_放完行动力不减半()
    {
        var battle = Duel(TestBattles.Rolls(0.0, 0.99), skills: [TestBattles.Defend with { TakesTurn = false }]);
        var (jin, enemy) = (battle.Current, Enemy(battle));

        Assert.True(battle.UseSkill(jin.Defend, jin).Used);

        Assert.Equal(5, battle.ActionPoints);
        Assert.False(battle.TurnActionUsed);
        Assert.Contains(CombatStatusIds.Block, jin.Statuses);
        Assert.True(battle.UseSkill(jin.BasicAttack, enemy).Used);
        Assert.Equal(2, battle.ActionPoints);
    }

    /// <summary>防御改成不占回合，好让冷却成为唯一拦着它的东西。</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void 冷却N的招用过之后_过N个自己的回合才能再用_别人的回合不算(int cooldown)
    {
        var defend = TestBattles.Defend with { TakesTurn = false, Cooldown = cooldown };
        var battle = Duel(TestBattles.NoRolls, skills: [defend]);
        var jin = battle.Current;

        Assert.True(battle.UseSkill(jin.Defend, jin).Used);
        Assert.Equal(cooldown, jin.CooldownLeft(jin.Defend));
        Assert.Equal(SkillRefusal.OnCooldown, battle.UseSkill(jin.Defend, jin).Refusal);

        battle.EndTurn();
        Assert.Equal(cooldown, jin.CooldownLeft(jin.Defend));

        for (var waited = 1; waited < cooldown; waited++)
        {
            NextTurnOf(battle, jin);
            Assert.Equal(cooldown - waited, jin.CooldownLeft(jin.Defend));
            Assert.Equal(SkillRefusal.OnCooldown, battle.UseSkill(jin.Defend, jin).Refusal);
        }

        NextTurnOf(battle, jin);
        Assert.Equal(0, jin.CooldownLeft(jin.Defend));
        Assert.True(battle.UseSkill(jin.Defend, jin).Used);
    }

    [Fact]
    public void 冷却1的招每回合都能用()
    {
        var battle = Duel(TestBattles.Rolls(0.0, 0.99, 0.0, 0.99));
        var (jin, enemy) = (battle.Current, Enemy(battle));

        Assert.True(battle.UseSkill(jin.BasicAttack, enemy).Used);
        NextTurnOf(battle, jin);
        Assert.True(battle.UseSkill(jin.BasicAttack, enemy).Used);

        Assert.Equal(40, enemy.Hp);
    }

    [Fact]
    public void 防御给自己挂格挡_挂着时受到的伤害降低_轮到自己行动时解除()
    {
        var battle = Duel(TestBattles.Rolls(0.0, 0.99));
        var jin = battle.Current;
        var student = battle.Combatants.Single(c => c.Character.Id == "xuesheng");

        Assert.True(battle.UseSkill(jin.Defend, jin).Used);
        Assert.Equal(CombatStatusIds.Block, Assert.Single(jin.Statuses));
        Assert.Equal(2, battle.ActionPoints);

        battle.EndTurn();
        battle.EndTurn();
        var enemy = battle.Current;
        Assert.Equal(CombatSide.Enemy, enemy.Side);
        Assert.Equal(15, battle.Preview(enemy.BasicAttack, jin)!.Damage);
        Assert.Equal(30, battle.Preview(enemy.BasicAttack, student)!.Damage);

        var use = battle.UseSkill(enemy.BasicAttack, jin);

        Assert.Equal(new Strike(Hit: true, Critical: false, Damage: 15), use.Strike);
        Assert.Equal(85, jin.Hp);
        Assert.Contains(CombatStatusIds.Block, jin.Statuses);

        battle.EndTurn();
        Assert.Same(jin, battle.Current);
        Assert.Empty(jin.Statuses);
    }

    [Fact]
    public void 瞄准时算得出的命中几率与伤害_就是真打时用的那一份()
    {
        var battle = Duel(TestBattles.NoRolls);
        var odds = battle.Preview(battle.Current.BasicAttack, Enemy(battle))!;
        Assert.Equal(0.8, odds.HitChance, precision: 10);
        Assert.Equal(0.25, odds.CriticalChance, precision: 10);
        Assert.Equal((30, 45), (odds.Damage, odds.CriticalDamage));

        var justHit = AttackOnce(odds.HitChance - 1e-9, odds.CriticalChance);
        var justMissed = AttackOnce(odds.HitChance);
        var critical = AttackOnce(0.0, odds.CriticalChance - 1e-9);

        Assert.Equal(new Strike(Hit: true, Critical: false, Damage: odds.Damage), justHit);
        Assert.Equal(new Strike(Hit: false, Critical: false, Damage: 0), justMissed);
        Assert.Equal(new Strike(Hit: true, Critical: true, Damage: odds.CriticalDamage), critical);
    }

    [Fact]
    public void 打空不掷暴击_也不扣HP()
    {
        // 只给一个数：打空之后再要一个就让用例失败。
        var battle = Duel(TestBattles.Rolls(0.99));
        var enemy = Enemy(battle);

        var use = battle.UseSkill(battle.Current.BasicAttack, enemy);

        Assert.Equal(new Strike(Hit: false, Critical: false, Damage: 0), use.Strike);
        Assert.Equal(100, enemy.Hp);
        Assert.True(battle.TurnActionUsed);
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(-0.1)]
    [InlineData(double.NaN)]
    public void 随机源给的数不在0到1之间_当场报错(double roll)
    {
        var battle = Duel(TestBattles.Rolls(roll));

        Assert.Throws<InvalidOperationException>(() => battle.UseSkill(battle.Current.BasicAttack, Enemy(battle)));
    }

    [Fact]
    public void 放的不是轮到的人会的招_或者对的人不在这一场里_当场报错()
    {
        var battle = Duel(TestBattles.NoRolls);
        var stranger = Enemy(Duel(TestBattles.NoRolls));

        Assert.Throws<ArgumentException>(() => battle.UseSkill(TestBattles.BasicAttack with { Range = 3 }, Enemy(battle)));
        Assert.Throws<ArgumentException>(() => battle.UseSkill(battle.Current.BasicAttack, stranger));
    }

    /// <summary>6 × 3 的一间屋：烬 (0,0) 敏捷 9，学生 (0,2)，敌人 (1,1) 斜着挨着烬。出手顺序烬、学生、敌人。</summary>
    private static Battle Duel(Func<double> roll, IEnumerable<SkillDefinition>? skills = null) =>
        Start([Jin(new GridCell(0, 0)), Student(new GridCell(0, 2)), Foe(new GridCell(1, 1))], roll, skills: skills);

    private static Battle Start(
        (CharacterDefinition, CombatSide, GridCell)[] positions,
        Func<double>? roll = null,
        int width = 6,
        int height = 3,
        IEnumerable<SkillDefinition>? skills = null) =>
        TestBattles.Start(width, height, positions, roll, SamePanels, skills);

    private static (CharacterDefinition, CombatSide, GridCell) Jin(GridCell cell) => (TestBattles.Character("jin", agility: 9), CombatSide.Ally, cell);

    private static (CharacterDefinition, CombatSide, GridCell) Student(GridCell cell) => (TestBattles.Character("xuesheng"), CombatSide.Ally, cell);

    private static (CharacterDefinition, CombatSide, GridCell) Foe(GridCell cell) => (TestBattles.Character("diren1"), CombatSide.Enemy, cell);

    private static Combatant Enemy(Battle battle) => battle.Combatants.Single(c => c.Side == CombatSide.Enemy);

    /// <summary>新开一场，烬用普通攻击打那个敌人一下，随机数照给的。</summary>
    private static Strike AttackOnce(params double[] rolls)
    {
        var battle = Duel(TestBattles.Rolls(rolls));
        return battle.UseSkill(battle.Current.BasicAttack, Enemy(battle)).Strike!.Value;
    }

    /// <summary>结束回合，直到又轮到这个人。</summary>
    private static void NextTurnOf(Battle battle, Combatant who)
    {
        do
        {
            battle.EndTurn();
        }
        while (battle.Current != who);
    }

    /// <summary>被拒的那一下什么都没改：还是他的回合，行动力满的，那一件没用，两招都不在冷却。</summary>
    private static void AssertUntouched(Battle battle, Combatant who)
    {
        Assert.Same(who, battle.Current);
        Assert.Equal(5, battle.ActionPoints);
        Assert.False(battle.TurnActionUsed);
        Assert.All(who.Skills, skill => Assert.Equal(0, who.CooldownLeft(skill)));
    }
}
