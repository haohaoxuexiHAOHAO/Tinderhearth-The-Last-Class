namespace Tinderhearth.Rules.Progression;

/// <summary>一个角色的四项属性：力量、敏捷、体质、智力。</summary>
/// <remarks>
/// 每一项在战斗与经营里各管什么在工作区根 desc/角色面板.md 的「四项属性」，本类不复述。四项都用 <c>required</c>：
/// 位置参数 record 缺字段会悄悄落成 0，而 0 是一个合法的属性值，缺了分不出来，理由同 <see cref="ItemDefinition"/>。
/// </remarks>
public sealed record CharacterAttributes
{
    /// <summary>力量。</summary>
    public required int Strength
    {
        get;
        init => field = NotNegative(value, nameof(Strength));
    }

    /// <summary>敏捷。</summary>
    public required int Agility
    {
        get;
        init => field = NotNegative(value, nameof(Agility));
    }

    /// <summary>体质。</summary>
    public required int Constitution
    {
        get;
        init => field = NotNegative(value, nameof(Constitution));
    }

    /// <summary>智力。</summary>
    public required int Intellect
    {
        get;
        init => field = NotNegative(value, nameof(Intellect));
    }

    private static int NotNegative(int value, string field) => value >= 0
        ? value
        : throw new ArgumentOutOfRangeException(field, $"角色定义的属性 {field} 不得为负，实际 {value}");
}
