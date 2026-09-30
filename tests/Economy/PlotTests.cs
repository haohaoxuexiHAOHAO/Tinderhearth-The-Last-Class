using Tinderhearth.Rules.Economy;
using Xunit;

namespace Tinderhearth.Rules.Tests.Economy;

/// <summary>
/// `GP-87` 地块状态机的守卫。**这里一个玩法数值都不定**：荒置天数与产量那几个量归数值模型、
/// 现在没有值，所以本类里的数字只为让关系可测 —— 改它们不该让任何一条失败。
/// </summary>
public class PlotTests
{
    /// <summary>荒置几天退回可耕。与数值模型无关的测试用值。</summary>
    private const int FallowDays = 7;

    /// <summary>按比例收的那份规则：浇满收 10 件、一次没浇收一半。</summary>
    private static readonly HarvestYieldRule Yield = new(
        BaseCount: 10, MinWaterFactor: 0.5, MaxWaterFactor: 1.0);

    /// <summary>基数小到会被取整抹成 0 的那份规则，用来逼出「至少 1 件」那条保底。</summary>
    private static readonly HarvestYieldRule TinyYield = new(
        BaseCount: 1, MinWaterFactor: 0.1, MaxWaterFactor: 1.0);

    private static CropDefinition Crop(
        int? regrowFromStage = null,
        IReadOnlyList<int>? stageDays = null,
        IReadOnlyList<Season>? seasons = null) => new()
        {
            Id = "turnip",
            StageDays = stageDays ?? [1, 1, 1, 1],
            RegrowFromStage = regrowFromStage,
            Seasons = seasons ?? [Season.Spring],
            YieldItemId = "item_turnip",
        };

    /// <summary>
    /// **可耕格不能播种** —— 这一条是承重的。它挡的不是一次误操作，而是「把锄合进清理」那种
    /// 实现：合了之后玩家清出一片地就直接得到犁开的土，然后把房子盖在犁沟上。
    /// </summary>
    [Fact]
    public void 可耕格不能播种要先锄()
    {
        var plot = new Plot(PlotState.Cleared);
        Assert.False(plot.Plant(Crop()));
        Assert.Equal(PlotState.Cleared, plot.State);
        Assert.Null(plot.CropId);

        Assert.True(plot.Till());
        Assert.True(plot.Plant(Crop()));
        Assert.Equal(PlotState.Planted, plot.State);
    }

    /// <summary>未清理的格什么都干不了，清理是它变得可用的唯一入口。</summary>
    [Fact]
    public void 未清理的格既不能锄也不能播种()
    {
        var plot = new Plot();
        Assert.Equal(PlotState.Uncleared, plot.State);
        Assert.False(plot.Till());
        Assert.False(plot.Plant(Crop()));
        Assert.False(plot.Water());
        Assert.Equal(PlotState.Uncleared, plot.State);

        Assert.True(plot.Clear());
        Assert.Equal(PlotState.Cleared, plot.State);
        Assert.False(plot.Clear());
    }

    /// <summary>
    /// 带作物的状态不许当起点：那样作物标识与阶段都是空的，而地块看起来却像种着东西。
    /// 开局那一小块是**已锄**的，所以那三个无作物的状态都要容得下。
    /// </summary>
    [Theory]
    [InlineData(PlotState.Planted)]
    [InlineData(PlotState.Harvestable)]
    [InlineData(PlotState.Withered)]
    public void 带作物的状态不许当起点(PlotState initial)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Plot(initial));
    }

    [Theory]
    [InlineData(PlotState.Uncleared)]
    [InlineData(PlotState.Cleared)]
    [InlineData(PlotState.Tilled)]
    public void 无作物的三个状态都能当起点(PlotState initial)
    {
        Assert.Equal(initial, new Plot(initial).State);
    }

    /// <summary>
    /// 按声明的阶段天数逐日推进，**到成熟那一阶段就是待收**。这里同时钉住成熟不带天数：
    /// 到了之后再推多少天都还是待收，不会自己往前走。
    /// </summary>
    [Fact]
    public void 按声明的天数长到成熟就待收()
    {
        var crop = Crop(stageDays: [1, 2, 2, 3]);
        var plot = Planted(crop);

        for (var day = 1; day < crop.DaysToFirstRipe; day++)
        {
            plot.AdvanceDay(crop, FallowDays);
            Assert.Equal(PlotState.Planted, plot.State);
        }

        plot.AdvanceDay(crop, FallowDays);
        Assert.Equal(PlotState.Harvestable, plot.State);
        Assert.Equal(crop.RipeStage, plot.Stage);

        plot.AdvanceDay(crop, FallowDays);
        plot.AdvanceDay(crop, FallowDays);
        Assert.Equal(PlotState.Harvestable, plot.State);
        Assert.Equal(crop.RipeStage, plot.Stage);
    }

    /// <summary>
    /// **阶段数由作物自己声明**，所以只有种子与成熟两个阶段的速生作物一天就待收。
    /// 这一条钉住推进那个循环读的是**这一条作物自己的成熟序号**，而不是一个全局常量 ——
    /// 缺了它，把成熟序号写死成一个数的实现照旧能让上面那条用例通过。
    /// </summary>
    [Fact]
    public void 两阶段的速生作物一天就待收()
    {
        var crop = Crop(stageDays: [1]);
        var plot = Planted(crop);

        Assert.Equal(1, crop.DaysToFirstRipe);
        Assert.Equal(PlotState.Planted, plot.State);

        plot.AdvanceDay(crop, FallowDays);

        Assert.Equal(PlotState.Harvestable, plot.State);
        Assert.Equal(crop.RipeStage, plot.Stage);
    }

    /// <summary>
    /// **一次没浇也收得到东西，而且件数是整数。** 这条是承重项：它挡的是「出征十天回来颗粒
    /// 无收」，与「家畜不死只停产」是同一条纪律。玩家看到的不能是 0 件，也不能是 5.4 颗。
    /// </summary>
    [Fact]
    public void 一次没浇也至少收一件且件数是整数()
    {
        var crop = Crop();
        var plot = Ripe(crop, waterDays: 0);
        Assert.True(plot.TryHarvest(crop, Yield, out var count));
        Assert.Equal(5, count);

        var tiny = Ripe(crop, waterDays: 0);
        Assert.True(tiny.TryHarvest(crop, TinyYield, out var tinyCount));
        Assert.Equal(1, tinyCount);
    }

    /// <summary>浇得越多收得越多，浇满拿到上限那一档。</summary>
    [Theory]
    [InlineData(0, 5)]
    [InlineData(2, 5)]
    [InlineData(3, 7)]
    [InlineData(4, 10)]
    public void 浇过的天数越多收得越多(int waterDays, int expected)
    {
        var crop = Crop();
        var plot = Ripe(crop, waterDays);
        Assert.True(plot.TryHarvest(crop, Yield, out var count));
        Assert.Equal(expected, count);
    }

    /// <summary>
    /// **循环收获退回它声明的那一阶段，而且浇水累计清零** —— 第二茬不继承第一茬的浇水记录。
    /// 不清零的话第一茬浇满的地第二茬躺着也能满产，浇水这件事从第二茬起就没有意义了。
    /// </summary>
    [Fact]
    public void 循环收获退回声明的阶段并清零浇水累计()
    {
        var crop = Crop(regrowFromStage: 2, stageDays: [1, 1, 1, 1]);
        var plot = Ripe(crop, waterDays: 4);
        Assert.Equal(4, plot.WateredDaysThisCrop);

        Assert.True(plot.TryHarvest(crop, Yield, out var first));
        Assert.Equal(10, first);
        Assert.Equal(PlotState.Planted, plot.State);
        Assert.Equal(2, plot.Stage);
        Assert.Equal("turnip", plot.CropId);
        Assert.Equal(0, plot.WateredDaysThisCrop);
        Assert.Equal(0, plot.DaysThisCrop);

        // 第二茬一次不浇，走到成熟只要「退回那一阶段之后剩下的天数」。
        for (var day = 0; day < crop.DaysToRegrow; day++)
        {
            plot.AdvanceDay(crop, FallowDays);
        }

        Assert.Equal(PlotState.Harvestable, plot.State);
        Assert.True(plot.TryHarvest(crop, Yield, out var second));
        Assert.Equal(5, second);
    }

    /// <summary>一次性作物收完地就空了，回到已锄 —— **不必重锄一遍**。</summary>
    [Fact]
    public void 一次性作物收完回到已锄()
    {
        var crop = Crop();
        var plot = Ripe(crop, waterDays: 4);
        Assert.True(plot.TryHarvest(crop, Yield, out _));
        Assert.Equal(PlotState.Tilled, plot.State);
        Assert.Null(plot.CropId);
        Assert.False(plot.TryHarvest(crop, Yield, out var again));
        Assert.Equal(0, again);
    }

    /// <summary>
    /// **荒置计时只对空着的已锄格走。** 有作物的格连着放多少天都不退化 —— 否则出征几天回来
    /// 地里的作物连着地一起没了，而那是在罚离家的玩家。
    /// </summary>
    [Fact]
    public void 荒置只对空的已锄格计时()
    {
        var empty = new Plot(PlotState.Tilled);
        for (var day = 1; day < FallowDays; day++)
        {
            empty.AdvanceDay(null, FallowDays);
            Assert.Equal(PlotState.Tilled, empty.State);
        }

        empty.AdvanceDay(null, FallowDays);
        Assert.Equal(PlotState.Cleared, empty.State);

        var crop = Crop(stageDays: [FallowDays * 3, 1, 1, 1]);
        var planted = Planted(crop);
        for (var day = 0; day < FallowDays * 2; day++)
        {
            planted.AdvanceDay(crop, FallowDays);
        }

        Assert.Equal(PlotState.Planted, planted.State);
        Assert.Equal(0, planted.DaysFallow);
    }

    /// <summary>可耕格已经退到底了，不再往下退 —— 它不会自己长回未清理。</summary>
    [Fact]
    public void 可耕格不再继续退化()
    {
        var plot = new Plot(PlotState.Cleared);
        for (var day = 0; day < FallowDays * 3; day++)
        {
            plot.AdvanceDay(null, FallowDays);
        }

        Assert.Equal(PlotState.Cleared, plot.State);
    }

    /// <summary>
    /// 换季只问一句：**新季节在不在这种作物的列表里**。一条规则覆盖单季、跨季与全年，
    /// 所以代码里没有「是不是跨季」那种分支。
    /// </summary>
    [Theory]
    [InlineData(Season.Spring, false)]
    [InlineData(Season.Autumn, false)]
    [InlineData(Season.Summer, true)]
    [InlineData(Season.Winter, true)]
    public void 换季时新季节不在列表里才枯死(Season newSeason, bool shouldWither)
    {
        var crop = Crop(seasons: [Season.Spring, Season.Autumn]);
        var plot = Planted(crop);
        plot.ApplySeasonChange(crop, newSeason);
        Assert.Equal(shouldWither ? PlotState.Withered : PlotState.Planted, plot.State);
    }

    /// <summary>
    /// 待收的地也会枯死 —— 放着不收撑不过换季。**枯死之后作物标识仍然在**，
    /// 显示层要靠它知道该画哪一种作物的枯死图。
    /// </summary>
    [Fact]
    public void 待收的地换季也枯死且作物标识保留()
    {
        var crop = Crop();
        var plot = Ripe(crop, waterDays: 4);
        plot.ApplySeasonChange(crop, Season.Winter);
        Assert.Equal(PlotState.Withered, plot.State);
        Assert.Equal("turnip", plot.CropId);
    }

    /// <summary>
    /// **枯死之后按天推进不再让它生长。** 这一半是正典那条「季节更替排在作物生长之前」的执行体：
    /// 顺序反了的话换季那天的作物会先白长一天再枯死。
    /// </summary>
    [Fact]
    public void 枯死之后按天推进不再生长()
    {
        var crop = Crop(stageDays: [5, 5, 5, 5]);
        var plot = Planted(crop);
        plot.ApplySeasonChange(crop, Season.Winter);

        for (var day = 0; day < 20; day++)
        {
            plot.AdvanceDay(crop, FallowDays);
        }

        Assert.Equal(PlotState.Withered, plot.State);
        Assert.Equal(0, plot.Stage);
        Assert.Equal(0, plot.DaysThisCrop);
    }

    /// <summary>
    /// 清掉枯株回到已锄，**不用重锄**。它与开荒那次清理是两件事：这一次不产任何材料，
    /// 所以调用方那边也不该往产出管道里塞东西。
    /// </summary>
    [Fact]
    public void 清掉枯株回到已锄()
    {
        var crop = Crop();
        var plot = Planted(crop);
        plot.ApplySeasonChange(crop, Season.Winter);

        Assert.True(plot.ClearWithered());
        Assert.Equal(PlotState.Tilled, plot.State);
        Assert.Null(plot.CropId);
        Assert.False(plot.ClearWithered());
        Assert.True(plot.Plant(crop));
    }

    /// <summary>
    /// 浇水只在作物正在长时成功，而且**同一天浇第二次不重复计数**。空地与待收的地浇不上：
    /// 湿的状态每天早上重置，分母在成熟那一刻定住 —— 对它们浇水不改变任何结果。
    /// </summary>
    [Fact]
    public void 只有正在长的作物浇得上且一天只算一次()
    {
        var crop = Crop(stageDays: [3, 1, 1, 1]);
        var plot = Planted(crop);

        Assert.True(plot.Water());
        Assert.True(plot.WateredToday);
        Assert.False(plot.Water());
        Assert.Equal(1, plot.WateredDaysThisCrop);

        plot.AdvanceDay(crop, FallowDays);
        Assert.False(plot.WateredToday);
        Assert.True(plot.Water());
        Assert.Equal(2, plot.WateredDaysThisCrop);

        var empty = new Plot(PlotState.Tilled);
        Assert.False(empty.Water());

        var ripe = Ripe(crop, waterDays: 0);
        Assert.False(ripe.Water());
    }

    /// <summary>
    /// 传错作物定义要当场报错，而不是按错的天数悄悄长下去。地块只存标识，**定义由调用方查**，
    /// 所以这一条是那个分工唯一的护栏。
    /// </summary>
    [Fact]
    public void 传错作物定义当场报错()
    {
        var crop = Crop();
        var other = crop with { Id = "carrot" };
        var plot = Planted(crop);

        Assert.Throws<ArgumentException>(() => plot.AdvanceDay(other, FallowDays));
        Assert.Throws<ArgumentException>(() => plot.ApplySeasonChange(other, Season.Summer));
        Assert.Throws<ArgumentException>(() =>
            plot.TryHarvest(other, Yield, out _));
    }

    /// <summary>长着作物的地推进时不传定义要报错 —— 那是调用方漏了查表，不是玩家的操作。</summary>
    [Fact]
    public void 长着作物却不传定义要报错()
    {
        var crop = Crop();
        var plot = Planted(crop);
        Assert.Throws<ArgumentNullException>(() => plot.AdvanceDay(null, FallowDays));
    }

    /// <summary>
    /// 荒置天数缺配置会得到 0，而 0 意味着「锄完当天就荒了」。**缺配置要当场报错**，
    /// 不许悄悄兜底（`ADR-0009`）。
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void 荒置天数非正就报错(int fallowRevertDays)
    {
        var plot = new Plot(PlotState.Tilled);
        Assert.Throws<ArgumentOutOfRangeException>(() => plot.AdvanceDay(null, fallowRevertDays));
    }

    /// <summary>
    /// 产量规则那三条约束是**形式**而不是值，所以代码判得到：件数至少 1、系数下限必须大于零、
    /// 上限不得低于下限。下限等于零那一条挡的就是「一次没浇便颗粒无收」。
    /// </summary>
    [Theory]
    [InlineData(0, 0.5, 1.0)]
    [InlineData(-1, 0.5, 1.0)]
    [InlineData(10, 0.0, 1.0)]
    [InlineData(10, -0.1, 1.0)]
    [InlineData(10, 0.8, 0.5)]
    public void 产量规则的三条约束在构造时就判(int baseCount, double min, double max)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new HarvestYieldRule(
            BaseCount: baseCount, MinWaterFactor: min, MaxWaterFactor: max));
    }

    /// <summary>造一格刚播下种的地。</summary>
    private static Plot Planted(CropDefinition crop)
    {
        var plot = new Plot(PlotState.Tilled);
        Assert.True(plot.Plant(crop));
        return plot;
    }

    /// <summary>造一格已经长到待收的地，途中浇指定天数的水。</summary>
    private static Plot Ripe(CropDefinition crop, int waterDays)
    {
        var plot = Planted(crop);
        for (var day = 0; day < crop.DaysToFirstRipe; day++)
        {
            if (day < waterDays)
            {
                Assert.True(plot.Water());
            }

            plot.AdvanceDay(crop, FallowDays);
        }

        Assert.Equal(PlotState.Harvestable, plot.State);
        return plot;
    }
}
