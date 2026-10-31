using System.Reflection;
using Tinderhearth.Rules.Economy;
using Xunit;

namespace Tinderhearth.Rules.Tests.Economy;

/// <summary>世界状态整份复制：改副本不碰原件，复制出来的每一格与原件逐项相同。</summary>
/// <remarks>
/// 每日结算在副本上跑、写档成功才换上去，所以副本与原件共用了任何会变的东西，写档失败时原件就已经被改了，
/// 而那不报错。逐项比读的是地块全部公开属性（<see cref="PlotSnapshot"/>），加一个属性不用改这里。
/// </remarks>
public class WorldStateTests
{
    private const int FallowDays = 7;

    private static readonly HarvestYieldRule Yield = new(BaseCount: 10, MinWaterFactor: 0.5, MaxWaterFactor: 1.0);

    private static CropDefinition Turnip() => new()
    {
        Id = "turnip",
        StageDays = [1, 1],
        RegrowFromStage = null,
        Seasons = [Season.Spring],
        SeedItemId = "item_turnip_seed",
        YieldItemId = "item_turnip",
    };

    /// <summary>一片 3×2 的地，几格各在不同的状态、计数都不是零：在长且浇过、待收且变异、枯死、荒置了一天。</summary>
    private static WorldState BusyWorld()
    {
        var grid = PlotGrid.Rectangle(new GridCell(-1, 2), width: 3, height: 2, PlotState.Tilled);
        var plots = grid.Cells.Select(c => c.Plot).ToList();

        plots[0].Plant(Turnip());
        plots[0].Water();

        plots[1].Plant(Turnip());
        plots[1].Water();
        plots[1].AdvanceDay(Turnip(), FallowDays, () => true);
        plots[1].AdvanceDay(Turnip(), FallowDays, () => true);

        plots[2].Plant(Turnip());
        plots[2].ApplySeasonChange(Turnip(), Season.Summer);

        plots[3].AdvanceDay(null, FallowDays, () => false);

        return new WorldState(new GameDate(2, Season.Autumn, 17), grid);
    }

    /// <summary>对每一格做一件这一格做得了的事，好让每一格都真的变了。</summary>
    private static void TouchEveryPlot(WorldState world)
    {
        world.AdvanceDate();
        foreach (var (_, plot) in world.Plots.Cells)
        {
            switch (plot.State)
            {
                case PlotState.Uncleared:
                    plot.Clear();
                    break;
                case PlotState.Cleared:
                    plot.Till();
                    break;
                case PlotState.Tilled:
                    plot.Plant(Turnip());
                    break;
                case PlotState.Planted:
                    plot.ApplySeasonChange(Turnip(), Season.Winter);
                    break;
                case PlotState.Harvestable:
                    plot.TryHarvest(Turnip(), Yield, out _);
                    break;
                case PlotState.Withered:
                    plot.ClearWithered();
                    break;
            }
        }
    }

    [Fact]
    public void 复制出来的每一格与原件逐项相同()
    {
        var world = BusyWorld();

        var copy = world.Clone();

        Assert.Equal(world.Date, copy.Date);
        Assert.Equal(PlotSnapshot.Of(world), PlotSnapshot.Of(copy));
    }

    [Fact]
    public void 改副本不碰原件()
    {
        var world = BusyWorld();
        var before = PlotSnapshot.Of(world);
        var dateBefore = world.Date;

        var copy = world.Clone();
        TouchEveryPlot(copy);

        Assert.Equal(dateBefore, world.Date);
        Assert.Equal(before, PlotSnapshot.Of(world));

        // 副本真的变了，否则上面两条是空过的。
        Assert.NotEqual(world.Date, copy.Date);
        Assert.All(PlotSnapshot.Of(copy).Zip(before), pair => Assert.NotEqual(pair.Second, pair.First));
    }

    [Fact]
    public void 改原件也不碰副本()
    {
        var world = BusyWorld();
        var copy = world.Clone();
        var copyBefore = PlotSnapshot.Of(copy);
        var copyDate = copy.Date;

        TouchEveryPlot(world);

        Assert.Equal(copyDate, copy.Date);
        Assert.Equal(copyBefore, PlotSnapshot.Of(copy));
    }

    [Fact]
    public void 副本与原件不共用任何一块地()
    {
        var world = BusyWorld();
        var copy = world.Clone();

        Assert.NotSame(world.Plots, copy.Plots);
        Assert.All(
            world.Plots.Cells.Zip(copy.Plots.Cells),
            pair => Assert.NotSame(pair.First.Plot, pair.Second.Plot));
    }

    /// <summary>地块用浅拷贝复制，它成立的前提是字段全是值类型与字符串。加一个会变的引用类型字段，这一条失败。</summary>
    [Fact]
    public void 地块的字段全是值类型与字符串()
    {
        var fields = typeof(Plot).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        Assert.NotEmpty(fields);
        Assert.All(fields, f => Assert.True(
            f.FieldType.IsValueType || f.FieldType == typeof(string),
            $"地块的字段 {f.Name} 是 {f.FieldType.Name}，浅拷贝会让副本与原件共用它"));
    }

    [Fact]
    public void 按左上角与宽高铺一片地()
    {
        var grid = PlotGrid.Rectangle(new GridCell(-1, 2), width: 3, height: 2, PlotState.Uncleared);
        GridCell[] rowByRow = [new(-1, 2), new(0, 2), new(1, 2), new(-1, 3), new(0, 3), new(1, 3)];

        Assert.Equal(6, grid.Count);
        Assert.Equal(rowByRow, grid.Cells.Select(c => c.Cell));
        Assert.All(grid.Cells, c => Assert.Equal(PlotState.Uncleared, c.Plot.State));
        Assert.False(grid.TryGet(new GridCell(2, 2), out _));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    public void 一格都没有的地被拒(int width, int height)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => PlotGrid.Rectangle(new GridCell(0, 0), width, height, PlotState.Tilled));
    }
}
