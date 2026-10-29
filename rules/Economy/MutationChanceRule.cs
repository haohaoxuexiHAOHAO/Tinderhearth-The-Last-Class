namespace Tinderhearth.Rules.Economy;

/// <summary>作物长成那一下掷变异用的几率：基础值加修饰之和，再钳在上限以内。值不在代码里，由调用方给。</summary>
/// <remarks>
/// 合成规则与三条校验的理由在设计仓 design/地块系统.md 的「变异几率：一个基础值，加事件与设备两路修饰」一节，
/// 本类型只执行。两个量的值都还没定、要实机种过几季才校得准，所以没有默认值；它是 record class 而不是
/// struct，理由同 <see cref="HarvestYieldRule"/>：<c>default</c> 一个 struct 会绕过校验拿到全 0。
///
/// 修饰从哪来（随机事件、田里的设备）不在这里，调用方把作用在这一格上的那几个数交进来。
/// </remarks>
/// <param name="BaseChance">没有任何修饰时的几率，全库一个。必须大于零、不大于上限。</param>
/// <param name="Cap">加了修饰之后最多到多少。必须小于 1。</param>
public sealed record MutationChanceRule(double BaseChance, double Cap)
{
    /// <inheritdoc cref="MutationChanceRule(double, double)"/>
    public double BaseChance { get; } = BaseChance switch
    {
        not > 0 => throw new ArgumentOutOfRangeException(
            nameof(BaseChance),
            $"变异几率的 {nameof(BaseChance)} 必须大于零，实际 {BaseChance}"
                + "（为零就是基地的地自己从不变异；没填也是零）"),
        _ when BaseChance > Cap => throw new ArgumentOutOfRangeException(
            nameof(BaseChance),
            $"变异几率的 {nameof(BaseChance)} 不得大于 {nameof(Cap)}，实际基础值 {BaseChance}、上限 {Cap}"),
        _ => BaseChance,
    };

    /// <inheritdoc cref="MutationChanceRule(double, double)"/>
    public double Cap { get; } = Cap < 1
        ? Cap
        : throw new ArgumentOutOfRangeException(
            nameof(Cap),
            $"变异几率的 {nameof(Cap)} 必须小于 1，实际 {Cap}"
                + "（到了 1，修饰叠满之后就是一道保证出变异的工序）");

    /// <summary>这一次掷的几率：基础值加修饰之和，再钳在上限以内。没有修饰时就是基础值。</summary>
    /// <param name="modifiers">作用在这一格上的修饰：事件那一路与覆盖到这一格的设备，各自不得为负。</param>
    public double ChanceWith(IEnumerable<double> modifiers)
    {
        ArgumentNullException.ThrowIfNull(modifiers);

        var sum = 0.0;
        foreach (var modifier in modifiers)
        {
            sum += modifier >= 0
                ? modifier
                : throw new ArgumentOutOfRangeException(
                    nameof(modifiers),
                    $"变异几率的修饰不得为负，实际 {modifier}：事件与设备那两路只往上调");
        }

        return Math.Min(BaseChance + sum, Cap);
    }
}
