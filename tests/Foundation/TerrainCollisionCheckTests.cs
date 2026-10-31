using Tinderhearth.Rules.Foundation.Terrain;
using Xunit;

namespace Tinderhearth.Rules.Tests.Foundation;

/// <summary>数据层的碰撞载入时那一次核对：那一层的两个开关，与同一种地形的标记瓦片碰撞齐不齐，一次全报。</summary>
/// <remarks>TileSet 本身在引擎层，这里交进去的是「每一张标了什么、带不带碰撞」，所以不用引擎就测得到。</remarks>
public class TerrainCollisionCheckTests
{
    private const int DataSet = 0;

    private static TerrainTile Tile(int terrain, string name, string tile, bool collision, int terrainSet = DataSet) =>
        new(terrainSet, terrain, name, tile, collision);

    /// <summary>照农场那份配好的样子：草与裸土不带碰撞，浅水的两张与深水那一张都带。</summary>
    private static TerrainTile[] Configured() =>
    [
        Tile(0, "grass", "grass-dirt.png（源 0）的 (0, 3)", collision: false),
        Tile(1, "baresoil", "grass-dirt.png（源 0）的 (2, 1)", collision: false),
        Tile(2, "riverwater", "grass-shallow.png（源 1）的 (2, 1)", collision: true),
        Tile(3, "deepwater", "shallow-deep.png（源 2）的 (2, 1)", collision: true),
        Tile(2, "riverwater", "shallow-deep.png（源 2）的 (0, 3)", collision: true),
    ];

    [Fact]
    public void 配好时没有问题()
    {
        Assert.Empty(TerrainCollisionCheck.Problems(true, true, DataSet, Configured()));
    }

    /// <summary>一张都没画碰撞也是齐的：哪几种地形该挡人这里判不了，那要实机走一遍才看得出来。</summary>
    [Fact]
    public void 全都不带碰撞也算齐()
    {
        TerrainTile[] tiles = [.. Configured().Select(tile => tile with { HasCollision = false })];

        Assert.Empty(TerrainCollisionCheck.Problems(true, true, DataSet, tiles));
    }

    [Fact]
    public void 数据层停用时报一条_说清改用眼睛藏()
    {
        var problem = Assert.Single(TerrainCollisionCheck.Problems(false, true, DataSet, Configured()));

        Assert.Contains("Enabled 关着", problem);
        Assert.Contains("Visible", problem);
    }

    [Fact]
    public void 数据层的碰撞关掉时报一条()
    {
        var problem = Assert.Single(TerrainCollisionCheck.Problems(true, false, DataSet, Configured()));

        Assert.Contains("Collision Enabled 关着", problem);
    }

    /// <summary>浅水两张只画了一张：点名那种地形，带的与不带的各是哪几张。</summary>
    [Fact]
    public void 同一种地形有的带碰撞有的不带时点名每一张()
    {
        var tiles = Configured();
        tiles[4] = tiles[4] with { HasCollision = false };

        var problem = Assert.Single(TerrainCollisionCheck.Problems(true, true, DataSet, tiles));

        Assert.Contains("「riverwater」（编号 2）", problem);
        Assert.Contains("带碰撞的是 grass-shallow.png（源 1）的 (2, 1)", problem);
        Assert.Contains("不带的是 shallow-deep.png（源 2）的 (0, 3)", problem);
    }

    /// <summary>两个开关都关着、两种地形各不齐：四条一次报出来，不停在第一处，先开关后地形、地形按编号排。</summary>
    [Fact]
    public void 几处不对一次全报()
    {
        TerrainTile[] tiles =
        [
            .. Configured(),
            Tile(3, "deepwater", "grass-shallow.png（源 1）的 (1, 1)", collision: false),
            Tile(0, "grass", "grass-shallow.png（源 1）的 (0, 3)", collision: true),
        ];

        var problems = TerrainCollisionCheck.Problems(false, false, DataSet, tiles);

        Assert.Equal(4, problems.Count);
        Assert.Contains("Enabled 关着", problems[0]);
        Assert.Contains("Collision Enabled 关着", problems[1]);
        Assert.Contains("「grass」（编号 0）", problems[2]);
        Assert.Contains("「deepwater」（编号 3）", problems[3]);
    }

    /// <summary>别的地形集上的瓦片、没标地形的瓦片，带不带碰撞都不参与。</summary>
    [Fact]
    public void 只看数据层那个地形集里标了地形的瓦片()
    {
        TerrainTile[] tiles =
        [
            .. Configured(),
            Tile(2, "other", "other.png（源 5）的 (0, 0)", collision: false, terrainSet: 1),
            Tile(-1, "", "grass-shallow.png（源 1）的 (3, 3)", collision: false, terrainSet: -1),
            Tile(-1, "", "grass-shallow.png（源 1）的 (3, 2)", collision: true, terrainSet: DataSet),
        ];

        Assert.Empty(TerrainCollisionCheck.Problems(true, true, DataSet, tiles));
    }
}
