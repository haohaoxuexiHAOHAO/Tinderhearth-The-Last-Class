namespace Tinderhearth.Rules.Combat;

/// <summary>命中反应；击退为世界像素距离的正值，方向由调用方施加。</summary>
public readonly record struct HitReaction(int KnockbackWorldPx, int HitstunFrames, int HitstopFrames, bool IsHeavy);

/// <summary>由攻击轻重生成反应，不施加伤害、不驱动引擎表现。</summary>
public static class HitResolution
{
    /// <summary>结算轻重攻击；待机与未知枚举值不是命中，拒绝结算。</summary>
    public static HitReaction Resolve(ComboKind weight) => weight switch
    {
        ComboKind.Light => new(CombatFeel.LightKnockbackWorldPx, CombatFeel.LightHitstunFrames,
            CombatFeel.LightHitstopFrames, false),
        ComboKind.Heavy => new(CombatFeel.HeavyKnockbackWorldPx, CombatFeel.HeavyHitstunFrames,
            CombatFeel.HeavyHitstopFrames, true),
        _ => throw new ArgumentOutOfRangeException(nameof(weight), weight, "命中需要轻或重攻击"),
    };
}
