namespace Tinderhearth.Rules.Economy;

/// <summary>一次收获算出几件要用的那几个量。值不在代码里，由调用方给。</summary>
/// <remarks>
/// 这三个量的值都还没定、要实机试，所以本类型不提供任何默认值 —— 缺配置要当场报错，不能悄悄
/// 兜底。它是 record class 而不是 record struct 正是为了这一条：<c>default</c> 一个 struct 会
/// 绕过下面三处校验拿到全 0，而那正是这里要挡的东西。
///
/// 这三处校验管的是形式而不是值，所以代码里查得出来：件数至少 1、系数下限必须大于零、上限
/// 不得低于下限。下限大于零挡的是「出征十天回来颗粒无收」。
/// </remarks>
/// <param name="BaseCount">浇满水时收几件。</param>
/// <param name="MinWaterFactor">一次没浇时的产量系数，必须大于零。</param>
/// <param name="MaxWaterFactor">浇满时的产量系数，不得低于下限。</param>
public sealed record HarvestYieldRule(
    int BaseCount,
    double MinWaterFactor,
    double MaxWaterFactor)
{
    /// <inheritdoc cref="HarvestYieldRule(int, double, double)"/>
    public int BaseCount { get; } = BaseCount >= 1
        ? BaseCount
        : throw new ArgumentOutOfRangeException(
            nameof(BaseCount), $"{nameof(BaseCount)} 至少为 1，实际 {BaseCount}");

    /// <inheritdoc cref="HarvestYieldRule(int, double, double)"/>
    public double MinWaterFactor { get; } = MinWaterFactor > 0
        ? MinWaterFactor
        : throw new ArgumentOutOfRangeException(
            nameof(MinWaterFactor),
            $"{nameof(MinWaterFactor)} 必须大于零，实际 {MinWaterFactor}"
                + "（等于零就是一次没浇便颗粒无收，那是在罚离家出征的玩家）");

    /// <inheritdoc cref="HarvestYieldRule(int, double, double)"/>
    public double MaxWaterFactor { get; } = MaxWaterFactor >= MinWaterFactor
        ? MaxWaterFactor
        : throw new ArgumentOutOfRangeException(
            nameof(MaxWaterFactor),
            $"{nameof(MaxWaterFactor)} 不得低于 {nameof(MinWaterFactor)}"
                + $"，实际上限 {MaxWaterFactor}、下限 {MinWaterFactor}");
}
