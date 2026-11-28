using Tinderhearth.Rules.Combat;
using Tinderhearth.Rules.Economy;
using Tinderhearth.Rules.Foundation.Actors;
using Tinderhearth.Rules.Progression;
using Xunit;

namespace Tinderhearth.Rules.Tests.Combat;

/// <summary>最低一档的 AI：先打这一回合打得着的，敌人先打学生、学生都打不着才打烬；谁都打不着就朝最近的走，然后防御。</summary>
/// <remarks>
/// 每个人的面板都一样：HP 100、攻击 40、防御 20、命中率 0.9、闪避率 0.2、暴击几率 0.25、暴击倍率 1.5，挨着打中是 30 点。
/// 随机源一下给 0、一下给 0.99：打中、不暴击。
/// </remarks>
public class BattleAiTests
{
    private static readonly CombatConfig SamePanels = TestBattles.Config(
        panel: TestBattles.FlatPanel(hp: 100, attack: 40, defense: 20, hitRate: 0.9, dodgeRate: 0.2, critChance: 0.25, critMultiplier: 1.5));

    [Fact]
    public void 学生打最近的敌人_站着就打得着就不走()
    {
        var battle = Start(8, 5, [Jin(new(0, 0)), Student("xuesheng", 6, new(3, 2)), Foe("diren1", 5, new(4, 2)), Foe("diren2", 4, new(6, 2))]);
        battle.EndTurn();
        var (student, near) = (battle.Current, Get(battle, "diren1"));

        var steps = BattleAi.PlayTurn(battle);

        var cast = Assert.IsType<AiCast>(Assert.Single(steps));
        Assert.Equal((student, student.BasicAttack, near), (cast.Actor, cast.Skill, cast.Target));
        Assert.Equal(new Strike(Hit: true, Critical: false, Damage: 30), cast.Use.Strike);
        Assert.Equal(70, near.Hp);
        Assert.Same(near, battle.Current);
    }

    [Fact]
    public void 学生打不着就走过去再打()
    {
        var battle = Start(8, 5, [Jin(new(0, 0)), Student("xuesheng", 6, new(0, 2)), Foe("diren1", 5, new(4, 2)), Foe("diren2", 4, new(7, 4))]);
        battle.EndTurn();
        var near = Get(battle, "diren1");

        var steps = BattleAi.PlayTurn(battle);

        Assert.Equal(2, steps.Count);
        var walk = Assert.IsType<AiWalk>(steps[0]);
        Assert.Equal(1, GridDistance.Between(walk.Path[^1], near.Cell));
        Assert.Same(near, Assert.IsType<AiCast>(steps[1]).Target);
        Assert.Equal(70, near.Hp);
    }

    [Fact]
    public void 敌人先打这一回合打得着的学生_烬挨着也不打烬()
    {
        var battle = Start(8, 3, [Jin(new(0, 0)), Student("xuesheng", 6, new(4, 0)), Foe("diren1", 5, new(1, 0))]);
        var (jin, student) = (battle.Current, Get(battle, "xuesheng"));
        battle.EndTurn();
        battle.EndTurn();

        var steps = BattleAi.PlayTurn(battle);

        Assert.IsType<AiWalk>(steps[0]);
        Assert.Same(student, Assert.IsType<AiCast>(steps[1]).Target);
        Assert.Equal(70, student.Hp);
        Assert.Equal(100, jin.Hp);
    }

    [Fact]
    public void 学生都打不着才打烬()
    {
        var battle = Start(12, 3, [Jin(new(0, 0)), Student("xuesheng", 6, new(11, 2)), Foe("diren1", 5, new(1, 0))]);
        var jin = battle.Current;
        battle.EndTurn();
        battle.EndTurn();

        var steps = BattleAi.PlayTurn(battle);

        Assert.Same(jin, Assert.IsType<AiCast>(Assert.Single(steps)).Target);
        Assert.Equal(70, jin.Hp);
    }

    [Fact]
    public void 谁都打不着就朝走过去最近的那个对面的人走_能走多远走多远_然后防御()
    {
        // 敌人到学生旁边要走 9 格，到烬旁边要走 12 格，这一回合只有 5 点行动力。
        var battle = Start(14, 3, [Jin(new(0, 0)), Student("xuesheng", 6, new(3, 2)), Foe("diren1", 5, new(13, 1))], roll: TestBattles.NoRolls);
        var (jin, student) = (battle.Current, Get(battle, "xuesheng"));
        battle.EndTurn();
        battle.EndTurn();
        var enemy = battle.Current;

        var steps = BattleAi.PlayTurn(battle);

        Assert.Equal(2, steps.Count);
        Assert.Equal(5, Assert.IsType<AiWalk>(steps[0]).Path.Count);
        Assert.Equal(5, GridDistance.Between(enemy.Cell, student.Cell));
        var cast = Assert.IsType<AiCast>(steps[1]);
        Assert.Equal((enemy.Defend, enemy), (cast.Skill, cast.Target));
        Assert.Contains(CombatStatusIds.Block, enemy.Statuses);
        Assert.Same(jin, battle.Current);
    }

    [Fact]
    public void 走不过去也打不着就原地防御()
    {
        // 第 3 列一整列是墙，敌人在墙的另一边。
        var battle = Start(
            7,
            3,
            [Jin(new(0, 0)), Student("xuesheng", 6, new(0, 2)), Foe("diren1", 5, new(6, 1))],
            roll: TestBattles.NoRolls,
            walls: [new(3, 0), new(3, 1), new(3, 2)]);
        battle.EndTurn();
        battle.EndTurn();
        var enemy = battle.Current;

        var steps = BattleAi.PlayTurn(battle);

        Assert.Same(enemy.Defend, Assert.IsType<AiCast>(Assert.Single(steps)).Skill);
        Assert.Equal(new GridCell(6, 1), enemy.Cell);
    }

    [Fact]
    public void 同一局面_同一串随机数_AI做的是同一件事()
    {
        var first = PlayRounds(seed: 7, rounds: 6);
        var again = PlayRounds(seed: 7, rounds: 6);

        Assert.Equal(first, again);
        Assert.Contains(first, step => step.Contains("走到", StringComparison.Ordinal));
        Assert.Contains(first, step => step.Contains("basic_attack", StringComparison.Ordinal));
    }

    /// <summary>烬每回合什么都不做，其余的人都交给 AI，打几轮，把每一步写成一行字。</summary>
    private static List<string> PlayRounds(int seed, int rounds)
    {
        var random = new Random(seed);
        var battle = Start(
            10,
            6,
            [
                Jin(new(0, 0)),
                Student("xuesheng", 6, new(1, 2)),
                Student("xuesheng2", 5, new(0, 4)),
                Foe("diren1", 5, new(8, 1)),
                Foe("diren2", 4, new(9, 4)),
            ],
            roll: random.NextDouble);
        var log = new List<string>();
        while (battle.Outcome is null && battle.Round <= rounds)
        {
            if (battle.Current.IsProtagonist)
            {
                battle.EndTurn();
                continue;
            }

            log.AddRange(BattleAi.PlayTurn(battle).Select(Describe));
        }

        log.Add($"收场：{battle.Outcome?.ToString() ?? "还在打"}");
        return log;
    }

    private static string Describe(AiStep step) => step switch
    {
        AiWalk walk => $"{walk.Actor.Index} 走到 {walk.Path[^1]}",
        AiCast cast => $"{cast.Actor.Index} 对 {cast.Target.Index} 放 {cast.Skill.Id}：{cast.Use}",
        _ => throw new ArgumentOutOfRangeException(nameof(step), step, "没见过的一步"),
    };

    private static Battle Start(
        int width,
        int height,
        (CharacterDefinition, CombatSide, GridCell)[] positions,
        Func<double>? roll = null,
        IReadOnlyCollection<GridCell>? walls = null) =>
        TestBattles.Start(width, height, positions, roll ?? HitNoCrit(), SamePanels, walls: walls);

    /// <summary>一下给 0、一下给 0.99：每一下都打中、不暴击。AI 打中了才掷暴击，所以两个数正好一对一对用掉。</summary>
    private static Func<double> HitNoCrit()
    {
        var next = 0;
        return () => next++ % 2 == 0 ? 0.0 : 0.99;
    }

    private static (CharacterDefinition, CombatSide, GridCell) Jin(GridCell cell) => (TestBattles.Character("jin", agility: 9), CombatSide.Ally, cell);

    private static (CharacterDefinition, CombatSide, GridCell) Student(string id, int agility, GridCell cell) =>
        (TestBattles.Character(id, agility: agility), CombatSide.Ally, cell);

    private static (CharacterDefinition, CombatSide, GridCell) Foe(string id, int agility, GridCell cell) =>
        (TestBattles.Character(id, agility: agility), CombatSide.Enemy, cell);

    private static Combatant Get(Battle battle, string id) => battle.Combatants.Single(c => c.Character.Id == id);
}
