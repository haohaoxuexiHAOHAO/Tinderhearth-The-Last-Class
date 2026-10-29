namespace Tinderhearth.Rules.UI;

/// <summary>
/// 世界空间 UI 的几何：读条圆环、精英血条、伤害数字的尺寸与相对执行者的偏移，单位是世界像素。
/// </summary>
/// <remarks>
/// 与 <see cref="HudLayout"/>、<see cref="UIMetrics"/> 同理放在规则层：这些数之间的关系能用单元
/// 测试盯住（圆环与画好的那张一样大、居中不落半格，血条宽对齐剪影），引擎层于是一个数字字面量都不用写。
///
/// 这里全是排版与表现量，不是玩法数值 —— 线宽、飘字距离、飘字时长与 <see cref="CameraFeel"/>
/// 的震动幅度同类，所以也不外置成配置文件，mod 不该改表现。
///
/// 这些数不乘相机缩放：放大由世界空间那一层的 FollowViewport 承担，世界空间 UI 于是和精灵同步
/// 放大，与相机缩放多少倍无关。
/// </remarks>
public static class WorldUILayout
{
    // ── 读条圆环（画在执行者身上，居中）──────────────────────────────
    /// <summary>读条圆环的宽。照画好的那张 <c>world/ring_cast</c>，有测试拿素材清单对它。</summary>
    public const int RingWidth = 14;

    /// <summary>读条圆环的高：小图标那一档。照画好的那张 <c>world/ring_cast</c>，有测试拿素材清单对它。</summary>
    public const int RingHeight = UIMetrics.IconSmall;

    // ── 精英 / BOSS 血条（贴在剪影头顶上方）──────────────────────────
    /// <summary>血条宽。对齐剪影宽度，一眼看出是这个敌人的血。</summary>
    public const int EliteBarWidth = UIMetrics.IconLarge;

    /// <summary>血条高。取面板内边距那一档，与屏幕空间的资源条看起来一样粗。</summary>
    public const int EliteBarHeight = UIMetrics.PanelPadding;

    /// <summary>
    /// 血条相对剪影中心的纵向偏移，向上为负：半个剪影加一格间距加血条本身，
    /// 让它浮在头顶、不压住剪影上要看清的那部分。
    /// </summary>
    public static int EliteBarOffsetY =>
        -(UIMetrics.IconLarge / 2 + UIMetrics.ItemGap + EliteBarHeight);

    // ── 伤害数字（从头顶冒出、向上飘一段后消失）────────────────────────
    /// <summary>伤害数字起始的纵向偏移，向上为负：正好在头顶。</summary>
    public static int DamageStartOffsetY => -(UIMetrics.IconLarge / 2);

    /// <summary>飘升距离，一个基础单位。</summary>
    public const int DamageFloatDistancePx = UIMetrics.BaseUnit;

    /// <summary>
    /// 飘字活多久，单位是秒。它是表现时长不是玩法数值，短到够看清一次跳字就行。到期直接消失，
    /// 不做半透明淡出 —— 透明度只用全透或全不透，见设计仓 production/像素绘制原则.md 的
    /// 「硬边、抗锯齿与点绘」一节。
    /// </summary>
    public const double DamageLifetimeSeconds = 0.6;

    /// <summary>
    /// 飘字在生命周期某一刻的纵向偏移。<paramref name="lifeFraction"/> 是 0 到 1 的已过比例；
    /// 从 <see cref="DamageStartOffsetY"/> 线性上升 <see cref="DamageFloatDistancePx"/>。
    /// </summary>
    public static double DamageRiseAt(double lifeFraction) =>
        DamageStartOffsetY - DamageFloatDistancePx * System.Math.Clamp(lifeFraction, 0.0, 1.0);
}
