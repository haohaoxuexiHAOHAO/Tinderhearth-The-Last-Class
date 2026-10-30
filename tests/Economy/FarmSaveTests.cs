using System.Text;
using Tinderhearth.Rules.Economy;
using Tinderhearth.Rules.Foundation.Save;
using Xunit;

namespace Tinderhearth.Rules.Tests.Economy;

/// <summary>农场那一份正式存档：没有就开新档，有就读回，与场景里那片地对不上、读不出来都当场被拒。</summary>
public class FarmSaveTests
{
    private static readonly CropDefinition Turnip = new()
    {
        Id = "turnip",
        StageDays = [1, 1],
        RegrowFromStage = null,
        Seasons = [Season.Spring],
        YieldItemId = "item_turnip",
    };

    private static readonly IReadOnlyDictionary<string, CropDefinition> Crops =
        new Dictionary<string, CropDefinition> { [Turnip.Id] = Turnip };

    private static PlotGrid Scene(int x = 2, int y = 3, int width = 3, int height = 2) =>
        PlotGrid.Rectangle(new GridCell(x, y), width, height, PlotState.Tilled);

    private static SaveShardRegistry RegistryFor(PlotGrid scene)
    {
        var registry = new SaveShardRegistry();
        WorldShards.Register(registry, scene);
        return registry;
    }

    /// <summary>场景里那片地种了一格、日期推到夏季，写成一份存档的文本。</summary>
    private static string SavedOn(PlotGrid scene)
    {
        var world = new WorldState(new GameDate(1, Season.Summer, 4), scene.Clone());
        world.Plots.Cells.First().Plot.Plant(Turnip);
        return FarmSave.Write(RegistryFor(scene), world);
    }

    [Fact]
    public void 没有存档时按开局那一天与场景里那片地开新档()
    {
        var scene = Scene();

        var world = FarmSave.LoadOrNew(null, RegistryFor(scene), Crops, scene);

        Assert.Equal(Calendar.StartDate, world.Date);
        Assert.Equal(PlotSnapshot.Of(WorldState.NewGame(scene.Clone())), PlotSnapshot.Of(world));
        Assert.NotSame(scene, world.Plots);
    }

    [Fact]
    public void 有存档就读回存档里那一天与那片地()
    {
        var scene = Scene();

        var world = FarmSave.LoadOrNew(SavedOn(scene), RegistryFor(scene), Crops, scene);

        Assert.Equal(new GameDate(1, Season.Summer, 4), world.Date);
        Assert.Equal(PlotState.Planted, world.Plots.Cells.First().Plot.State);
    }

    [Fact]
    public void 场景里那片地挪过位置时被拒并说清两边各多了哪几格()
    {
        var text = SavedOn(Scene(x: 2));
        var moved = Scene(x: 3);

        var e = Assert.Throws<InvalidDataException>(() => FarmSave.LoadOrNew(text, RegistryFor(moved), Crops, moved));

        Assert.Contains("(5, 3)", e.Message);
        Assert.Contains("(2, 3)", e.Message);
    }

    [Fact]
    public void 场景里那片地改过大小时被拒()
    {
        var text = SavedOn(Scene(width: 3));
        var wider = Scene(width: 4);

        var e = Assert.Throws<InvalidDataException>(() => FarmSave.LoadOrNew(text, RegistryFor(wider), Crops, wider));

        Assert.Contains("(5, 3)", e.Message);
        Assert.Contains("(5, 4)", e.Message);
    }

    [Fact]
    public void 读不出来的存档整份被拒_不开新档()
    {
        var scene = Scene();

        var e = Assert.Throws<SaveRejectedException>(
            () => FarmSave.LoadOrNew("{ 写坏了", RegistryFor(scene), Crops, scene));

        Assert.Equal(SaveRejection.Unreadable, e.Reason);
    }

    [Fact]
    public void 报得出每个分片各占多少字节()
    {
        var text = SavedOn(Scene());
        string[] ids = [WorldShards.CalendarId, WorldShards.PlotsId];

        var sizes = SaveFile.ShardBytes(text);

        Assert.Equal(ids, sizes.Select(s => s.Id));
        Assert.All(sizes, s => Assert.True(s.Bytes > 0));
        Assert.True(sizes.Sum(s => s.Bytes) < Encoding.UTF8.GetByteCount(text));
    }
}
