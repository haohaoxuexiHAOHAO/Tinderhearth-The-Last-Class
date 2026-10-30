namespace Tinderhearth.Rules.Economy;

/// <summary>一格地存档里记的那几项：这台状态机的全部状态，一项不多、一项不少。</summary>
/// <remarks>
/// 存什么的清单在设计仓 design/地块系统.md 的「一格要存什么」一节。这里与 <see cref="Plot"/> 的公开属性一一对应，
/// 有测试盯着：给地块加一项状态而不加进这里，那条测试失败。
/// </remarks>
public readonly record struct PlotRecord(
    PlotState State,
    string? CropId,
    int Stage,
    int DaysInStage,
    bool WateredToday,
    int WateredDaysThisCrop,
    int DaysThisCrop,
    int DaysFallow,
    bool Mutated);
