namespace Tinderhearth.Rules.Foundation.Actors;

/// <summary>
/// 侧视本体的体型档：小、人、大。每一档多高在设计仓 canon/gameplay/玩法定位.md 的「像素基准」。
/// </summary>
/// <remarks>
/// 它放在角色这一层，因为读它的不只界面：世界那一层的血条按它给宽，侧视影子也按体型分套。
///
/// 第一个取值是「没报」，默认值就是它：忘了填的角色拿去摆任何东西都当场报错，不悄悄当成人形。
/// </remarks>
public enum BodyTier
{
    /// <summary>没报。拿它去摆任何东西都当场报错。</summary>
    Unset,

    /// <summary>小。</summary>
    Small,

    /// <summary>人。</summary>
    Human,

    /// <summary>大。</summary>
    Large,
}
