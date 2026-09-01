namespace Tinderhearth.Rules.Ui;

/// <summary>
/// 世界空间 UI 的几何（`UI-9`）：读条圆环、精英血条、伤害数字的尺寸与相对执行者的偏移。
/// </summary>
/// <remarks>
/// 为什么放规则层：与 <see cref="HudLayout"/>、<see cref="UiMetrics"/> 同理 —— 这些数之间有
/// **可以判死的关系**（圆环直径等于占位件、血条宽对齐剪影、进度弧半径落在墨环内侧），放这里
/// 就能用单元测试盯住，引擎层于是不写任何数字字面量。
///
/// 这里全是**排版与表现量**，不是玩法数值：线宽、飘字距离、飘字时长与 <see cref="CameraFeel"/>
/// 的震动幅度同类 —— 属呈现规则，与 `GP-2` 无关，也不外置成配置（mod 不该改表现）。
///
/// 世界空间的单位是**世界像素**：侧视 2 倍缩放后每个世界像素占 2 个物理像素，与精灵同步放大，
/// 所以这里的数不乘缩放（缩放由 <see cref="UiLayer.WorldSpace"/> 那层的 FollowViewport 承担）。
/// </remarks>
public static class WorldUiLayout
{
    // ── 读条圆环（画在执行者身上，居中）──────────────────────────────
    /// <summary>圆环直径。与占位件 `cast-ring.png` 同尺寸（16×16），替换时不必改代码。</summary>
    public const int RingDiameter = UiMetrics.IconSmall;

    /// <summary>圆环半径。</summary>
    public static int RingRadius => RingDiameter / 2;

    /// <summary>进度弧线宽。2px 让它在 12px 基准、2 倍缩放下看得清一圈进度。</summary>
    public const int ArcWidthPx = 2;

    /// <summary>进度弧半径。落在墨环内侧一格，压着底环画而不超出圆外。</summary>
    public static int ArcRadius => RingRadius - 1;

    /// <summary>进度弧的分段数。够让一整圈看起来是圆的，不必更多。</summary>
    public const int ArcSegments = 24;

    // ── 精英 / BOSS 血条（贴在剪影头顶上方）──────────────────────────
    /// <summary>血条宽。对齐 32px 剪影，一眼看出是这个敌人的血。</summary>
    public const int EliteBarWidth = UiMetrics.IconLarge;

    /// <summary>血条高。取内边距量级（4px），与屏幕空间资源条同一视觉重量。</summary>
    public const int EliteBarHeight = UiMetrics.PanelPadding;

    /// <summary>
    /// 血条相对剪影中心的纵向偏移（世界像素，向上为负）：半个剪影 + 一格间距 + 血条本身，
    /// 让它浮在头顶、不压住 32px 剪影的可读区。
    /// </summary>
    public static int EliteBarOffsetY =>
        -(UiMetrics.IconLarge / 2 + UiMetrics.ItemGap + EliteBarHeight);

    // ── 伤害数字（从头顶冒出、向上飘一段后消失）────────────────────────
    /// <summary>伤害数字起始纵向偏移（世界像素，向上为负）：正好在头顶。</summary>
    public static int DamageStartOffsetY => -(UiMetrics.IconLarge / 2);

    /// <summary>飘升距离：一个基础单位（16 世界像素）。</summary>
    public const int DamageFloatDistancePx = UiMetrics.BaseUnit;

    /// <summary>
    /// 飘字生命周期（秒）。表现时长，不是玩法数值 —— 短到够看清一次跳字。到期直接消失，
    /// **不做半透明淡出**（像素绘制原则 §9：透明度只用全透或全不透）。
    /// </summary>
    public const double DamageLifetimeSeconds = 0.6;

    /// <summary>
    /// 飘字在生命周期某一刻的纵向偏移（世界像素）。<paramref name="lifeFraction"/> 是 0..1 的
    /// 已过比例；从 <see cref="DamageStartOffsetY"/> 线性上升 <see cref="DamageFloatDistancePx"/>。
    /// </summary>
    public static double DamageRiseAt(double lifeFraction) =>
        DamageStartOffsetY - DamageFloatDistancePx * System.Math.Clamp(lifeFraction, 0.0, 1.0);
}
