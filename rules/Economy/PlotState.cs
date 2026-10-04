namespace Tinderhearth.Rules.Economy;

/// <summary>一格地此刻处在哪个状态。</summary>
/// <remarks>
/// 每条流转的触发条件在设计仓 design/地块系统.md 的「一格地的状态机」一节，本枚举不复述。
///
/// <see cref="Cleared"/> 与 <see cref="Tilled"/> 刻意分成两个取值：清干净的那一格能盖房但种
/// 不了，锄过的才能播种。不要把它们合成一个，理由在那一份的「理由与取舍」一节。
/// </remarks>
public enum PlotState
{
    /// <summary>未清理：上面有石头、树木或杂草。既种不了也盖不了房。</summary>
    Uncleared,

    /// <summary>可耕：清干净了。能盖房，但不能播种 —— 播种要先锄。</summary>
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
