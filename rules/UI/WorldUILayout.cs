using Tinderhearth.Rules.Foundation.Actors;

namespace Tinderhearth.Rules.UI;

/// <summary>
/// 世界空间 UI 的几何：读条圆环、当前操作格、精英血条、伤害数字的尺寸与偏移，单位是世界像素。
/// </summary>
/// <remarks>
/// 与 <see cref="HudLayout"/>、<see cref="UIMetrics"/> 同理放在规则层：这些数之间的关系能用单元
/// 测试盯住（圆环与画好的那张一样大、居中不落半格，血条按目标报的头顶摆、宽按体型档给），引擎层于是一个数字字面量都不用写。
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

    // ── 当前操作格（勾在俯视那一格地外面）────────────────────────────
    /// <summary>
    /// 当前操作格那两张比一格地大一圈：四边各往外这么多。照画好的 <c>world/cell_usable</c> 与 <c>world/cell_blocked</c>，
    /// 有测试拿素材清单对它。
    /// </summary>
    public const int CellCursorOutset = 1;

    /// <summary>一格地边长 <paramref name="tileSide"/> 时，当前操作格那两张该多大：两边各大出 <see cref="CellCursorOutset"/>。</summary>
    public static int CellCursorSide(int tileSide) => tileSide + (CellCursorOutset * 2);

    // ── 精英 / BOSS 血条（浮在目标报的头顶上方）────────────────────────
    /// <summary>血条高：与 HUD 资源条同一个数，两处共用一套槽与填充（<c>hud/bar_track</c> 那一套）。</summary>
    public const int EliteBarHeight = HudLayout.GaugeBarHeight;

    /// <summary>血条宽，按目标的体型档给：小与人形一档，大体型一档。</summary>
    /// <remarks>
    /// 人形取剪影宽的上限（设计仓 canon/gameplay/玩法定位.md 的「像素基准」：侧视本体宽不超过 32）。大体型取 HUD 资源条
    /// 那么长：剪影宽出一截，条也长一档，而且与玩家自己那几根一样长时，一下掉了多少比得出来。小的不另窄一档：再窄，
    /// 一像素就代表太多血，看不出掉了多少。
    /// </remarks>
    public static int EliteBarWidthOf(BodyTier tier) => tier switch
    {
        BodyTier.Small or BodyTier.Human => UIMetrics.IconLarge,
        BodyTier.Large => HudLayout.GaugeBarWidth,
        _ => throw new ArgumentOutOfRangeException(nameof(tier), tier, "体型档没报，血条给不出宽"),
    };

    /// <summary>血条左边相对目标脚底的横向偏移：横着居中在脚底上。两档宽都是偶数，挪半根仍在整像素上（有测试判）。</summary>
    public static int EliteBarLeftX(BodyTier tier) => -(EliteBarWidthOf(tier) / 2);

    /// <summary>
    /// 血条顶边相对目标脚底的纵向偏移，向上为负：头顶往上空一个组内间距，再往上一根条高，整根浮在头顶以上。
    /// </summary>
    public static int EliteBarTopY(WorldUIAnchor anchor) =>
        -(anchor.HeadHeightWorldPx + UIMetrics.ItemGap + EliteBarHeight);

    // ── 伤害数字（从头顶冒出、向上飘一段后消失）────────────────────────
    /// <summary>伤害数字起始的纵向偏移，向上为负：正好在目标报的头顶。</summary>
    public static int DamageStartOffsetY(WorldUIAnchor anchor) => -anchor.HeadHeightWorldPx;

    /// <summary>飘升距离，一个基础单位。</summary>
    public const int DamageFloatDistancePx = UIMetrics.BaseUnit;

    /// <summary>
    /// 飘字活多久，单位是秒。它是表现时长不是玩法数值，短到够看清一次跳字就行。到期直接消失，
    /// 不做半透明淡出 —— 透明度只用全透或全不透，见设计仓 production/像素绘制原则.md 的
    /// 「硬边、抗锯齿与点绘」一节。
    /// </summary>
    public const double DamageLifetimeSeconds = 0.6;

    /// <summary>
    /// 飘字在生命周期某一刻相对目标脚底的纵向偏移。<paramref name="lifeFraction"/> 是 0 到 1 的已过比例；
    /// 从 <see cref="DamageStartOffsetY"/> 线性上升 <see cref="DamageFloatDistancePx"/>。
    /// </summary>
    public static double DamageRiseAt(double lifeFraction, WorldUIAnchor anchor) =>
        DamageStartOffsetY(anchor) - DamageFloatDistancePx * System.Math.Clamp(lifeFraction, 0.0, 1.0);
}
