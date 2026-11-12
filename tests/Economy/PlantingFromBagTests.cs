using Tinderhearth.Rules.Economy;
using Tinderhearth.Rules.UI;
using Xunit;

namespace Tinderhearth.Rules.Tests.Economy;

/// <summary>屏幕上的随身栏画的是背包上随身栏那一排：手上那一格就是那一排的那一格，播一颗少一颗，拿着收上来的菜播不下去。</summary>
/// <remarks>
/// 这几条要有测试，因为失效方式都不报错：随身栏自己存一份的话，播下去背包里一颗不少；扣在别的格上，玩家看着
/// 手上那一格没变；收获物被当成种子的话，地里长出一株「芜菁」作物却没有它的种子那一份记账。
/// 背包与堆上限都是测试用值。
/// </remarks>
public class PlantingFromBagTests
{
    private static readonly CropDefinition Turnip = new()
    {
        Id = "turnip",
        StageDays = [1, 1],
        RegrowFromStage = null,
        Seasons = [Season.Spring],
        SeedItemId = TestBags.TurnipSeed,
        YieldItemId = TestBags.Turnip,
    };

    private static readonly IReadOnlyDictionary<string, CropDefinition> BySeed =
        CropDefinition.BySeed(new Dictionary<string, CropDefinition> { [Turnip.Id] = Turnip });

    private static CropDefinition? SeedOf(string id) => BySeed.GetValueOrDefault(id);

    /// <summary>与农场里那一步同一个换法：背包上随身栏那一排每一格换成屏幕上随身栏的一格。</summary>
    private static IReadOnlyList<CarrySlot> Bar(Bag bag) =>
        [.. bag.Carry.Select(s => s is { } stack ? new CarrySlot(stack.ItemId, stack.Count) : CarrySlot.Empty)];

    /// <summary>
    /// 一个背包：随身栏那一排第 1 格两颗种子、第 2 格空着、第 3 格一堆收上来的芜菁，其余空着；网格里第一格三颗种子。
    /// </summary>
    private static Bag Packed()
    {
        var carry = new ItemStack?[CarrySlotBar.SlotCount];
        carry[0] = new ItemStack(TestBags.TurnipSeed, null, 2, 1);
        carry[2] = new ItemStack(TestBags.Turnip, TestBags.Common, 7, 2);
        return Bag.Restore(carry, 2, 1, [new ItemStack(TestBags.TurnipSeed, null, 3, 3), null], TestBags.Items);
    }

    [Fact]
    public void 屏幕上的随身栏画的是背包上随身栏那一排_网格里的不在上面()
    {
        var bag = Packed();

        var bar = CarrySlotBar.Empty();
        bar.Fill(Bar(bag));

        Assert.Equal(new CarrySlot(TestBags.TurnipSeed, 2), bar.Slots[0]);
        Assert.True(bar.Slots[1].IsEmpty);
        Assert.Equal(new CarrySlot(TestBags.Turnip, 7), bar.Slots[2]);
        Assert.DoesNotContain(new CarrySlot(TestBags.TurnipSeed, 3), bar.Slots);
    }

    [Fact]
    public void 背包上随身栏那一排与屏幕上的随身栏格数对不上时当场抛()
    {
        var bar = CarrySlotBar.Empty();

        Assert.Throws<ArgumentException>(() => bar.Fill(Bar(new Bag(2, 1, CarrySlotBar.SlotCount - 1, TestBags.Items))));
    }

    [Fact]
    public void 播种成功从选中那一格扣一颗_扣到零那一格变空()
    {
        var bag = Packed();

        Assert.Equal(PlantResult.Planted, Planting.TryPlantFromBag(new Plot(PlotState.Tilled), bag, 0, SeedOf));
        Assert.Equal(1, bag.Carry[0]?.Count);
        Assert.Equal(new CarrySlot(TestBags.TurnipSeed, 1), Bar(bag)[0]);

        Assert.Equal(PlantResult.Planted, Planting.TryPlantFromBag(new Plot(PlotState.Tilled), bag, 0, SeedOf));
        Assert.Null(bag.Carry[0]);
        Assert.True(Bar(bag)[0].IsEmpty);

        // 网格里那三颗一颗没动：手上拿的只是随身栏那一排。
        Assert.Equal(3, bag.Slots[0]?.Count);
    }

    [Fact]
    public void 拿着收上来的菜去播被拒_那一格与地都不变()
    {
        var bag = Packed();
        var before = bag.Carry.ToList();
        var plot = new Plot(PlotState.Tilled);

        var result = Planting.TryPlantFromBag(plot, bag, 2, SeedOf);

        Assert.Equal(PlantResult.NotASeed, result);
        Assert.Equal(before, bag.Carry);
        Assert.Equal(PlotState.Tilled, plot.State);
    }

    [Fact]
    public void 手上那一格是空的被拒_什么都不扣()
    {
        var bag = Packed();
        var before = bag.Carry.ToList();
        var plot = new Plot(PlotState.Tilled);

        Assert.Equal(PlantResult.NothingSelected, Planting.TryPlantFromBag(plot, bag, 1, SeedOf));

        Assert.Equal(before, bag.Carry);
        Assert.Equal(PlotState.Tilled, plot.State);
    }

    [Fact]
    public void 这一格不许播时不扣种子()
    {
        var bag = Packed();
        var before = bag.Carry.ToList();

        Assert.Equal(PlantResult.CellNotReady, Planting.TryPlantFromBag(new Plot(PlotState.Cleared), bag, 0, SeedOf));

        Assert.Equal(before, bag.Carry);
    }
}
