using Tinderhearth.Rules.Progression;

namespace Tinderhearth.Rules.Combat;

/// <summary>一个角色在战斗里的面板，从四项属性按配置算出来。</summary>
/// <remarks>
/// 哪一项看哪一项属性照工作区根 desc/各类公式计算.md 的「属性换算成面板」那张表；初始值与每点给多少在配置里
/// （<see cref="CombatPanelConfig"/>）。宝物、天赋与状态给的加在算出来的数上，等它们落地时再加。
///
/// 点数类的几项（HP、MP、攻击、防御、韧性）向下取整成整数；几率与倍率留小数，1 就是 100%。
/// 每一项的上限（工作区根 desc/角色面板.md 的「面板上的每一项」）这里不截：占位的数离上限还远，截的规矩跟着正式数值一起定。
/// </remarks>
public sealed record CombatPanel(
    int HpMax,
    int MpMax,
    int HpRegen,
    int MpRegen,
    int PhysicalAttack,
    int MagicAttack,
    int PhysicalDefense,
    int MagicDefense,
    int ToughnessMax,
    double HitRate,
    double DodgeRate,
    double PhysicalCritChance,
    double MagicCritChance,
    double PhysicalCritMultiplier,
    double MagicCritMultiplier)
{
    /// <summary>照那张表从属性算出面板。</summary>
    public static CombatPanel From(CharacterAttributes attributes, CombatPanelConfig config)
    {
        ArgumentNullException.ThrowIfNull(attributes);
        ArgumentNullException.ThrowIfNull(config);
        return new(
            HpMax: Whole(config.HpMax, attributes.Constitution),
            MpMax: Whole(config.MpMax, attributes.Intellect),
            HpRegen: Whole(config.HpRegen, attributes.Constitution),
            MpRegen: Whole(config.MpRegen, attributes.Intellect),
            PhysicalAttack: Whole(config.PhysicalAttack, attributes.Strength),
            MagicAttack: Whole(config.MagicAttack, attributes.Intellect),
            PhysicalDefense: Whole(config.PhysicalDefense, attributes.Constitution),
            MagicDefense: Whole(config.MagicDefense, attributes.Constitution),
            ToughnessMax: Whole(config.ToughnessMax, attributes.Constitution),
            HitRate: config.HitRate.At(attributes.Agility),
            DodgeRate: config.DodgeRate.At(attributes.Agility),
            PhysicalCritChance: config.PhysicalCritChance.At(attributes.Agility),
            MagicCritChance: config.MagicCritChance.At(attributes.Agility),
            PhysicalCritMultiplier: config.PhysicalCritMultiplier.At(attributes.Strength),
            MagicCritMultiplier: config.MagicCritMultiplier.At(attributes.Intellect));
    }

    private static int Whole(PanelConversion conversion, int points) => CombatRounding.Floor(conversion.At(points));
}
