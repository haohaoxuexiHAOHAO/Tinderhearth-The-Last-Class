using Tinderhearth.Rules.Combat;
using Tinderhearth.Rules.Economy;
using Tinderhearth.Rules.Foundation.Actors;
using Tinderhearth.Rules.Progression;
using Tinderhearth.Rules.Tests.Progression;

namespace Tinderhearth.Rules.Tests.Combat;

/// <summary>测试用的一场仗：一间没有墙的屋，几个人站好，敏捷与行动力按用例给。</summary>
/// <remarks>
/// 角色、技能与配置都写在这里，不读工程里那一份：那里的数是占位、会改，用例要的是自己定死的数。
/// 打仗的用例都从这里开打，各处不各抄一份。
/// </remarks>
internal static class TestBattles
{
    /// <summary>走一格 1 点，贴着敌人那一步多 1 点。</summary>
    public static readonly MovementConfig Costs = new() { StepCost = 1, ExtraBesideEnemy = 1 };

    /// <summary>一份角色定义：敏捷与行动力按用例给，其余照 <see cref="TestCharacters"/>。</summary>
    public static CharacterDefinition Character(string id, int agility = 5, int actionPoints = 5)
    {
        var definition = CharacterDefinition.Parse(TestCharacters.Json(id, $"character.{id}.name"), "测试");
        return definition with
        {
            Attributes = definition.Attributes with { Agility = agility },
            ActionPoints = actionPoints,
        };
    }

    /// <summary>width × height 的地面、没有墙的一间屋，照给的先后摆站位，开打。</summary>
    public static Battle Start(int width, int height, params (CharacterDefinition Character, CombatSide Side, GridCell Cell)[] positions)
    {
        Dictionary<string, CharacterDefinition> characters = positions.Select(p => p.Character).DistinctBy(c => c.Id).ToDictionary(c => c.Id);
        var room = BattleRoom.Create(
            Floor(width, height),
            [],
            positions.Select(p => new StartingPosition(p.Cell, p.Character.Id, p.Side)),
            characters);
        return Battle.Start(room, Content(characters));
    }

    /// <summary>width × height 的地面：第 0 到 width − 1 列，第 0 到 height − 1 行。</summary>
    public static IReadOnlyList<GridCell> Floor(int width, int height) =>
        [.. Enumerable.Range(0, width).SelectMany(x => Enumerable.Range(0, height).Select(y => new GridCell(x, y)))];

    /// <summary>打仗那份内容：给的这几个角色，加上他们指的普通攻击与防御和这里的配置。</summary>
    public static CombatContent Content(IReadOnlyDictionary<string, CharacterDefinition> characters) =>
        CombatContent.Build(characters, Skills(), Config());

    /// <summary>角色指的普通攻击与防御，照工程里那两份的样子。</summary>
    private static Dictionary<string, SkillDefinition> Skills() => new()
    {
        ["basic_attack"] = new SkillDefinition
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
        },
        ["defend"] = new SkillDefinition
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
            SelfStatusId = "block",
            WeaponKind = null,
        },
    };

    /// <summary>面板每一项都是 1、不看属性；走路照 <see cref="Costs"/>。</summary>
    private static CombatConfig Config()
    {
        var flat = new PanelConversion { Base = 1, PerPoint = 0 };
        return new CombatConfig
        {
            Panel = new CombatPanelConfig
            {
                HpMax = flat,
                MpMax = flat,
                HpRegen = flat,
                MpRegen = flat,
                PhysicalAttack = flat,
                MagicAttack = flat,
                PhysicalDefense = flat,
                MagicDefense = flat,
                ToughnessMax = flat,
                HitRate = flat,
                DodgeRate = flat,
                PhysicalCritChance = flat,
                MagicCritChance = flat,
                PhysicalCritMultiplier = flat,
                MagicCritMultiplier = flat,
            },
            Movement = Costs,
        };
    }
}
