using Tinderhearth.Rules.Economy;
using Tinderhearth.Rules.UI;
using Xunit;

namespace Tinderhearth.Rules.Tests.Economy;

/// <summary>随身栏是背包的前几格：手上那一格就是背包那一格，播一颗少一颗，拿着收上来的菜播不下去。</summary>
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

    /// <summary>与农场里那一步同一个换法：背包每一格换成随身栏的一格，再取前几格。</summary>
    private static IReadOnlyList<CarrySlot> Projection(Bag bag) => CarrySlotBar.ProjectionOf(
        [.. bag.Slots.Select(s => s is { } stack ? new CarrySlot(stack.ItemId, stack.Count) : CarrySlot.Empty)]);

    /// <summary>一个比随身栏多两格的背包：第 1 格两颗种子，第 2 格空着，第 3 格一堆收上来的芜菁，最后一格三颗种子。</summary>
    private static Bag Packed()
    {
        var slots = new ItemStack?[CarrySlotBar.SlotCount + 2];
        slots[0] = new ItemStack(TestBags.TurnipSeed, null, 2, 1);
        slots[2] = new ItemStack(TestBags.Turnip, TestBags.Common, 7, 2);
        slots[^1] = new ItemStack(TestBags.TurnipSeed, null, 3, 3);
        return Bag.Restore(slots, TestBags.Items);
    }

    [Fact]
    public void 随身栏显示背包的前几格_后面那几格不在随身栏上()
    {
        var bag = Packed();

        var carry = Projection(bag);

        Assert.Equal(CarrySlotBar.SlotCount, carry.Count);
        Assert.Equal(new CarrySlot(TestBags.TurnipSeed, 2), carry[0]);
        Assert.True(carry[1].IsEmpty);
        Assert.Equal(new CarrySlot(TestBags.Turnip, 7), carry[2]);
        Assert.DoesNotContain(new CarrySlot(TestBags.TurnipSeed, 3), carry);
    }

    [Fact]
    public void 背包比随身栏少格时当场抛()
    {
        Assert.Throws<ArgumentException>(
            () => Projection(TestBags.Empty(CarrySlotBar.SlotCount - 1)));
    }

    [Fact]
    public void 播种成功从选中那一格扣一颗_扣到零那一格变空()
    {
        var bag = Packed();

        Assert.Equal(PlantResult.Planted, Planting.TryPlantFromBag(new Plot(PlotState.Tilled), bag, 0, SeedOf));
        Assert.Equal(1, bag.Slots[0]?.Count);
        Assert.Equal(new CarrySlot(TestBags.TurnipSeed, 1), Projection(bag)[0]);

        Assert.Equal(PlantResult.Planted, Planting.TryPlantFromBag(new Plot(PlotState.Tilled), bag, 0, SeedOf));
        Assert.Null(bag.Slots[0]);
        Assert.True(Projection(bag)[0].IsEmpty);

        // 别的格一颗没动。
        Assert.Equal(3, bag.Slots[bag.SlotCount - 1]?.Count);
    }

    [Fact]
    public void 拿着收上来的菜去播被拒_那一格与地都不变()
    {
        var bag = Packed();
        var before = bag.Slots.ToList();
        var plot = new Plot(PlotState.Tilled);

        var result = Planting.TryPlantFromBag(plot, bag, 2, SeedOf);

        Assert.Equal(PlantResult.NotASeed, result);
        Assert.Equal(before, bag.Slots);
        Assert.Equal(PlotState.Tilled, plot.State);
    }

    [Fact]
    public void 手上那一格是空的被拒_什么都不扣()
    {
        var bag = Packed();
        var before = bag.Slots.ToList();
        var plot = new Plot(PlotState.Tilled);

        Assert.Equal(PlantResult.NothingSelected, Planting.TryPlantFromBag(plot, bag, 1, SeedOf));

        Assert.Equal(before, bag.Slots);
        Assert.Equal(PlotState.Tilled, plot.State);
    }

    [Fact]
    public void 这一格不许播时不扣种子()
    {
        var bag = Packed();
        var before = bag.Slots.ToList();

        Assert.Equal(PlantResult.CellNotReady, Planting.TryPlantFromBag(new Plot(PlotState.Cleared), bag, 0, SeedOf));

        Assert.Equal(before, bag.Slots);
    }
}
