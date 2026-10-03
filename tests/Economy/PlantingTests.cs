using Tinderhearth.Rules.Economy;
using Tinderhearth.Rules.UI;
using Xunit;

namespace Tinderhearth.Rules.Tests.Economy;

/// <summary>
/// `GP-128` 播种取随身栏选中那一格：一块地里种得下两种作物，三条被拒路径各说得出是哪一种。
/// </summary>
/// <remarks>
/// **这几条用例自己准备与清理数据，不启动引擎。** 每个方法各造自己那几格地与那两份作物定义，
/// 所以顺序无关、可重复跑。
///
/// **它们为什么要有**：三条被拒路径合并成一个「失败」之后，玩家按下去没反应而界面说不出该换格、
/// 换东西还是先锄地 —— 而合并这件事不报错。另外「一块地里种得下两种」正是整条需求的成功判据，
/// 而在这之前它是检查器里写死的一个标识。
/// </remarks>
public class PlantingTests
{
    private const string Turnip = "turnip";
    private const string Herb = "herb";

    private static CropDefinition Crop(string id, int days = 2) => new()
    {
        Id = id,
        StageDays = [days],
        RegrowFromStage = null,
        Seasons = [Season.Spring],
        YieldItemId = $"{id}_yield",
    };

    /// <summary>两种作物的清单。**只认这两种** —— 别的标识一律不是种子。</summary>
    private static Func<string, CropDefinition?> Seeds()
    {
        var table = new Dictionary<string, CropDefinition>(StringComparer.Ordinal)
        {
            [Turnip] = Crop(Turnip),
            [Herb] = Crop(Herb, days: 3),
        };
        return id => table.TryGetValue(id, out var crop) ? crop : null;
    }

    private static Plot Tilled() => new(PlotState.Tilled);

    // ── 主路径：一块地里种得下两种 ──────────────────────────────────

    [Fact]
    public void 换一种种子再播另一格长出来的是各自那一种()
    {
        // **整条需求的成功判据。** 在这之前播哪一种是检查器里写死的，想种第二种要停下来改参数重跑。
        var bar = CarrySlotBar.Empty();
        bar.Fill([
            new CarrySlot(Turnip, 1), new CarrySlot(Herb, 1),
            CarrySlot.Empty, CarrySlot.Empty, CarrySlot.Empty, CarrySlot.Empty,
        ]);

        var first = Tilled();
        var second = Tilled();
        var seeds = Seeds();

        bar.Select(0);
        Assert.Equal(PlantResult.Planted, Plant(first, bar, seeds));

        bar.Select(1);
        Assert.Equal(PlantResult.Planted, Plant(second, bar, seeds));

        Assert.Equal(Turnip, first.CropId);
        Assert.Equal(Herb, second.CropId);
        Assert.Equal(PlotState.Planted, first.State);
        Assert.Equal(PlotState.Planted, second.State);
    }

    [Fact]
    public void 端到端从选种子到收上来()
    {
        // 主路径走完整条链：选中第 N 格 → 那一格是种子 → 对已锄的格播种 → 按天长 → 收。
        var bar = CarrySlotBar.Empty();
        bar.Fill([
            CarrySlot.Empty, CarrySlot.Empty, new CarrySlot(Turnip, 3),
            CarrySlot.Empty, CarrySlot.Empty, CarrySlot.Empty,
        ]);
        bar.Select(2);

        var plot = Tilled();
        var seeds = Seeds();
        var crop = seeds(Turnip)!;

        Assert.Equal(PlantResult.Planted, Plant(plot, bar, seeds));
        Assert.Equal(Turnip, plot.CropId);

        plot.Water();
        for (var day = 0; day < crop.DaysToFirstRipe; day++)
        {
            plot.AdvanceDay(crop, fallowRevertDays: 3);
        }

        Assert.Equal(PlotState.Harvestable, plot.State);

        var rule = new HarvestYieldRule(BaseCount: 4, MinWaterFactor: 0.5, MaxWaterFactor: 1.0);
        Assert.True(plot.TryHarvest(crop, rule, out var count));
        Assert.True(count >= 1);

        // 一次性作物收完地回到已锄，所以下一茬可以换一种种子播。
        Assert.Equal(PlotState.Tilled, plot.State);
        Assert.Null(plot.CropId);

        bar.Select(0);
        bar.Fill([
            new CarrySlot(Herb, 1), CarrySlot.Empty, CarrySlot.Empty,
            CarrySlot.Empty, CarrySlot.Empty, CarrySlot.Empty,
        ]);
        Assert.Equal(PlantResult.Planted, Plant(plot, bar, seeds));
        Assert.Equal(Herb, plot.CropId);
    }

    // ── 三条被拒路径，各说得出是哪一种 ──────────────────────────────

    [Fact]
    public void 手上那一格是空的时被拒()
    {
        var bar = CarrySlotBar.Empty();     // 六格全空
        var plot = Tilled();

        Assert.Equal(PlantResult.NothingSelected, Plant(plot, bar, Seeds()));
        Assert.Equal(PlotState.Tilled, plot.State);      // 被拒那一下不改状态
        Assert.Null(plot.CropId);
    }

    [Fact]
    public void 手上拿着不是种子的东西时被拒()
    {
        var bar = CarrySlotBar.Empty();
        bar.Fill([
            new CarrySlot("iron_ore", 5), CarrySlot.Empty, CarrySlot.Empty,
            CarrySlot.Empty, CarrySlot.Empty, CarrySlot.Empty,
        ]);
        var plot = Tilled();

        Assert.Equal(PlantResult.NotASeed, Plant(plot, bar, Seeds()));
        Assert.Equal(PlotState.Tilled, plot.State);
    }

    [Fact]
    public void 那一格状态不许播时被拒()
    {
        // **这一条在游戏里到不了**：动作是按格子状态派发的，已锄的格才派发到播种。
        // 它只在规则层被测到 —— 而那正是这条用例存在的理由（口径见 FarmField 的类注释）。
        var plot = new Plot(PlotState.Cleared);   // 清过但没锄
        var bar = CarrySlotBar.Empty();
        bar.Fill([
            new CarrySlot(Turnip, 1), CarrySlot.Empty, CarrySlot.Empty,
            CarrySlot.Empty, CarrySlot.Empty, CarrySlot.Empty,
        ]);

        Assert.Equal(PlantResult.CellNotReady, Plant(plot, bar, Seeds()));
        Assert.Equal(PlotState.Cleared, plot.State);
    }

    [Fact]
    public void 三条被拒原因两两分得开()
    {
        // 承重项。合并成一个「失败」之后三种的补救办法就分不出来了：换格、换东西、先锄地。
        var results = new[]
        {
            PlantResult.NothingSelected, PlantResult.NotASeed, PlantResult.CellNotReady,
        };

        Assert.Equal(results.Length, results.Distinct().Count());
        Assert.DoesNotContain(PlantResult.Planted, results);
    }

    // ── 边界 ────────────────────────────────────────────────────────

    [Fact]
    public void 件数为零的那一格算空手而不是算拿着东西()
    {
        // 「有标识没件数」是刚用完，玩家看到的是空格 —— 所以原因该是「空手」而不是「不是种子」。
        var bar = CarrySlotBar.Empty();
        bar.Fill([
            new CarrySlot(Turnip, 0), CarrySlot.Empty, CarrySlot.Empty,
            CarrySlot.Empty, CarrySlot.Empty, CarrySlot.Empty,
        ]);

        Assert.Equal(PlantResult.NothingSelected, Plant(Tilled(), bar, Seeds()));
    }

    [Fact]
    public void 先看手上再看地里()
    {
        // 顺序是有意的：玩家按下去那一刻格子状态已经决定了派发到播种，所以把手上那两条排前面，
        // 他拿到的就是他真能改的那一条。这条用例钉住那个顺序 —— 两者同时不满足时报「空手」。
        var plot = new Plot(PlotState.Cleared);   // 格子也不许播
        var bar = CarrySlotBar.Empty();           // 手上也是空的

        Assert.Equal(PlantResult.NothingSelected, Plant(plot, bar, Seeds()));
    }

    [Fact]
    public void 解析器与地块都不许为空()
    {
        Assert.Throws<ArgumentNullException>(
            () => Planting.TryPlant(null!, Turnip, Seeds()));
        Assert.Throws<ArgumentNullException>(
            () => Planting.TryPlant(Tilled(), Turnip, null!));
    }

    /// <summary>把「手上那一格」翻译成播种那一下，与 `FarmField` 里那一步同一个形状。</summary>
    private static PlantResult Plant(
        Plot plot, CarrySlotBar bar, Func<string, CropDefinition?> seeds)
    {
        var hand = bar.Selected;
        return Planting.TryPlant(plot, hand.IsEmpty ? null : hand.ItemId, seeds);
    }
}
