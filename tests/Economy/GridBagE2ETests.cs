using System.Text.Json.Nodes;
using Tinderhearth.Rules.Economy;
using Tinderhearth.Rules.Foundation.Save;
using Tinderhearth.Rules.Progression;
using Tinderhearth.Rules.Tests.Progression;
using Tinderhearth.Rules.Tests.UI;
using Tinderhearth.Rules.UI;
using Xunit;

namespace Tinderhearth.Rules.Tests.Economy;

/// <summary>
/// 背包改成网格那份需求的端到端：种子进随身栏那一排、大东西进网格、从随身栏播种、收获进网格、挪开大东西、一键整理、写档读回。
/// </summary>
/// <remarks>
/// 作物、物品与配置读的都是工程里那一份（只读不写），走基地用的那几个读口：调试键给种子的那一批（<see cref="SeedBatches"/>）、
/// 给大东西的那一件（<see cref="BigItemSamples"/>）、从随身栏播种、结算驱动、收获那一道闸、挪格与整理、写档与读档；写档写进内存、
/// 不碰真文件。工程里的收成都只占一格，所以「收成放不下这么大的」那一条换一份物品表，只把一样收成改成占 2×2。
/// 基地那一套搭在 <see cref="BaseSetup"/>，各一环自己的边界在各自的测试类里，这里只把它们串起来。
/// </remarks>
public class GridBagE2ETests
{
    /// <summary>改成网格之前那一版的存档版本号：背包那一片一格一项，随身栏是开头那几格。</summary>
    private const int FlatBagVersion = 2;

    private static readonly HarvestYieldRule Yield = new(BaseCount: 4, MinWaterFactor: 0.5, MaxWaterFactor: 1.0);

    /// <summary>播下去的那两格。</summary>
    private static readonly GridCell First = new(4, 2);
    private static readonly GridCell Second = new(5, 2);

    [Fact]
    public void 主路径_种子进随身栏大东西进网格_播种收获进网格_挪开大东西腾出一块_一键整理_写档读回每一堆的位置与件数都对()
    {
        var setup = new BaseSetup(rolls: [true, false]);
        var world = setup.NewGame();
        var bag = world.State.Bag;

        // 调试键给的第一批种子落满随身栏那一排，网格还空着。
        Assert.True(bag.TryAddAll(SeedBatches.Batch(setup.Crops.Values, 0, CarrySlotBar.SlotCount, setup.Items)));
        Assert.All(bag.Carry, slot => Assert.NotNull(slot));
        Assert.All(bag.Slots, slot => Assert.Null(slot));

        // 调试键给的第一件大东西落在网格左上角，占满它那一块。
        var (bigId, bigQuality, bigCount) = BigItemSamples.Sample(setup.Items, 0);
        var footprint = setup.Items.Items[bigId].Footprint;
        Assert.True(bag.TryAdd(bigId, bigQuality, bigCount));
        Assert.Equal(BlockCells(0, footprint, bag.GridWidth), CoveredBy(bag, 0));

        // 从随身栏第一格播两格，推进到成熟：一株变了、一株没变。
        var seed = bag.Carry[0]!.Value;
        var crop = CropDefinition.BySeed(setup.Crops)[seed.ItemId];
        Assert.Equal(PlantResult.Planted, setup.PlantFromFirstSlot(world, First));
        Assert.Equal(PlantResult.Planted, setup.PlantFromFirstSlot(world, Second));
        for (var night = 0; night < crop.DaysToFirstRipe; night++)
        {
            Assert.True(setup.Sleep(world).Advanced);
        }

        // 收进来：随身栏那一排满着，变了的与没变的各一堆，都落进网格。睡过之后世界换了一份，背包现读。
        bag = world.State.Bag;
        Assert.Equal(seed.Count - 2, bag.Carry[0]?.Count);
        foreach (var cell in (GridCell[])[First, Second])
        {
            Assert.Equal(HarvestStatus.Stored, Harvesting.ByProtagonist(BaseSetup.PlotAt(world.State, cell), crop, Yield, bag).Status);
        }

        Assert.Equal(2, bag.Slots.OfType<ItemStack>().Count(s => s.ItemId == crop.YieldItemId));
        Assert.DoesNotContain(bag.Carry, s => s?.ItemId == crop.YieldItemId);

        // 挪开大东西：从它左上角拿起，放到网格右边那一块。原来那一块空出来，再给一件大东西，第一块放得下的空地就是它。
        var target = bag.GridWidth - footprint.Width;
        Assert.Equal(BagMoveResult.Moved, bag.Move(BagPlace.Grid(0), BagPlace.Grid(target)));
        Assert.Equal(BlockCells(target, footprint, bag.GridWidth), CoveredBy(bag, target));
        var (nextId, nextQuality, nextCount) = BigItemSamples.Sample(setup.Items, BigItemSamples.Next(0, BigItemSamples.Kinds(setup.Items).Count));
        Assert.True(bag.TryAdd(nextId, nextQuality, nextCount));
        Assert.Equal(nextId, bag.StackAt(BagPlace.Grid(0))?.ItemId);

        // 一键整理：照进来时的排序键排网格，随身栏那一排不动，东西一件不少。
        var carryBefore = bag.Carry.ToList();
        var totalsBefore = Totals(bag.Carry.Concat(bag.Slots));
        Assert.Equal(BagArrangeResult.Arranged, new BagControls().Tidy(bag));
        Assert.Equal(carryBefore, bag.Carry);
        Assert.Equal(totalsBefore, Totals(bag.Carry.Concat(bag.Slots)));

        // 停掉游戏再进来：读回写出的那一份，每一堆的位置与件数、日期与每一格地都一样。
        var back = setup.Load(setup.Write(world.State));
        Assert.Equal(world.State.Date, back.Date);
        Assert.Equal(PlotSnapshot.Of(world.State), PlotSnapshot.Of(back));
        Assert.Equal(bag.Carry, back.Bag.Carry);
        Assert.Equal(bag.Slots, back.Bag.Slots);
    }

    [Fact]
    public void 失败路径_空格够数却凑不出够大的一块时收获被拒_那一株还在_背包一件不变()
    {
        // 工程里的收成都只占一格：换一份物品表，把第一种作物的收成改成占 2×2，别的照旧。
        var shipped = ShippedItems.Load();
        var crop = CropSheetE2ETests.ShippedCrops().Values.OrderBy(c => c.Id, StringComparer.Ordinal).First();
        var items = ItemCatalog.Build(
            [
                .. shipped.Items.Values.Select(item => item.Id == crop.YieldItemId
                    ? item with { Footprint = new() { Width = 2, Height = 2 } }
                    : item),
            ],
            shipped.Categories.Values,
            shipped.Rarities,
            shipped.Qualities,
            PaletteFile.ReadPalette().BandOf);
        var setup = new BaseSetup(rolls: [false], items);
        var world = setup.NewGame();
        Assert.True(BaseSetup.PlotAt(world.State, First).Plant(crop));
        for (var night = 0; night < crop.DaysToFirstRipe; night++)
        {
            Assert.True(setup.Sleep(world).Advanced);
        }

        // 睡过之后世界换了一份：那一格与背包都现读。
        var plot = BaseSetup.PlotAt(world.State, First);
        Assert.Equal(PlotState.Harvestable, plot.State);

        // 随身栏那一排与网格都放满一格大的种子，再隔一格拿掉网格里的一堆：空格有好几格，却没有一块 2×2 全空着。
        var bag = world.State.Bag;
        var seeds = setup.Items.Items.Values
            .Where(i => !i.HasQuality && i.Footprint.Width * i.Footprint.Height == 1)
            .OrderBy(i => i.Id, StringComparer.Ordinal)
            .ToList();
        for (var slot = 0; slot < bag.CarryCount + bag.SlotCount; slot++)
        {
            var kind = seeds[slot % seeds.Count];
            Assert.True(bag.TryAdd(kind.Id, null, kind.StackLimit));
        }

        for (var cell = 0; cell < bag.SlotCount; cell++)
        {
            if (((cell % bag.GridWidth) + (cell / bag.GridWidth)) % 2 == 0)
            {
                Assert.True(bag.TryRemoveAt(cell, bag.Slots[cell]!.Value.Count));
            }
        }

        Assert.True(bag.EmptySlotCount >= 4, $"空格只有 {bag.EmptySlotCount} 个，凑不出「空格够数」");
        var before = bag.Carry.Concat(bag.Slots).ToList();

        var attempt = Harvesting.ByProtagonist(plot, crop, Yield, bag);

        Assert.Equal(HarvestStatus.NoRoomForSize, attempt.Status);
        Assert.Equal(PlotState.Harvestable, plot.State);
        Assert.Equal(before, bag.Carry.Concat(bag.Slots));
    }

    [Fact]
    public void 失败路径_大东西随身栏空着也不进那一排_挪进去被拒_背包一件不变()
    {
        var setup = new BaseSetup(rolls: []);
        var bag = setup.NewGame().State.Bag;
        var (bigId, bigQuality, bigCount) = BigItemSamples.Sample(setup.Items, 0);

        Assert.True(bag.TryAdd(bigId, bigQuality, bigCount));
        Assert.All(bag.Carry, slot => Assert.Null(slot));
        var before = bag.Carry.Concat(bag.Slots).ToList();

        Assert.Equal(BagMoveResult.TooBigForCarry, bag.Move(BagPlace.Grid(0), BagPlace.Carry(0)));
        Assert.Equal(before, bag.Carry.Concat(bag.Slots));
    }

    [Fact]
    public void 失败路径_照新的先后摆不下全部时_排序与一键整理都一格不动()
    {
        // 第一级的网格摆满：左边四堆一格大的种子，右边两颗大东西。按最近获得排，后进来的种子排在前面、先占掉第一行，
        // 第二颗大东西就凑不出它那一块。原来的摆法放得下，新摆法放不下。
        var setup = new BaseSetup(rolls: []);
        var bigOnes = BigItemSamples.Kinds(setup.Items).Take(2).Select(id => setup.Items.Items[id]).ToList();
        var seeds = setup.Items.Items.Values.Where(i => !i.HasQuality).OrderBy(i => i.Id, StringComparer.Ordinal).Take(4).ToList();
        Assert.Equal(2, bigOnes.Count);
        Assert.All(bigOnes, item => Assert.Equal(new ItemFootprint { Width = 2, Height = 2 }, item.Footprint));
        var grid = setup.NewGameBag;
        var anchors = new ItemStack?[grid.SlotCount];
        anchors[0] = new ItemStack(seeds[0].Id, null, 1, 3);
        anchors[1] = new ItemStack(seeds[1].Id, null, 1, 4);
        anchors[grid.GridWidth] = new ItemStack(seeds[2].Id, null, 1, 5);
        anchors[grid.GridWidth + 1] = new ItemStack(seeds[3].Id, null, 1, 6);
        anchors[2] = new ItemStack(bigOnes[0].Id, Harvesting.UnmutatedQualityId, 1, 1);
        anchors[4] = new ItemStack(bigOnes[1].Id, Harvesting.UnmutatedQualityId, 1, 2);
        var bag = Bag.Restore(new ItemStack?[grid.CarryCount], grid.GridWidth, grid.GridHeight, anchors, setup.Items);
        Assert.All(Enumerable.Range(0, bag.SlotCount), cell => Assert.NotNull(bag.AnchorOf(BagPlace.Grid(cell))));
        var before = bag.Slots.ToList();
        var controls = new BagControls();

        Assert.Equal(BagArrangeResult.NoRoom, controls.SortBy(bag, BagSortKey.Recent));
        Assert.Equal(before, bag.Slots);
        Assert.Equal(BagArrangeResult.NoRoom, controls.Tidy(bag));
        Assert.Equal(before, bag.Slots);
    }

    [Fact]
    public void 读改成网格之前那一版的存档_开头那几格进随身栏_其余照格号摆进网格_东西一件不少_再写再读一样()
    {
        var setup = new BaseSetup(rolls: []);
        var crop = setup.Crops.Values.OrderBy(c => c.Id, StringComparer.Ordinal).First();
        var carry = CarrySlotBar.SlotCount;
        var slots = new ItemStack?[carry + 4];
        slots[0] = new ItemStack(crop.SeedItemId, null, 12, 1);
        slots[2] = new ItemStack(crop.YieldItemId, Harvesting.UnmutatedQualityId, 3, 2);
        slots[carry] = new ItemStack(crop.YieldItemId, Harvesting.MutatedQualityId, 4, 3);
        slots[carry + 3] = new ItemStack(crop.SeedItemId, null, 2, 4);
        var root = JsonNode.Parse(setup.Write(setup.NewGame().State))!.AsObject();
        root[SaveFile.VersionKey] = FlatBagVersion;
        root[SaveFile.ShardsKey]![WorldShards.BagId] = FlatBag(slots);

        var back = setup.Load(root.ToJsonString());

        Assert.Equal(slots[..carry], back.Bag.Carry);
        Assert.Equal(slots[carry], back.Bag.StackAt(BagPlace.Grid(0)));
        Assert.Equal(slots[carry + 3], back.Bag.StackAt(BagPlace.Grid(3)));
        Assert.Equal(Totals(slots), Totals(back.Bag.Carry.Concat(back.Bag.Slots)));

        // 写出去是现在的形状，再读回来一样。
        var again = setup.Load(setup.Write(back));
        Assert.Equal(back.Bag.Carry, again.Bag.Carry);
        Assert.Equal(back.Bag.Slots, again.Bag.Slots);
    }

    /// <summary>左上角在网格第 <paramref name="anchor"/> 格、占地这么大的一块，盖着网格里哪几格，从上到下、从左到右。</summary>
    private static List<int> BlockCells(int anchor, ItemFootprint footprint, int gridWidth) =>
        [
            .. Enumerable.Range(0, footprint.Height)
                .SelectMany(dy => Enumerable.Range(0, footprint.Width).Select(dx => anchor + (dy * gridWidth) + dx)),
        ];

    /// <summary>网格里被左上角在第 <paramref name="anchor"/> 格的那一堆盖着的格，从上到下、从左到右。</summary>
    private static List<int> CoveredBy(Bag bag, int anchor) =>
        [.. Enumerable.Range(0, bag.SlotCount).Where(cell => bag.AnchorOf(BagPlace.Grid(cell)) == BagPlace.Grid(anchor))];

    /// <summary>每一种每一档各几件，按标识与品质排好：位置变了也比得了。</summary>
    private static List<(string ItemId, string? QualityId, int Count)> Totals(IEnumerable<ItemStack?> stacks) =>
        [
            .. stacks.OfType<ItemStack>()
                .GroupBy(s => (s.ItemId, s.QualityId))
                .Select(g => (g.Key.ItemId, g.Key.QualityId, g.Sum(s => s.Count)))
                .OrderBy(t => t.ItemId, StringComparer.Ordinal)
                .ThenBy(t => t.QualityId, StringComparer.Ordinal),
        ];

    /// <summary>改成网格之前那一版的背包那一片：一格一项，种类、品质、件数、序号各一串，随身栏是开头那几格。</summary>
    private static JsonObject FlatBag(IReadOnlyList<ItemStack?> slots) => new()
    {
        ["itemId"] = new JsonArray([.. slots.Select(s => JsonValue.Create(s?.ItemId))]),
        ["qualityId"] = new JsonArray([.. slots.Select(s => JsonValue.Create(s?.QualityId))]),
        ["count"] = new JsonArray([.. slots.Select(s => (JsonNode)JsonValue.Create(s?.Count ?? 0))]),
        ["acquired"] = new JsonArray([.. slots.Select(s => (JsonNode)JsonValue.Create(s?.AcquiredSequence ?? 0))]),
    };
}
