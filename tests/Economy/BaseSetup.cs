using Tinderhearth.Rules.Economy;
using Tinderhearth.Rules.Foundation.Config;
using Tinderhearth.Rules.Foundation.Save;
using Tinderhearth.Rules.Progression;
using Tinderhearth.Rules.Tests.Progression;
using Tinderhearth.Rules.UI;

namespace Tinderhearth.Rules.Tests.Economy;

/// <summary>基地那一套：工程里的作物、物品与配置，场景里一片地，写档写进内存。几份端到端都从这里搭。</summary>
/// <remarks>
/// 变异的随机源由测试按顺序给。物品表默认读工程里那一份；要测工程里没有的形状（比如一样占不止一格的收成）时，
/// 由测试换一份，作物照旧读工程里的。
/// </remarks>
internal sealed class BaseSetup
{
    private const int FallowDays = 7;

    private readonly Queue<bool> _rolls;

    public BaseSetup(IEnumerable<bool> rolls, ItemCatalog? items = null)
    {
        _rolls = new Queue<bool>(rolls);
        Items = items ?? ShippedItems.Load();
        Crops = CropSheetE2ETests.ShippedCrops();
        var config = GameConfig.Parse(File.ReadAllText(Path.Combine(RepoFiles.Root(), "data", "config", "game.json")));
        NewGameBag = Bag.NewGame(config, Items, CarrySlotBar.SlotCount);
        Settlement = StandardSettlement.Create(Crops, FallowDays, () => _rolls.Dequeue(), _ => { }, _ => { });
    }

    /// <summary>场景里那片地：2×2，左上角不在原点，读回来时格坐标真的被核到。</summary>
    public PlotGrid Scene { get; } = PlotGrid.Rectangle(new GridCell(4, 2), width: 2, height: 2, PlotState.Tilled);

    public ItemCatalog Items { get; }

    public IReadOnlyDictionary<string, CropDefinition> Crops { get; }

    /// <summary>开新档时的背包：网格第一级那么大，随身栏那一排与技能位同数，空的。</summary>
    public Bag NewGameBag { get; }

    public DailySettlement Settlement { get; }

    /// <summary>还没有存档时进基地。</summary>
    public CurrentWorld NewGame() => new(Load(null));

    /// <summary>睡一觉：走结算那唯一的入口。</summary>
    public SettlementOutcome Sleep(CurrentWorld world) => Settlement.EndDay(world, DayEndSource.Sleep);

    /// <summary>手上是随身栏第一格，播到给定那一格。</summary>
    public PlantResult PlantFromFirstSlot(CurrentWorld world, GridCell cell) =>
        Planting.TryPlantFromBag(PlotAt(world.State, cell), world.State.Bag, 0, CropDefinition.BySeed(Crops).GetValueOrDefault);

    public string Write(WorldState world) => BaseSave.Write(Registry(), world);

    public WorldState Load(string? saved) => BaseSave.LoadOrNew(saved, Registry(), Crops, Scene, NewGameBag);

    /// <summary>那片地里的一格；不在那片地里当场抛。</summary>
    public static Plot PlotAt(WorldState world, GridCell cell) =>
        world.Plots.TryGet(cell, out var plot) ? plot : throw new InvalidOperationException($"没有 {cell} 这一格");

    private SaveShardRegistry Registry()
    {
        var registry = new SaveShardRegistry();
        WorldShards.Register(registry, Scene, NewGameBag);
        return registry;
    }
}
