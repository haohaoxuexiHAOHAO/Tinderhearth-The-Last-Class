using Tinderhearth.Rules.Economy;
using Xunit;

namespace Tinderhearth.Rules.Tests.Economy;

/// <summary>作物长成那一晚掷一次变异：掷中的那一株待收时一直是变异的，收掉或枯死时清掉，下一茬重掷。</summary>
/// <remarks>
/// 随机源全部注入：必中、必不中、按顺序给、或数次数，所以每一条都可重复。几率怎么算不在这里，见
/// <see cref="MutationChanceRuleTests"/>。规则的家在设计仓 design/地块系统.md 与 decisions/ADR-0030-作物成熟时按几率变异.md。
/// </remarks>
public class CropMutationTests
{
    private const int FallowDays = 7;

    private static readonly HarvestYieldRule Yield = new(BaseCount: 10, MinWaterFactor: 0.5, MaxWaterFactor: 1.0);

    private static CropDefinition Crop(int? regrowFromStage = null) => new()
    {
        Id = "turnip",
        StageDays = [1, 1, 1],
        RegrowFromStage = regrowFromStage,
        Seasons = [Season.Spring],
        SeedItemId = "item_turnip_seed",
        YieldItemId = "item_turnip",
    };

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void 长成那一晚按掷出的结果变或不变(bool hit)
    {
        var crop = Crop();
        var plot = Planted(crop);

        GrowDays(plot, crop, crop.DaysToFirstRipe, () => hit);

        Assert.Equal(PlotState.Harvestable, plot.State);
        Assert.Equal(hit, plot.Mutated);
    }

    /// <summary>没到成熟的那几晚一次都不掷，必中也一样；长成之后在地里放几天也不再掷。</summary>
    /// <remarks>每晚都掷的话，熟了不收、多放几天就能多掷几次，而长得慢的作物也更容易变。</remarks>
    [Fact]
    public void 一茬只在长成那一晚掷一次()
    {
        var crop = Crop();
        var plot = Planted(crop);
        var rolls = 0;
        bool Roll()
        {
            rolls++;
            return true;
        }

        GrowDays(plot, crop, crop.DaysToFirstRipe - 1, Roll);
        Assert.Equal(0, rolls);
        Assert.False(plot.Mutated);

        GrowDays(plot, crop, 1, Roll);
        Assert.Equal(1, rolls);
        Assert.True(plot.Mutated);

        GrowDays(plot, crop, 3, Roll);
        Assert.Equal(1, rolls);
        Assert.True(plot.Mutated);
    }

    /// <summary>能反复收的作物收完那一位清掉，下一次长成重掷：先中后不中，两茬一变一不变。</summary>
    [Fact]
    public void 循环作物下一茬重掷而不继承上一茬()
    {
        var crop = Crop(regrowFromStage: 1);
        var plot = Planted(crop);
        var results = new Queue<bool>([true, false]);

        GrowDays(plot, crop, crop.DaysToFirstRipe, results.Dequeue);
        Assert.True(plot.TryHarvest(crop, Yield, out var first));
        Assert.True(first.Mutated);
        Assert.False(plot.Mutated);
        Assert.Equal(PlotState.Planted, plot.State);

        GrowDays(plot, crop, crop.DaysToRegrow!.Value, results.Dequeue);
        Assert.Equal(PlotState.Harvestable, plot.State);
        Assert.False(plot.Mutated);
        Assert.True(plot.TryHarvest(crop, Yield, out var second));
        Assert.False(second.Mutated);
        Assert.Empty(results);
    }

    /// <summary>换季枯死清掉那一位；清掉枯株之后种下一株，那一位不留给它。</summary>
    [Fact]
    public void 换季枯死与清掉枯株都清掉那一位()
    {
        var crop = Crop();
        var plot = Planted(crop);
        GrowDays(plot, crop, crop.DaysToFirstRipe, () => true);
        Assert.True(plot.Mutated);

        plot.ApplySeasonChange(crop, Season.Summer);
        Assert.Equal(PlotState.Withered, plot.State);
        Assert.False(plot.Mutated);

        Assert.True(plot.ClearWithered());
        Assert.False(plot.Mutated);

        Assert.True(plot.Plant(crop));
        GrowDays(plot, crop, crop.DaysToFirstRipe, () => false);
        Assert.False(plot.Mutated);
    }

    /// <summary>「变异」只在待收那一态为真：必中的一整茬里每走一步都核一遍。</summary>
    [Fact]
    public void 变异只在待收那一态为真()
    {
        var crop = Crop();
        var plot = new Plot(PlotState.Tilled);
        AssertOnlyWhenHarvestable(plot);

        Assert.True(plot.Plant(crop));
        AssertOnlyWhenHarvestable(plot);
        for (var day = 0; day < crop.DaysToFirstRipe + 2; day++)
        {
            plot.AdvanceDay(crop, FallowDays, () => true);
            AssertOnlyWhenHarvestable(plot);
        }

        Assert.True(plot.TryHarvest(crop, Yield, out _));
        AssertOnlyWhenHarvestable(plot);
        Assert.Equal(PlotState.Tilled, plot.State);
    }

    /// <summary>变异那一株收上来写着变异，件数照常按浇水算：与没变的那一株浇得一样，收得一样多。</summary>
    [Fact]
    public void 收获结果带着变异而件数与没变的同一套()
    {
        var crop = Crop();
        var mutated = Planted(crop);
        var plain = Planted(crop);
        Assert.True(mutated.Water());
        Assert.True(plain.Water());

        GrowDays(mutated, crop, crop.DaysToFirstRipe, () => true);
        GrowDays(plain, crop, crop.DaysToFirstRipe, () => false);

        Assert.True(mutated.TryHarvest(crop, Yield, out var fromMutated));
        Assert.True(plain.TryHarvest(crop, Yield, out var fromPlain));
        Assert.True(fromMutated.Mutated);
        Assert.False(fromPlain.Mutated);
        Assert.Equal(fromPlain.Count, fromMutated.Count);
    }

    /// <summary>不给随机源就报错，不替调用方悄悄当成不变异。</summary>
    [Fact]
    public void 不给随机源就报错()
    {
        var crop = Crop();
        var plot = Planted(crop);

        Assert.Throws<ArgumentNullException>(() => plot.AdvanceDay(crop, FallowDays, null!));
    }

    private static Plot Planted(CropDefinition crop)
    {
        var plot = new Plot(PlotState.Tilled);
        Assert.True(plot.Plant(crop));
        return plot;
    }

    private static void GrowDays(Plot plot, CropDefinition crop, int days, Func<bool> roll)
    {
        for (var day = 0; day < days; day++)
        {
            plot.AdvanceDay(crop, FallowDays, roll);
        }
    }

    private static void AssertOnlyWhenHarvestable(Plot plot) =>
        Assert.False(plot.Mutated && plot.State != PlotState.Harvestable, $"状态是 {plot.State}，变异却为真");
}
