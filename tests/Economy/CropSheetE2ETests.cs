using Tinderhearth.Rules.Economy;
using Tinderhearth.Rules.Foundation.Content;
using Tinderhearth.Rules.Tests.Foundation;
using Tinderhearth.Rules.Tests.Progression;
using Tinderhearth.Rules.UI;
using Xunit;

namespace Tinderhearth.Rules.Tests.Economy;

/// <summary>作物素材表那份需求的端到端：从工程里的作物数据读起，经随身栏播种、按几率掷变异，到收获带着变异。</summary>
/// <remarks>
/// 读的是工程里那批作物数据（代码仓 data/crops/），只读不写，走的是启动与农场共用的那个读法。每一次掷用的
/// 随机数都由测试按顺序给，多掷一次就取空、当场失败，所以每一条都可重复。各一环自己的边界在各自的测试类里，
/// 这里只把它们串起来。
/// </remarks>
public class CropSheetE2ETests
{
    private const int FallowDays = 7;

    private static readonly HarvestYieldRule Yield = new(BaseCount: 4, MinWaterFactor: 0.5, MaxWaterFactor: 1.0);

    /// <summary>测试用的几率：没有修饰时就是基础值。随机数小于它算中。</summary>
    private static readonly MutationChanceRule Chance = new(BaseChance: 0.25, Cap: 0.5);

    /// <summary>小于几率的那个随机数：中。</summary>
    private const double Hit = 0.0;

    /// <summary>不小于几率的那个随机数：不中。</summary>
    private const double Miss = 0.75;

    [Fact]
    public void 主路径_播种推进到长成必中之后待收且变异_收获带着变异再回到已锄()
    {
        var crops = ShippedCrops();
        var items = ShippedItems.Load();
        var bag = new Bag(CarrySlotBar.SlotCount, items);
        Assert.True(bag.TryAddAll(SeedBatches.Batch(crops.Values, 0, CarrySlotBar.SlotCount, items)));
        var bar = CarrySlotBar.Empty();
        bar.Fill(CarrySlotBar.ProjectionOf(
            [.. bag.Slots.Select(s => s is { } stack ? new CarrySlot(stack.ItemId, stack.Count) : CarrySlot.Empty)]));
        var plot = new Plot(PlotState.Tilled);

        Assert.Equal(
            PlantResult.Planted,
            Planting.TryPlantFromBag(plot, bag, bar.SelectedIndex, CropDefinition.BySeed(crops).GetValueOrDefault));
        var crop = crops[plot.CropId!];
        var draws = new Queue<double>([Hit]);

        Grow(plot, crop, crop.DaysToFirstRipe, draws);

        Assert.Equal(PlotState.Harvestable, plot.State);
        Assert.True(plot.Mutated);
        Assert.Empty(draws);

        Assert.True(plot.TryHarvest(crop, Yield, out var harvest));
        Assert.True(harvest.Mutated);
        Assert.True(harvest.Count >= 1);
        Assert.Equal(PlotState.Tilled, plot.State);
        Assert.False(plot.Mutated);
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(1.5)]
    public void 失败路径_上限不小于一被拒(double cap)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new MutationChanceRule(Chance.BaseChance, cap));
    }

    /// <summary>没到成熟的那几晚一次都不掷：一个随机数都不给，长到成熟前一晚为止不会取空。</summary>
    [Fact]
    public void 失败路径_没到成熟不掷()
    {
        var crop = ShippedCrops().Values.First();
        var plot = new Plot(PlotState.Tilled);
        Assert.True(plot.Plant(crop));
        var draws = new Queue<double>();

        Grow(plot, crop, crop.DaysToFirstRipe - 1, draws);

        Assert.Equal(PlotState.Planted, plot.State);
        Assert.False(plot.Mutated);
    }

    /// <summary>循环作物下一茬重掷、不继承上一茬：先中后不中，两茬一变一不变。</summary>
    /// <remarks>
    /// 工程里的占位作物都只收一次，所以把其中一份的收获后退回那一格改成 1 再读 —— 走的仍是同一个解析。
    /// </remarks>
    [Fact]
    public void 失败路径_循环作物下一茬重掷不继承上一茬()
    {
        var (path, text) = ShippedCropFiles().First();
        var regrowing = text.Replace("\"regrowFromStage\": null", "\"regrowFromStage\": 1", StringComparison.Ordinal);
        Assert.NotEqual(text, regrowing);
        var crop = CropDefinition.Parse(regrowing, path);
        var plot = new Plot(PlotState.Tilled);
        Assert.True(plot.Plant(crop));
        var draws = new Queue<double>([Hit, Miss]);

        Grow(plot, crop, crop.DaysToFirstRipe, draws);
        Assert.True(plot.TryHarvest(crop, Yield, out var first));
        Assert.Equal(PlotState.Planted, plot.State);

        Grow(plot, crop, crop.DaysToRegrow!.Value, draws);
        Assert.True(plot.TryHarvest(crop, Yield, out var second));

        Assert.True(first.Mutated);
        Assert.False(second.Mutated);
        Assert.Empty(draws);
    }

    /// <summary>推进几天，每一次掷都从给定的那一串随机数里取下一个，再按几率判中没中。</summary>
    private static void Grow(Plot plot, CropDefinition crop, int days, Queue<double> draws)
    {
        for (var day = 0; day < days; day++)
        {
            plot.AdvanceDay(crop, FallowDays, () => draws.Dequeue() < Chance.ChanceWith([]));
        }
    }

    /// <summary>工程里那批作物数据，经启动与农场共用的那个读法建表，种子与产出对着工程里那批物品核。睡一觉那份端到端也读它。</summary>
    internal static IReadOnlyDictionary<string, CropDefinition> ShippedCrops()
    {
        var catalog = new ContentCatalog();
        catalog.AddSource(new InMemoryContentSource("base", ShippedCropFiles().ToDictionary(f => f.Path, f => f.Text)));
        return CropDefinition.LoadAll(catalog, ShippedItems.Load());
    }

    /// <summary>工程里那批作物数据文件，按路径排，路径写成内容目录里的相对路径。</summary>
    internal static List<(string Path, string Text)> ShippedCropFiles() =>
        [.. Directory.GetFiles(Path.Combine(RepoFiles.Root(), "data", CropDefinition.ContentDirectory), "*.json")
            .Order(StringComparer.Ordinal)
            .Select(p => ($"{CropDefinition.ContentDirectory}/{Path.GetFileName(p)}", File.ReadAllText(p)))];
}
