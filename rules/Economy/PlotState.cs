namespace Tinderhearth.Rules.Economy;

/// <summary>一格地此刻处在哪个状态。</summary>
/// <remarks>
/// 状态与每条流转的权威是设计仓 `design/地块系统.md` 那张图与紧跟它的那张表，本枚举不复述条件。
///
/// **<see cref="Cleared"/> 与 <see cref="Tilled"/> 必须是两个取值。** 清干净的那一格能盖房但
/// 种不了，锄过的才能播种。合成一个取值的后果不是少一个枚举值，而是玩家清出一片地就直接得到
/// 一片犁开的土 —— 然后把房子盖在犁沟上，而他那时还没决定要不要种地。
/// </remarks>
public enum PlotState
{
    /// <summary>未清理：上面有石头、树木或杂草。既种不了也盖不了房。</summary>
    Uncleared,

    /// <summary>可耕：清干净了。能盖房，**不能播种** —— 播种要先锄。</summary>
    Cleared,

    /// <summary>已锄：能播种。空着满若干天退回 <see cref="Cleared"/>。</summary>
    Tilled,

    /// <summary>已播种：作物在长，还没到成熟那一阶段。</summary>
    Planted,

    /// <summary>待收：到了成熟那一阶段。</summary>
    Harvestable,

    /// <summary>待清：枯株还在地里，清掉才回到 <see cref="Tilled"/>。</summary>
    Withered,
}
