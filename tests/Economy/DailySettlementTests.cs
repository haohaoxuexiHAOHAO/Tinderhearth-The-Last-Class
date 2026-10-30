using Tinderhearth.Rules.Economy;
using Xunit;

namespace Tinderhearth.Rules.Tests.Economy;

/// <summary>每日结算：一个入口按步名把正典那串逐步调到；日期、换季与作物那三步各做对了自己那一件。</summary>
/// <remarks>
/// 驱动那几条用每一步只记下自己名字的假步，比的是名字、不是序号。作物的阶段天数与荒置天数都是测试用值，
/// 改它们不该让任何一条失败。
/// </remarks>
public class DailySettlementTests
{
    private const int FallowDays = 7;

    private static readonly SettlementStep Nothing = _ => { };

    private static readonly CropDefinition SpringOnly = Crop("spring_only", [Season.Spring]);
    private static readonly CropDefinition SpringAndSummer = Crop("spring_summer", [Season.Spring, Season.Summer]);

    private static IReadOnlyDictionary<string, CropDefinition> Crops => new Dictionary<string, CropDefinition>
    {
        [SpringOnly.Id] = SpringOnly,
        [SpringAndSummer.Id] = SpringAndSummer,
    };

    /// <summary>一个计时阶段、一天：播下去第二天早上就长成。</summary>
    private static CropDefinition Crop(string id, IReadOnlyList<Season> seasons) => new()
    {
        Id = id,
        StageDays = [1],
        RegrowFromStage = null,
        Seasons = seasons,
        YieldItemId = "item_" + id,
    };

    /// <summary>一片 2×1 的地，左边种只在春季的，右边种春夏都行的，日期是给定那一天。</summary>
    private static WorldState FieldOn(GameDate date)
    {
        var grid = PlotGrid.Rectangle(new GridCell(0, 0), width: 2, height: 1, PlotState.Tilled);
        var plots = grid.Cells.Select(c => c.Plot).ToList();
        plots[0].Plant(SpringOnly);
        plots[1].Plant(SpringAndSummer);
        return new WorldState(date, grid);
    }

    private static Plot PlotAt(WorldState world, int x) =>
        world.Plots.TryGet(new GridCell(x, 0), out var plot) ? plot : throw new InvalidOperationException($"没有第 {x} 格");

    /// <summary>一份每一步都只记下自己名字的驱动；事件那一处记「事件」。</summary>
    /// <remarks>步是倒着交进去的：驱动要按步名表的顺序调，不按交进来的先后。</remarks>
    private static (DailySettlement Settlement, List<string> Calls) Recording()
    {
        var calls = new List<string>();
        var steps = SettlementSteps.Order.Reverse().Select(name => (name, (SettlementStep)(_ => calls.Add(name))));
        return (new DailySettlement(steps, _ => calls.Add("事件")), calls);
    }

    private static IEnumerable<(string Name, SettlementStep Run)> AllNothing() =>
        SettlementSteps.Order.Select(name => (name, Nothing));

    [Fact]
    public void 每一步恰好被调到一次顺序与步名表一致()
    {
        var (settlement, calls) = Recording();

        settlement.Run(FieldOn(Calendar.StartDate), DayEndSource.Sleep);

        Assert.Equal(SettlementSteps.Order, calls.Take(SettlementSteps.Order.Count));
    }

    [Fact]
    public void 事件的询问排在最后一步之后()
    {
        var (settlement, calls) = Recording();
        string[] expected = [.. SettlementSteps.Order, "事件"];

        settlement.Run(FieldOn(Calendar.StartDate), DayEndSource.Sleep);

        Assert.Equal(expected, calls);
    }

    [Fact]
    public void 步数常量等于步名表的项数且步名不重复()
    {
        Assert.Equal(SettlementSteps.Count, SettlementSteps.Order.Count);
        Assert.Equal(SettlementSteps.Order.Count, SettlementSteps.Order.Distinct().Count());
    }

    [Fact]
    public void 少一步构造被拒并说清缺哪一步()
    {
        var steps = AllNothing().Where(s => s.Name != SettlementSteps.DebtInterest);

        var e = Assert.Throws<ArgumentException>(() => new DailySettlement(steps, Nothing));

        Assert.Contains(SettlementSteps.DebtInterest, e.Message);
    }

    [Fact]
    public void 多一个表里没有的名字构造被拒()
    {
        var steps = AllNothing().Append(("顺手加的一步", Nothing));

        var e = Assert.Throws<ArgumentException>(() => new DailySettlement(steps, Nothing));

        Assert.Contains("顺手加的一步", e.Message);
    }

    [Fact]
    public void 同一步给两次构造被拒()
    {
        var steps = AllNothing().Append((SettlementSteps.CropGrowth, Nothing));

        var e = Assert.Throws<ArgumentException>(() => new DailySettlement(steps, Nothing));

        Assert.Contains(SettlementSteps.CropGrowth, e.Message);
    }

    [Fact]
    public void 日期推进把日期推进一天()
    {
        var world = FieldOn(new GameDate(1, Season.Spring, 3));

        StandardSettlement.Create(Crops, FallowDays, () => false).Run(world, DayEndSource.Sleep);

        Assert.Equal(new GameDate(1, Season.Spring, 4), world.Date);
    }

    [Fact]
    public void 换季那天不当季的作物枯死()
    {
        var world = FieldOn(new GameDate(1, Season.Spring, Calendar.DaysPerSeason));

        var day = StandardSettlement.Create(Crops, FallowDays, () => false).Run(world, DayEndSource.Sleep);

        Assert.True(day.SeasonChanged);
        Assert.Equal(PlotState.Withered, PlotAt(world, 0).State);
        Assert.Equal(PlotState.Harvestable, PlotAt(world, 1).State);
    }

    /// <summary>只留日期推进与季节那一步，其余换成空步，前后逐格比：不换季的那天它一格都不动。</summary>
    [Fact]
    public void 不换季的那天季节那一步一格都不动()
    {
        var world = FieldOn(new GameDate(1, Season.Spring, 3));
        var before = PlotSnapshot.Of(world);
        var steps = StandardSettlement.Steps(Crops, FallowDays, () => false)
            .Select(s => s.Name is SettlementSteps.DateAdvance or SettlementSteps.SeasonChange ? s : (s.Name, Nothing));

        var day = new DailySettlement(steps, Nothing).Run(world, DayEndSource.Sleep);

        Assert.False(day.SeasonChanged);
        Assert.Equal(before, PlotSnapshot.Of(world));
        Assert.Empty(day.CropChanges);
    }

    /// <summary>左边那株今晚本该长成，可它换季枯死了：一次都不掷；右边那株照常长成、掷一次、中了。</summary>
    [Fact]
    public void 换季枯死的那一格不再长也不掷变异()
    {
        var world = FieldOn(new GameDate(1, Season.Spring, Calendar.DaysPerSeason));
        var rolls = 0;
        CropChange[] expected =
        [
            new(new GridCell(0, 0), SpringOnly.Id, CropChangeKind.Withered),
            new(new GridCell(1, 0), SpringAndSummer.Id, CropChangeKind.Mutated),
        ];

        var day = StandardSettlement.Create(Crops, FallowDays, () => ++rolls > 0).Run(world, DayEndSource.Sleep);

        var withered = PlotAt(world, 0);
        Assert.Equal(PlotState.Withered, withered.State);
        Assert.False(withered.Mutated);
        Assert.Equal(PlotState.Harvestable, PlotAt(world, 1).State);
        Assert.True(PlotAt(world, 1).Mutated);
        Assert.Equal(1, rolls);
        Assert.Equal(expected, day.CropChanges);
    }

    /// <summary>不换季的那天，作物那一步对每一格做的就是地块那台状态机的按天推进，一格一次。</summary>
    [Fact]
    public void 不换季的那天每一格照地块那台状态机过一夜()
    {
        var world = FieldOn(new GameDate(1, Season.Spring, 3));
        PlotAt(world, 0).Water();
        var expected = world.Clone();
        foreach (var (_, plot) in expected.Plots.Cells)
        {
            plot.AdvanceDay(Crops[plot.CropId!], FallowDays, () => true);
        }

        StandardSettlement.Create(Crops, FallowDays, () => true).Run(world, DayEndSource.Sleep);

        Assert.Equal(PlotSnapshot.Of(expected), PlotSnapshot.Of(world));
    }

    [Fact]
    public void 两个来源推同一份世界结果完全一致()
    {
        var slept = FieldOn(new GameDate(1, Season.Spring, Calendar.DaysPerSeason));
        var forced = slept.Clone();
        var settlement = StandardSettlement.Create(Crops, FallowDays, () => true);

        var sleptDay = settlement.Run(slept, DayEndSource.Sleep);
        var forcedDay = settlement.Run(forced, DayEndSource.ForcedEnd);

        Assert.Equal(slept.Date, forced.Date);
        Assert.Equal(PlotSnapshot.Of(slept), PlotSnapshot.Of(forced));
        Assert.Equal(sleptDay.SeasonChanged, forcedDay.SeasonChanged);
        Assert.Equal(sleptDay.CropChanges, forcedDay.CropChanges);
    }

    [Fact]
    public void 荒置天数小于一构造被拒()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => StandardSettlement.Create(Crops, 0, () => false));
    }

    [Fact]
    public void 地里那种作物找不到定义时推进当场报错()
    {
        var world = FieldOn(new GameDate(1, Season.Spring, 3));
        var onlySpring = new Dictionary<string, CropDefinition> { [SpringOnly.Id] = SpringOnly };

        Assert.Throws<ArgumentNullException>(
            () => StandardSettlement.Create(onlySpring, FallowDays, () => false).Run(world, DayEndSource.Sleep));
    }
}
