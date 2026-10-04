using Tinderhearth.Rules.Economy;
using Xunit;

namespace Tinderhearth.Rules.Tests.Economy;

/// <summary>地块状态机：开荒、锄、播、浇、长、收、换季枯死与荒置退化这一整条流转。</summary>
/// <remarks>
/// 这里一个玩法数值都不定。荒置天数与产量那几个量归设计仓 design/数值模型.md，现在还没有值，
/// 所以本类里的数字只是为了让关系测得出来，改它们不该让任何一条失败。
/// </remarks>
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

    /// <summary>可耕格不能直接播种，要先锄一遍。</summary>
    /// <remarks>
    /// 它挡的不是一次误操作，而是「把锄合进清理」那种实现：合了之后玩家清出一片地
    /// 就直接得到犁开的土，然后把房子盖在犁沟上。
    /// </remarks>
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

    /// <summary>带作物的状态不许当起点。</summary>
    /// <remarks>
    /// 那样造出来的地块作物标识与阶段都是空的，看起来却像种着东西。
    /// 开局那一小块地是已锄的，所以三个无作物的状态都要容得下。
    /// </remarks>
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

    /// <summary>按声明的阶段天数逐日推进，到成熟那一阶段就是待收。</summary>
    /// <remarks>同时核对成熟不带天数：到了之后再推多少天都还是待收，不会自己往前走。</remarks>
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

    /// <summary>只有种子与成熟两个阶段的速生作物，播下去一天就待收。</summary>
    /// <remarks>
    /// 它核对的是推进那个循环读的是这一条作物自己的成熟序号，不是一个全局常量 ——
    /// 缺了它，把成熟序号写死成一个数的实现照旧能让上一条通过。
    /// </remarks>
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

    /// <summary>一次没浇也至少收一件，而且件数一定是整数。</summary>
    /// <remarks>
    /// 它挡的是「出征几天回来颗粒无收」，与家畜不死只停产是同一条纪律。
    /// 玩家看到的不能是 0 件，也不能是带小数的颗数。
    /// </remarks>
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

    /// <summary>浇得越多收得越多，浇满拿到上限那一档，浇得太少则停在下限那一档。</summary>
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

    /// <summary>循环收获退回它声明的那一阶段，并且把浇水累计清零。</summary>
    /// <remarks>
    /// 不清零的话，第一茬浇满的地第二茬躺着也能满产，浇水这件事从第二茬起就没有意义了。
    /// </remarks>
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

    /// <summary>一次性作物收完地就空了，回到已锄，不必重锄一遍。</summary>
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

    /// <summary>荒置计时只对空着的已锄格走，有作物的格连着放多少天都不退化。</summary>
    /// <remarks>
    /// 有作物的格也计时的话，出征几天回来地里的作物连着地一起没了，那是在罚离家的玩家。
    /// </remarks>
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

    /// <summary>换季只问一句：新季节在不在这种作物的季节列表里。</summary>
    /// <remarks>一条规则覆盖单季、跨季与全年，所以代码里没有「是不是跨季」那种分支。</remarks>
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

    /// <summary>待收的地放着不收也撑不过换季，而枯死之后作物标识仍然留着。</summary>
    /// <remarks>显示层要靠那个标识知道该画哪一种作物的枯死图。</remarks>
    [Fact]
    public void 待收的地换季也枯死且作物标识保留()
    {
        var crop = Crop();
        var plot = Ripe(crop, waterDays: 4);
        plot.ApplySeasonChange(crop, Season.Winter);
        Assert.Equal(PlotState.Withered, plot.State);
        Assert.Equal("turnip", plot.CropId);
    }

    /// <summary>枯死之后按天推进不再让它生长。</summary>
    /// <remarks>
    /// 每日结算里季节更替排在作物生长之前，靠的就是这条。顺序反了的话，换季那天的作物
    /// 会先白长一天再枯死。步序见设计仓 canon/gameplay/时间与经营.md 的
    /// 「结算顺序（固定，不得改动）」一节。
    /// </remarks>
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

    /// <summary>清掉枯株回到已锄，不用重锄。</summary>
    /// <remarks>
    /// 它与开荒那次清理是两件事：这一次不产任何材料，所以调用方那边也不该往产出管道里塞东西。
    /// </remarks>
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

    /// <summary>清掉枯株之后那一格不再带着浇水标记。</summary>
    /// <remarks>
    /// 守的是「没有作物的已锄地绝不带着浇水标记」。显示层按这个标记选干湿贴图，所以漏清一次的
    /// 样子是「刚清干净的空地显示成湿土」，而代码不报错。
    ///
    /// 这条路径真的漏过：浇过水 → 换季枯死 → 当天就清掉，中间没有按天推进去清那个标记。
    /// 收获那条路也要清，但待收的地浇不上水、而变成待收必经一次按天推进，所以那边今天断不出
    /// 名堂来 —— 与其写一条永远通过的断言，不如只钉这一条真能失败的。
    /// </remarks>
    [Fact]
    public void 清掉枯株之后不再带着浇水标记()
    {
        var crop = Crop();
        var plot = Planted(crop);

        Assert.True(plot.Water());
        plot.ApplySeasonChange(crop, Season.Winter);
        Assert.Equal(PlotState.Withered, plot.State);

        // 枯死那一下不清标记：作物死了，当天浇过的土还是湿的。
        Assert.True(plot.WateredToday);

        Assert.True(plot.ClearWithered());
        Assert.Equal(PlotState.Tilled, plot.State);
        Assert.False(plot.WateredToday);
    }

    /// <summary>只有正在长的作物浇得上，而且同一天浇第二次不重复计数。</summary>
    /// <remarks>
    /// 空地与待收的地都浇不上：湿的状态每天早上重置，产量的分母在成熟那一刻定住，
    /// 对它们浇水不改变任何结果，放开一个没有后果的动作等于让玩家白花时间。
    /// </remarks>
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

    /// <summary>传错作物定义要当场报错，而不是按错的天数悄悄长下去。</summary>
    /// <remarks>
    /// 地块只存作物标识，定义由调用方自己查表传进来，这一条是那个分工唯一的护栏。
    /// </remarks>
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

    /// <summary>荒置天数非正就报错。</summary>
    /// <remarks>
    /// 缺配置会得到 0，而 0 意味着锄完当天就荒了。参数的唯一来源是场景与资源，
    /// 缺了要当场报错，代码不替它悄悄兜一个能用的默认值。
    /// </remarks>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void 荒置天数非正就报错(int fallowRevertDays)
    {
        var plot = new Plot(PlotState.Tilled);
        Assert.Throws<ArgumentOutOfRangeException>(() => plot.AdvanceDay(null, fallowRevertDays));
    }

    /// <summary>产量规则在构造时就判三条：件数至少 1、系数下限大于零、上限不低于下限。</summary>
    /// <remarks>
    /// 这三条管的是填法而不是具体取什么值，所以代码判得到。
    /// 下限必须大于零那一条挡的就是「一次没浇便颗粒无收」。
    /// </remarks>
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
