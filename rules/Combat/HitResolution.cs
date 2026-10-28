namespace Tinderhearth.Rules.Combat;
/// <summary>一招打得轻还是重。命中反应按它取那一组，伤害不读它。</summary>
/// <remarks>
/// 轻重是一招自己声明的一格，不是「按了哪个键」：技能与普攻的每一段各填一格（设计仓
/// design/伤害系统.md 的「硬直与失衡：本页只产出量，不管姿态」）。它只管被打得多晃 —— 硬直、击退、
/// 顿帧，以及表现上震屏、火花与受击动画取哪一套；疼多少是倍率的事。
/// </remarks>
public enum HitWeight
{
    /// <summary>轻：不击退，普攻连段才留得在射程里。</summary>
    Light,

    /// <summary>重：击退、硬直与顿帧都比轻的长，表现也换成重的那一套。</summary>
    Heavy,
}
/// <summary>一次命中的反应量：击退距离、硬直帧数、顿帧帧数，以及这一下是不是重的。</summary>
/// <remarks>击退是世界像素的正值距离，往哪边推由调用方按谁打谁决定。</remarks>
public readonly record struct HitReaction(int KnockbackWorldPx, int HitstunFrames, int HitstopFrames, bool IsHeavy);
/// <summary>按这一招的轻重取出那一组反应量。它不扣伤害，也不驱动引擎那边的表现。</summary>
public static class HitResolution
{
    /// <summary>按轻重结算。不认识的取值拒绝结算。</summary>
    public static HitReaction Resolve(HitWeight weight) => weight switch
    {
        HitWeight.Light => new(CombatFeel.LightKnockbackWorldPx, CombatFeel.LightHitstunFrames,
            CombatFeel.LightHitstopFrames, false),
        HitWeight.Heavy => new(CombatFeel.HeavyKnockbackWorldPx, CombatFeel.HeavyHitstunFrames,
            CombatFeel.HeavyHitstopFrames, true),
        _ => throw new ArgumentOutOfRangeException(nameof(weight), weight, "一招要么是轻的、要么是重的"),
    };
}
