using Tinderhearth.Rules.Foundation.Content;

namespace Tinderhearth.Rules.Combat;

/// <summary>战斗那一份配置（代码仓 <c>data/config/combat.json</c>）。代码里不出现这些数字的字面量。</summary>
/// <remarks>
/// 值都是占位，正式的归设计仓 design/数值模型.md。每一格都用 <c>required</c>：缺了载入被拒，没有能用的默认值，
/// 理由同 <see cref="Foundation.Config.GameConfig"/> 写的那一条静默失效。
/// </remarks>
public sealed record CombatConfig
{
    public const string ContentPath = "config/combat.json";

    /// <summary>面板每一项怎么从属性算。</summary>
    public required CombatPanelConfig Panel
    {
        get;
        init => field = value ?? throw new ArgumentNullException(nameof(Panel), $"{ContentPath} 的 Panel 不得为 null");
    }

    public static CombatConfig Parse(string json) => ContentJson.Parse<CombatConfig>(json, ContentPath);

    /// <summary>从内容目录里读这一份。缺了这一份当场抛。</summary>
    public static CombatConfig Load(ContentCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        return catalog.Resolve("config").TryGetValue(ContentPath, out var entry)
            ? Parse(entry.Text)
            : throw new FileNotFoundException($"缺少 {ContentPath}");
    }
}

/// <summary>面板上一项怎么从属性算：初始值 + 每点属性给的 × 那一项属性。</summary>
public sealed record PanelConversion
{
    /// <summary>初始值。</summary>
    public required double Base
    {
        get;
        init => field = NotNegative(value, nameof(Base));
    }

    /// <summary>每点属性给多少。</summary>
    public required double PerPoint
    {
        get;
        init => field = NotNegative(value, nameof(PerPoint));
    }

    /// <summary>那一项属性是这么多点时，这一项是多少。</summary>
    public double At(int points) => Base + (PerPoint * points);

    private static double NotNegative(double value, string field) => double.IsFinite(value) && value >= 0
        ? value
        : throw new ArgumentOutOfRangeException(field, $"面板换算的 {field} 必须是不小于 0 的数，实际 {value}");
}

/// <summary>面板每一项各自怎么换算。哪一项看哪一项属性是规则，写在 <see cref="CombatPanel.From"/>，不在配置里。</summary>
/// <remarks>那张表在工作区根 desc/各类公式计算.md 的「属性换算成面板」。</remarks>
public sealed record CombatPanelConfig
{
    public required PanelConversion HpMax { get; init => field = Present(value, nameof(HpMax)); }

    public required PanelConversion MpMax { get; init => field = Present(value, nameof(MpMax)); }

    public required PanelConversion HpRegen { get; init => field = Present(value, nameof(HpRegen)); }

    public required PanelConversion MpRegen { get; init => field = Present(value, nameof(MpRegen)); }

    public required PanelConversion PhysicalAttack { get; init => field = Present(value, nameof(PhysicalAttack)); }

    public required PanelConversion MagicAttack { get; init => field = Present(value, nameof(MagicAttack)); }

    public required PanelConversion PhysicalDefense { get; init => field = Present(value, nameof(PhysicalDefense)); }

    public required PanelConversion MagicDefense { get; init => field = Present(value, nameof(MagicDefense)); }

    public required PanelConversion ToughnessMax { get; init => field = Present(value, nameof(ToughnessMax)); }

    public required PanelConversion HitRate { get; init => field = Present(value, nameof(HitRate)); }

    public required PanelConversion DodgeRate { get; init => field = Present(value, nameof(DodgeRate)); }

    public required PanelConversion PhysicalCritChance { get; init => field = Present(value, nameof(PhysicalCritChance)); }

    public required PanelConversion MagicCritChance { get; init => field = Present(value, nameof(MagicCritChance)); }

    public required PanelConversion PhysicalCritMultiplier { get; init => field = Present(value, nameof(PhysicalCritMultiplier)); }

    public required PanelConversion MagicCritMultiplier { get; init => field = Present(value, nameof(MagicCritMultiplier)); }

    private static PanelConversion Present(PanelConversion value, string field) =>
        value ?? throw new ArgumentNullException(field, $"{CombatConfig.ContentPath} 的 Panel.{field} 不得为 null");
}
