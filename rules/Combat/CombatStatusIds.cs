namespace Tinderhearth.Rules.Combat;

/// <summary>一场仗认得的状态，按标识记（工作区根 desc/状态.md 的「标识」那一列）。</summary>
/// <remarks>
/// 现在只有「格挡」：防御给自己挂上，挂着时受到的伤害降低，轮到自己行动时解除。技能给自己挂的写了别的标识，
/// 载入时由 <see cref="CombatContent"/> 拒掉，不等放那一招时才发现。别的状态等用到它们的需求再加。
/// </remarks>
public static class CombatStatusIds
{
    /// <summary>格挡。</summary>
    public const string Block = "block";

    /// <summary>认得的全部标识。</summary>
    public static IReadOnlySet<string> Known { get; } = new HashSet<string>(StringComparer.Ordinal) { Block };
}
