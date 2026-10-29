using Tinderhearth.Rules.Economy;
using Xunit;

namespace Tinderhearth.Rules.Tests.Economy;

/// <summary>变异几率：基础值加修饰之和、钳在上限以内；三条形式校验与负修饰各自当场被拒。</summary>
/// <remarks>
/// 这里的数只是测试用的，全取二进制下精确的小数，相等比较才不受浮点误差干扰。基础值与上限的真值还没定，
/// 归设计仓 design/数值模型.md 的尚未给值表。
/// </remarks>
public class MutationChanceRuleTests
{
    [Fact]
    public void 没有修饰时就是基础值()
    {
        Assert.Equal(0.125, new MutationChanceRule(0.125, 0.5).ChanceWith([]));
    }

    [Fact]
    public void 几率是基础值加修饰之和()
    {
        Assert.Equal(0.4375, new MutationChanceRule(0.125, 0.75).ChanceWith([0.25, 0.0625]));
    }

    /// <summary>修饰叠得再多也停在上限。没有这一条，几台设备盖在同一格上就能把几率推到必中。</summary>
    [Fact]
    public void 加了修饰之后钳在上限以内()
    {
        Assert.Equal(0.5, new MutationChanceRule(0.125, 0.5).ChanceWith([0.25, 0.25, 0.25]));
    }

    /// <summary>上限本身取得到：钳住的是「超过」，不是「到达」。</summary>
    [Fact]
    public void 正好加到上限时就是上限()
    {
        Assert.Equal(0.5, new MutationChanceRule(0.25, 0.5).ChanceWith([0.25]));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-0.125)]
    [InlineData(double.NaN)]
    public void 基础值不大于零被拒(double baseChance)
    {
        var error = Assert.Throws<ArgumentOutOfRangeException>(() => new MutationChanceRule(baseChance, 0.5));

        Assert.Equal(nameof(MutationChanceRule.BaseChance), error.ParamName);
        Assert.Contains("必须大于零", error.Message);
    }

    /// <summary>两格都没填就是两个零。它在检查器里的样子与「填了零」一样，所以同一条拒绝要把它拦下。</summary>
    [Fact]
    public void 两格都没填时当场被拒()
    {
        var error = Assert.Throws<ArgumentOutOfRangeException>(() => new MutationChanceRule(0, 0));

        Assert.Equal(nameof(MutationChanceRule.BaseChance), error.ParamName);
    }

    [Fact]
    public void 基础值大于上限被拒()
    {
        var error = Assert.Throws<ArgumentOutOfRangeException>(() => new MutationChanceRule(0.75, 0.5));

        Assert.Equal(nameof(MutationChanceRule.BaseChance), error.ParamName);
        Assert.Contains("不得大于", error.Message);
    }

    /// <summary>基础值等于上限照旧成立：那只是说修饰加不上去了，不是填错。</summary>
    [Fact]
    public void 基础值等于上限照旧成立()
    {
        Assert.Equal(0.5, new MutationChanceRule(0.5, 0.5).ChanceWith([0.25]));
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(1.5)]
    [InlineData(double.NaN)]
    public void 上限不小于一被拒(double cap)
    {
        var error = Assert.Throws<ArgumentOutOfRangeException>(() => new MutationChanceRule(0.5, cap));

        Assert.Equal(nameof(MutationChanceRule.Cap), error.ParamName);
        Assert.Contains("必须小于 1", error.Message);
    }

    /// <summary>事件与设备那两路只往上调。负的修饰不报错的话，它会悄悄把基础值压到「从不变异」。</summary>
    [Theory]
    [InlineData(-0.0625)]
    [InlineData(double.NaN)]
    public void 负的修饰被拒(double bad)
    {
        var rule = new MutationChanceRule(0.125, 0.5);

        var error = Assert.Throws<ArgumentOutOfRangeException>(() => rule.ChanceWith([0.25, bad]));

        Assert.Contains("不得为负", error.Message);
    }
}
