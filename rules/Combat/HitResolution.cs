namespace Tinderhearth.Rules.Combat;
/// <summary>一次命中的反应量：击退距离、硬直帧数、顿帧帧数，以及这一下是不是重击。</summary>
/// <remarks>击退是世界像素的正值距离，往哪边推由调用方按谁打谁决定。</remarks>
public readonly record struct HitReaction(int KnockbackWorldPx, int HitstunFrames, int HitstopFrames, bool IsHeavy);
/// <summary>按攻击轻重取出那一组反应量。它不扣伤害，也不驱动引擎那边的表现。</summary>
public static class HitResolution
{
    /// <summary>结算轻击或重击。待机与未知枚举值都不是命中，拒绝结算。</summary>
    public static HitReaction Resolve(ComboKind weight) => weight switch
    {
        ComboKind.Light => new(CombatFeel.LightKnockbackWorldPx, CombatFeel.LightHitstunFrames,
            CombatFeel.LightHitstopFrames, false),
        ComboKind.Heavy => new(CombatFeel.HeavyKnockbackWorldPx, CombatFeel.HeavyHitstunFrames,
            CombatFeel.HeavyHitstopFrames, true),
        _ => throw new ArgumentOutOfRangeException(nameof(weight), weight, "命中要么是轻攻击、要么是重攻击"),
    };
}
