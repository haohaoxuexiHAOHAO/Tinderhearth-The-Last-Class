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

    /// <summary>走一格花几点行动力。</summary>
    public required MovementConfig Movement
    {
        get;
        init => field = value ?? throw new ArgumentNullException(nameof(Movement), $"{ContentPath} 的 Movement 不得为 null");
    }

    /// <summary>打不打得中。</summary>
    public required HitConfig Hit
    {
        get;
        init => field = (value ?? throw new ArgumentNullException(nameof(Hit), $"{ContentPath} 的 Hit 不得为 null")).Checked();
    }

    /// <summary>打多少。</summary>
    public required DamageConfig Damage
    {
        get;
        init => field = value ?? throw new ArgumentNullException(nameof(Damage), $"{ContentPath} 的 Damage 不得为 null");
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

/// <summary>走一格花几点行动力（工作区根 desc/各类公式计算.md 的「行动力」）。斜着走也算一格。</summary>
public sealed record MovementConfig
{
    /// <summary>走一格花几点。</summary>
    public required int StepCost
    {
        get;
        init => field = value > 0
            ? value
            : throw new ArgumentOutOfRangeException(nameof(StepCost), $"{CombatConfig.ContentPath} 的 Movement.StepCost 必须为正，实际 {value}");
    }

    /// <summary>贴着敌人走那一步多花几点：这一步走之前和走之后都挨着敌人时加上，挨着几个都只加一次。</summary>
    public required int ExtraBesideEnemy
    {
        get;
        init => field = value >= 0
            ? value
            : throw new ArgumentOutOfRangeException(
                nameof(ExtraBesideEnemy), $"{CombatConfig.ContentPath} 的 Movement.ExtraBesideEnemy 不得为负，实际 {value}");
    }
}

/// <summary>打不打得中（工作区根 desc/各类公式计算.md 的「命中」）：命中几率常规情况下的上下限，与闪避的距离倍率。几率写小数，1 就是 100%。</summary>
public sealed record HitConfig
{
    /// <summary>常规情况下命中几率最低多少。</summary>
    public required double MinChance
    {
        get;
        init => field = Probability(value, nameof(MinChance));
    }

    /// <summary>常规情况下命中几率最高多少，不得低于 <see cref="MinChance"/>。</summary>
    public required double MaxChance
    {
        get;
        init => field = Probability(value, nameof(MaxChance));
    }

    /// <summary>闪避的距离倍率：第一个数是挨着（隔 1 格）时的，每多隔一格取下一个数，比这一串还远的都取最后一个。</summary>
    /// <remarks>随距离只升不降，desc 那一节写着：降了，退开反而更好打中。</remarks>
    public required IReadOnlyList<double> DodgeByDistance
    {
        get;
        init => field = Ascending(value);
    }

    /// <summary>隔这么多格时，守方的面板闪避率乘多少。</summary>
    /// <param name="distance">隔几格，照走路的数法，至少 1。</param>
    public double DodgeMultiplierAt(int distance)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(distance, 1);
        return DodgeByDistance[Math.Min(distance, DodgeByDistance.Count) - 1];
    }

    /// <summary>上下限对不对得上。读每一格时还看不到另一格，所以整份读完再核。</summary>
    internal HitConfig Checked() => MinChance <= MaxChance
        ? this
        : throw new ArgumentOutOfRangeException(
            nameof(MaxChance), $"{CombatConfig.ContentPath} 的 Hit.MaxChance（{MaxChance}）比 Hit.MinChance（{MinChance}）还低");

    private static double Probability(double value, string field) => value is >= 0 and <= 1
        ? value
        : throw new ArgumentOutOfRangeException(field, $"{CombatConfig.ContentPath} 的 Hit.{field} 要在 0 到 1 之间，实际 {value}");

    private static IReadOnlyList<double> Ascending(IReadOnlyList<double> values)
    {
        const string where = $"{CombatConfig.ContentPath} 的 Hit.{nameof(DodgeByDistance)}";
        if (values is null || values.Count == 0)
        {
            throw new ArgumentException($"{where} 至少要写一个数：挨着时的那一个", nameof(DodgeByDistance));
        }

        if (values.Any(v => !double.IsFinite(v) || v < 0))
        {
            throw new ArgumentOutOfRangeException(nameof(DodgeByDistance), $"{where} 每个数都不得小于 0，实际 [{string.Join(", ", values)}]");
        }

        for (var i = 1; i < values.Count; i++)
        {
            if (values[i] < values[i - 1])
            {
                throw new ArgumentOutOfRangeException(
                    nameof(DodgeByDistance),
                    $"{where} 随距离只升不降，隔 {i + 1} 格的 {values[i]} 比隔 {i} 格的 {values[i - 1]} 小");
            }
        }

        return [.. values];
    }
}

/// <summary>打多少（工作区根 desc/各类公式计算.md 的「伤害」）：防御减免里的 K，与挂着「格挡」时受到的伤害降低几成。</summary>
public sealed record DamageConfig
{
    /// <summary>防御减免 = 有效防御 ÷ (有效防御 + K) 里的那个 K，物理、魔法共用，要为正。</summary>
    public required double DefenseK
    {
        get;
        init => field = double.IsFinite(value) && value > 0
            ? value
            : throw new ArgumentOutOfRangeException(nameof(DefenseK), $"{CombatConfig.ContentPath} 的 Damage.DefenseK 必须为正，实际 {value}");
    }

    /// <summary>挂着「格挡」时受到的伤害降低几成：0.5 就是少一半。在 0 到 1 之间。</summary>
    public required double BlockReduction
    {
        get;
        init => field = value is >= 0 and <= 1
            ? value
            : throw new ArgumentOutOfRangeException(
                nameof(BlockReduction), $"{CombatConfig.ContentPath} 的 Damage.BlockReduction 要在 0 到 1 之间，实际 {value}");
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
