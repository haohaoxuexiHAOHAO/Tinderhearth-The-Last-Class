using Tinderhearth.Rules.Combat;
using Tinderhearth.Rules.Economy;
using Tinderhearth.Rules.Foundation.Actors;
using Tinderhearth.Rules.Progression;
using Tinderhearth.Rules.Tests.Progression;
using Xunit.Sdk;

namespace Tinderhearth.Rules.Tests.Combat;

/// <summary>测试用的一场仗：一间没有墙的屋，几个人站好，敏捷、行动力、面板、招与随机数按用例给。</summary>
/// <remarks>
/// 角色、技能与配置都写在这里，不读工程里那一份：那里的数是占位、会改，用例要的是自己定死的数。
/// 打仗的用例都从这里开打，各处不各抄一份。
/// </remarks>
internal static class TestBattles
{
    /// <summary>走一格 1 点，贴着敌人那一步多 1 点。</summary>
    public static readonly MovementConfig Costs = new() { StepCost = 1, ExtraBesideEnemy = 1 };

    /// <summary>命中几率限制在 5%–95%；挨着时闪避乘 0.5，隔 2 格乘 0.75，隔 3 格及以上乘 1。</summary>
    public static readonly HitConfig Hit = new() { MinChance = 0.05, MaxChance = 0.95, DodgeByDistance = [0.5, 0.75, 1.0] };

    /// <summary>K 为 60，「格挡」让受到的伤害少一半。</summary>
    public static readonly DamageConfig Damage = new() { DefenseK = 60, BlockReduction = 0.5 };

    /// <summary>普通攻击：占回合、打挨着的一个对面的人、冷却 1、物理、倍率 1、不穿透。</summary>
    public static readonly SkillDefinition BasicAttack = new()
    {
        Id = "basic_attack",
        NameKey = "skill.basic_attack.name",
        DescriptionKey = "skill.basic_attack.description",
        TakesTurn = true,
        Target = SkillTarget.Enemy,
        Range = 1,
        Cooldown = 1,
        DamageKind = DamageKind.Physical,
        Multiplier = 1.0,
        Penetration = 0,
        SelfStatusId = null,
        WeaponKind = null,
    };

    /// <summary>防御：占回合、对自己放、冷却 1，给自己挂「格挡」。</summary>
    public static readonly SkillDefinition Defend = new()
    {
        Id = "defend",
        NameKey = "skill.defend.name",
        DescriptionKey = "skill.defend.description",
        TakesTurn = true,
        Target = SkillTarget.Self,
        Range = 0,
        Cooldown = 1,
        DamageKind = null,
        Multiplier = null,
        Penetration = null,
        SelfStatusId = CombatStatusIds.Block,
        WeaponKind = null,
    };

    /// <summary>一份角色定义：敏捷与行动力按用例给，其余照 <see cref="TestCharacters"/>；普通攻击与防御指上面那两招。</summary>
    public static CharacterDefinition Character(string id, int agility = 5, int actionPoints = 5)
    {
        var definition = CharacterDefinition.Parse(TestCharacters.Json(id, $"character.{id}.name"), "测试");
        return definition with
        {
            Attributes = definition.Attributes with { Agility = agility },
            ActionPoints = actionPoints,
        };
    }

    /// <summary>width × height 的一间屋，照给的先后摆站位，开打。</summary>
    /// <param name="roll">随机源；不给的话这一场一掷骰就让用例失败。</param>
    /// <param name="config">战斗配置；不给的话面板每一项都是 1，走路、命中与伤害照上面那几份。</param>
    /// <param name="skills">换掉同标识的那几招，比如把防御的冷却改成 2。</param>
    /// <param name="walls">哪几格是墙；不给就一格墙都没有。</param>
    public static Battle Start(
        int width,
        int height,
        (CharacterDefinition Character, CombatSide Side, GridCell Cell)[] positions,
        Func<double>? roll = null,
        CombatConfig? config = null,
        IEnumerable<SkillDefinition>? skills = null,
        IReadOnlyCollection<GridCell>? walls = null)
    {
        Dictionary<string, CharacterDefinition> characters = positions.Select(p => p.Character).DistinctBy(c => c.Id).ToDictionary(c => c.Id);
        var room = BattleRoom.Create(
            Floor(width, height).Except(walls ?? []),
            walls ?? [],
            positions.Select(p => new StartingPosition(p.Cell, p.Character.Id, p.Side)),
            characters);
        return Battle.Start(room, Content(characters, config, skills), roll ?? NoRolls);
    }

    /// <summary>width × height 的地面：第 0 到 width − 1 列，第 0 到 height − 1 行。</summary>
    public static IReadOnlyList<GridCell> Floor(int width, int height) =>
        [.. Enumerable.Range(0, width).SelectMany(x => Enumerable.Range(0, height).Select(y => new GridCell(x, y)))];

    /// <summary>打仗那份内容：给的这几个角色，加上普通攻击与防御（可以换掉）和配置（不给就用 <see cref="Config"/>）。</summary>
    public static CombatContent Content(
        IReadOnlyDictionary<string, CharacterDefinition> characters,
        CombatConfig? config = null,
        IEnumerable<SkillDefinition>? skills = null)
    {
        var all = new Dictionary<string, SkillDefinition> { [BasicAttack.Id] = BasicAttack, [Defend.Id] = Defend };
        foreach (var skill in skills ?? [])
        {
            all[skill.Id] = skill;
        }

        return CombatContent.Build(characters, all, config ?? Config());
    }

    /// <summary>一份战斗配置：面板不给就每一项都是 1；走路、命中与伤害不给就照上面那几份。</summary>
    public static CombatConfig Config(CombatPanelConfig? panel = null, HitConfig? hit = null, DamageConfig? damage = null) => new()
    {
        Panel = panel ?? FlatPanel(),
        Movement = Costs,
        Hit = hit ?? Hit,
        Damage = damage ?? Damage,
    };

    /// <summary>不看属性的面板：每一项都是给定的数，物理与魔法同一个数。</summary>
    public static CombatPanelConfig FlatPanel(
        double hp = 1,
        double attack = 1,
        double defense = 1,
        double hitRate = 1,
        double dodgeRate = 1,
        double critChance = 1,
        double critMultiplier = 1)
    {
        static PanelConversion Flat(double value) => new() { Base = value, PerPoint = 0 };
        return new CombatPanelConfig
        {
            HpMax = Flat(hp),
            MpMax = Flat(1),
            HpRegen = Flat(1),
            MpRegen = Flat(1),
            PhysicalAttack = Flat(attack),
            MagicAttack = Flat(attack),
            PhysicalDefense = Flat(defense),
            MagicDefense = Flat(defense),
            ToughnessMax = Flat(1),
            HitRate = Flat(hitRate),
            DodgeRate = Flat(dodgeRate),
            PhysicalCritChance = Flat(critChance),
            MagicCritChance = Flat(critChance),
            PhysicalCritMultiplier = Flat(critMultiplier),
            MagicCritMultiplier = Flat(critMultiplier),
        };
    }

    /// <summary>照给的先后一个一个吐的随机数；吐完了还要就让用例失败。</summary>
    public static Func<double> Rolls(params double[] values)
    {
        var queue = new Queue<double>(values);
        return () => queue.TryDequeue(out var value) ? value : throw new XunitException("随机数用完了：这一场掷骰的次数比用例给的多");
    }

    /// <summary>一个数都不给的随机源：这一场一掷骰就让用例失败。</summary>
    public static Func<double> NoRolls { get; } = () => throw new XunitException("这一场不该掷骰");
}
