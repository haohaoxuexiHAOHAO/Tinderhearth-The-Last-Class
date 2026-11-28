using Tinderhearth.Rules.Progression;

namespace Tinderhearth.Rules.Combat;

/// <summary>一招打一个人之前就算得出的那几个数：打中的几率、打中之后暴击的几率、不暴击打多少、暴击打多少。</summary>
/// <remarks>
/// 照工作区根 desc/各类公式计算.md 的「命中」「伤害」「暴击」算。瞄准时写的与真打时用的是同一份：真打时先掷命中，
/// 打中了再掷暴击，打多少就取这里的一个数，所以写出来的数与打出来的对得上。
///
/// 守方的暴击避免全部来自宝物、宝石、天赋与状态（同一份 desc 的「属性换算成面板」），现在都还没有，所以是 0；地形修正也还没有。
/// </remarks>
/// <param name="HitChance">打中的几率，1 就是 100%，已经限制在常规的上下限之间。</param>
/// <param name="CriticalChance">打中之后暴击的几率，限制在 0 到 1 之间。</param>
/// <param name="Damage">打中、不暴击时打多少，至少 1。瞄准时写的就是它。</param>
/// <param name="CriticalDamage">打中、暴击时打多少，至少 1。</param>
public sealed record StrikeOdds(double HitChance, double CriticalChance, int Damage, int CriticalDamage)
{
    /// <summary>算一次。</summary>
    /// <param name="attacker">出手的人的面板。</param>
    /// <param name="defender">挨打的人的面板。</param>
    /// <param name="defenderBlocking">挨打的人挂没挂着「格挡」。</param>
    /// <param name="skill">放的那一招，要是打人的招。</param>
    /// <param name="distance">两人隔几格，照走路的数法，至少 1。</param>
    /// <param name="config">战斗那一份配置。</param>
    public static StrikeOdds Compute(
        CombatPanel attacker,
        CombatPanel defender,
        bool defenderBlocking,
        SkillDefinition skill,
        int distance,
        CombatConfig config)
    {
        ArgumentNullException.ThrowIfNull(attacker);
        ArgumentNullException.ThrowIfNull(defender);
        ArgumentNullException.ThrowIfNull(skill);
        ArgumentNullException.ThrowIfNull(config);
        if (skill.DamageKind is not { } kind || skill.Multiplier is not { } multiplier || skill.Penetration is not { } penetration)
        {
            throw new ArgumentException($"技能 {skill.Id} 不打人，算不出打多少", nameof(skill));
        }

        var physical = kind == DamageKind.Physical;
        var dodge = defender.DodgeRate * config.Hit.DodgeMultiplierAt(distance);
        var hitChance = Math.Clamp(attacker.HitRate - dodge, config.Hit.MinChance, config.Hit.MaxChance);
        var criticalChance = Math.Clamp(physical ? attacker.PhysicalCritChance : attacker.MagicCritChance, 0, 1);

        // 先后照「伤害」那一节：技能倍率 → 防御减免 → 暴击 → 其他增减，算完才取整。
        double effectiveDefense = Math.Max(0, (physical ? defender.PhysicalDefense : defender.MagicDefense) - penetration);
        var reduction = effectiveDefense / (effectiveDefense + config.Damage.DefenseK);
        var beforeCritical = (physical ? attacker.PhysicalAttack : attacker.MagicAttack) * multiplier * (1 - reduction);
        var critical = physical ? attacker.PhysicalCritMultiplier : attacker.MagicCritMultiplier;
        var others = defenderBlocking ? 1 - config.Damage.BlockReduction : 1;
        return new(
            HitChance: hitChance,
            CriticalChance: criticalChance,
            Damage: AtLeastOne(beforeCritical * others),
            CriticalDamage: AtLeastOne(beforeCritical * critical * others));
    }

    private static int AtLeastOne(double damage) => Math.Max(1, CombatRounding.Floor(damage));
}
