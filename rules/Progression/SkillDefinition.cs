using Tinderhearth.Rules.Foundation.Content;
using Tinderhearth.Rules.Foundation.Text;

namespace Tinderhearth.Rules.Progression;

/// <summary>一招的定义，来自数据文件而不是代码。普通攻击与防御也是招，一份文件一招。</summary>
/// <remarks>
/// 每一招要写什么在工作区根 desc/战斗规则.md 的「技能」。这里只读打一场仗用得上的几格；
/// 准备回合、范围形状、击退与消耗，等用到它们的需求再加。
///
/// 每一格都用 <c>required</c>，理由同 <see cref="ItemDefinition"/>：伤害那三格、给自己挂的状态与兵器种类写 <c>null</c>
/// 是合法值（防御不打人，普通攻击不给自己挂东西），只有 <c>required</c> 分得出「没写」与「写了 null」。
/// 几格之间要对得上的，在 <see cref="Parse"/> 里一次全报。
/// </remarks>
public sealed record SkillDefinition
{
    public const string ContentDirectory = "skills";

    /// <summary>稳定标识。角色定义与存档都按它引用；玩家看不到它。</summary>
    public required string Id
    {
        get;
        init => field = NonEmpty(value, nameof(Id));
    }

    /// <summary>名字在文本表里的键。</summary>
    public required string NameKey
    {
        get;
        init => field = TextKey.Require(value, $"技能定义的 {nameof(NameKey)}");
    }

    /// <summary>描述在文本表里的键。</summary>
    public required string DescriptionKey
    {
        get;
        init => field = TextKey.Require(value, $"技能定义的 {nameof(DescriptionKey)}");
    }

    /// <summary>用它算不算这一回合那一件占回合的事。</summary>
    public required bool TakesTurn { get; init; }

    /// <summary>对谁放。</summary>
    public required SkillTarget Target { get; init; }

    /// <summary>施放距离：按走路的数法数格子，横、竖、斜各算 1 格。对自己放的写 0。</summary>
    public required int Range
    {
        get;
        init => field = value >= 0
            ? value
            : throw new ArgumentOutOfRangeException(nameof(Range), $"技能定义的 {nameof(Range)} 不得为负，实际 {value}");
    }

    /// <summary>冷却：用过之后过几个自己的回合才能再用。至少 1，冷却 1 的每回合都能用。</summary>
    public required int Cooldown
    {
        get;
        init => field = value >= 1
            ? value
            : throw new ArgumentOutOfRangeException(nameof(Cooldown), $"技能定义的 {nameof(Cooldown)} 至少为 1，实际 {value}");
    }

    /// <summary>打的是物理还是魔法；不造成伤害的写 <c>null</c>。</summary>
    public required DamageKind? DamageKind { get; init; }

    /// <summary>技能倍率，乘在攻击上；不造成伤害的写 <c>null</c>。</summary>
    public required double? Multiplier
    {
        get;
        init => field = value is null || (double.IsFinite(value.Value) && value.Value > 0)
            ? value
            : throw new ArgumentOutOfRangeException(
                nameof(Multiplier), $"技能定义的 {nameof(Multiplier)} 要么是 null（不打人），要么为正，实际 {value}");
    }

    /// <summary>穿透：先从对方的防御里减掉多少；不造成伤害的写 <c>null</c>。</summary>
    public required int? Penetration
    {
        get;
        init => field = value is null or >= 0
            ? value
            : throw new ArgumentOutOfRangeException(
                nameof(Penetration), $"技能定义的 {nameof(Penetration)} 要么是 null（不打人），要么不小于 0，实际 {value}");
    }

    /// <summary>用了给自己挂哪个状态，写状态的标识（工作区根 desc/状态.md 那一列）；不挂的写 <c>null</c>。</summary>
    /// <remarks>标识认不认得，打仗那一侧在载入时核（<see cref="Combat.CombatContent"/>），认得哪几个在 <see cref="Combat.CombatStatusIds"/>。</remarks>
    public required string? SelfStatusId
    {
        get;
        init => field = NullOrNonEmpty(value, nameof(SelfStatusId));
    }

    /// <summary>兵器种类，只决定放哪段特效、配什么声音（工作区根 desc/战斗规则.md 的「消耗、兵器与击退」）；不用兵器的写 <c>null</c>。</summary>
    public required string? WeaponKind
    {
        get;
        init => field = NullOrNonEmpty(value, nameof(WeaponKind));
    }

    /// <summary>这一招要在文本表里有的那几条。启动时逐条核，缺了打一条错误。</summary>
    public IReadOnlyList<string> TextKeys => [NameKey, DescriptionKey];

    /// <summary>解析一份技能定义，并核几格之间对不对得上；对不上的一次全报。</summary>
    public static SkillDefinition Parse(string json, string whatForDiagnostics)
    {
        var skill = ContentJson.Parse<SkillDefinition>(json, whatForDiagnostics);
        var mismatches = skill.Mismatches().ToList();
        return mismatches.Count == 0
            ? skill
            : throw new InvalidDataException($"{whatForDiagnostics}（技能 {skill.Id}）几格对不上：{string.Join("；", mismatches)}");
    }

    private IEnumerable<string> Mismatches()
    {
        var damaging = DamageKind is not null;
        if (damaging != Multiplier.HasValue)
        {
            yield return damaging ? "打人的招要写技能倍率" : "不打人的招，技能倍率写 null";
        }

        if (damaging != Penetration.HasValue)
        {
            yield return damaging ? "打人的招要写穿透" : "不打人的招，穿透写 null";
        }

        if (Target == SkillTarget.Self && Range != 0)
        {
            yield return $"对自己放的招施放距离写 0，实际 {Range}";
        }

        if (Target == SkillTarget.Self && damaging)
        {
            yield return "对自己放的招不打人，伤害那几格写 null";
        }

        if (Target == SkillTarget.Enemy && Range < 1)
        {
            yield return "指向对面一个人的招，施放距离至少 1";
        }
    }

    private static string NonEmpty(string value, string field) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException($"技能定义的 {field} 不得为空", field)
            : value;

    private static string? NullOrNonEmpty(string? value, string field) =>
        value is not null && string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException($"技能定义的 {field} 要么是 null，要么写一个标识，不许是空串", field)
            : value;
}

/// <summary>一招对谁放。</summary>
public enum SkillTarget
{
    /// <summary>对自己，比如防御。</summary>
    Self,

    /// <summary>对面的一个人，比如普通攻击。</summary>
    Enemy,
}

/// <summary>伤害分物理与魔法，各对一种防御（工作区根 desc/战斗规则.md 的「类型与元素」）。</summary>
public enum DamageKind
{
    /// <summary>物理：物理攻击打，物理防御挡。</summary>
    Physical,

    /// <summary>魔法：魔法攻击打，魔法防御挡。</summary>
    Magic,
}
