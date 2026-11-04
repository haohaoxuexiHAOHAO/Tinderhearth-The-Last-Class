using Tinderhearth.Rules.Foundation.Terrain;
using Xunit;

namespace Tinderhearth.Rules.Tests.Foundation;

/// <summary>地面纹理声明载入时那一次核对：每种地形都有一项、每项二选一、尺寸整格、主色不撞，一次全报。</summary>
/// <remarks>贴图与 TileSet 在引擎层，这里交进去的是「每一项写了什么、图多大、各地形的主色」，所以不用引擎就测得到。</remarks>
public class TerrainFillCheckTests
{
    private const int Cell = 16;

    private static readonly Rgb8 Moss3 = new(80, 102, 54);
    private static readonly Rgb8 Soil4 = new(122, 90, 63);
    private static readonly Rgb8 Water4 = new(85, 121, 133);
    private static readonly Rgb8 Water2 = new(40, 66, 81);

    /// <summary>照农场那份数据层：四种地形，主色从三套双网格图里读出来。</summary>
    private static FillTerrain[] Terrains() =>
    [
        new(0, "grass", Moss3),
        new(1, "baresoil", Soil4),
        new(2, "riverwater", Water4),
        new(3, "deepwater", Water2),
    ];

    private static FillDeclaration Texture(int index, int terrain, int width = 64, int height = 64) =>
        new(index, Empty: false, terrain, HasTexture: true, NoFill: false, width, height);

    private static FillDeclaration Skip(int index, int terrain) =>
        new(index, Empty: false, terrain, HasTexture: false, NoFill: true, Width: 0, Height: 0);

    private static FillDeclaration Blank(int index) =>
        new(index, Empty: true, Terrain: -1, HasTexture: false, NoFill: false, Width: 0, Height: 0);

    /// <summary>先只画了草：草给图，其余三种明确不铺。</summary>
    private static FillDeclaration[] GrassOnly() =>
    [
        Texture(0, 0),
        Skip(1, 1),
        Skip(2, 2),
        Skip(3, 3),
    ];

    /// <summary>一项都没有是「不铺纹理」，地面照原样画，不是配错。</summary>
    [Fact]
    public void 一项都没声明时不报()
    {
        Assert.Empty(TerrainFillCheck.Problems(Terrains(), [], Cell));
    }

    [Fact]
    public void 每种都声明了且尺寸整格时不报()
    {
        Assert.Empty(TerrainFillCheck.Problems(Terrains(), GrassOnly(), Cell));
    }

    [Fact]
    public void 漏了的地形一种一条()
    {
        var problems = TerrainFillCheck.Problems(Terrains(), [Texture(0, 0), Skip(1, 1)], Cell);

        Assert.Equal(2, problems.Count);
        Assert.Contains("「riverwater」（编号 2）没有声明", problems[0]);
        Assert.Contains("「deepwater」（编号 3）没有声明", problems[1]);
    }

    /// <summary>复制一项忘了改地形，是最常见的手滑：两项都点名。</summary>
    [Fact]
    public void 同一种地形声明两次时点名两项()
    {
        FillDeclaration[] fills = [.. GrassOnly(), Texture(4, 0)];

        var problem = Assert.Single(TerrainFillCheck.Problems(Terrains(), fills, Cell));

        Assert.Contains("「grass」（编号 0）声明了 2 次：第 0、4 项", problem);
    }

    [Fact]
    public void 地形编号没填或不存在时说清()
    {
        FillDeclaration[] fills = [.. GrassOnly(), Texture(4, -1), Texture(5, 7)];

        var problems = TerrainFillCheck.Problems(Terrains(), fills, Cell);

        Assert.Equal(2, problems.Count);
        Assert.Contains("第 4 项没填 Terrain", problems[0]);
        Assert.Contains("第 5 项的 Terrain 是 7", problems[1]);
        Assert.Contains("0 grass", problems[1]);
    }

    [Fact]
    public void 空着的一格要说是空的()
    {
        FillDeclaration[] fills = [.. GrassOnly(), Blank(4)];

        var problem = Assert.Single(TerrainFillCheck.Problems(Terrains(), fills, Cell));

        Assert.Contains("第 4 项是空的", problem);
    }

    [Fact]
    public void 纹理图与不铺要二选一()
    {
        var fills = GrassOnly();
        fills[0] = fills[0] with { NoFill = true };
        fills[1] = fills[1] with { NoFill = false };

        var problems = TerrainFillCheck.Problems(Terrains(), fills, Cell);

        Assert.Equal(2, problems.Count);
        Assert.Contains("第 0 项（地形「grass」（编号 0））既给了纹理图又勾了 No Fill", problems[0]);
        Assert.Contains("第 1 项（地形「baresoil」（编号 1））既没给纹理图也没勾 No Fill", problems[1]);
    }

    [Theory]
    [InlineData(60, 64)]
    [InlineData(64, 40)]
    [InlineData(17, 16)]
    public void 纹理图不是整格时说出多大(int width, int height)
    {
        var fills = GrassOnly();
        fills[0] = Texture(0, 0, width, height);

        var problem = Assert.Single(TerrainFillCheck.Problems(Terrains(), fills, Cell));

        Assert.Contains($"纹理图是 {width}×{height}", problem);
        Assert.Contains("格边长 16 的整数倍", problem);
    }

    [Theory]
    [InlineData(16, 16)]
    [InlineData(64, 32)]
    [InlineData(128, 128)]
    public void 整格的纹理图不报(int width, int height)
    {
        var fills = GrassOnly();
        fills[0] = Texture(0, 0, width, height);

        Assert.Empty(TerrainFillCheck.Problems(Terrains(), fills, Cell));
    }

    /// <summary>着色器按颜色认地形，两种同色就会一起铺上同一张。</summary>
    [Fact]
    public void 主色撞了且其中一种要铺纹理时报()
    {
        var terrains = Terrains();
        terrains[1] = terrains[1] with { Key = Moss3 };

        var problem = Assert.Single(TerrainFillCheck.Problems(terrains, GrassOnly(), Cell));

        Assert.Contains("「grass」（编号 0）与地形「baresoil」（编号 1）的主色都是 #506636", problem);
    }

    /// <summary>撞色的两种都不铺时没有像素会被换掉，不报。</summary>
    [Fact]
    public void 主色撞了但两种都不铺时不报()
    {
        var terrains = Terrains();
        terrains[3] = terrains[3] with { Key = Water4 };

        Assert.Empty(TerrainFillCheck.Problems(terrains, GrassOnly(), Cell));
    }

    [Fact]
    public void 要铺纹理却读不出主色时报()
    {
        var terrains = Terrains();
        terrains[0] = terrains[0] with { Key = null };

        var problem = Assert.Single(TerrainFillCheck.Problems(terrains, GrassOnly(), Cell));

        Assert.Contains("读不出它的主色", problem);
    }

    /// <summary>几种错一起出现：一次报全，先按项号报每一项自己的错，再报两次的、没声明的、撞色的。</summary>
    [Fact]
    public void 几处不对一次全报_顺序固定()
    {
        var terrains = Terrains();
        terrains[2] = terrains[2] with { Key = Moss3 };
        FillDeclaration[] fills =
        [
            Texture(0, 0, width: 60),
            Texture(1, 2),
            Texture(2, 0),
            Blank(3),
        ];

        var problems = TerrainFillCheck.Problems(terrains, fills, Cell);

        Assert.Equal(6, problems.Count);
        Assert.Contains("第 0 项（地形「grass」（编号 0））的纹理图是 60×64", problems[0]);
        Assert.Contains("第 3 项是空的", problems[1]);
        Assert.Contains("「grass」（编号 0）声明了 2 次：第 0、2 项", problems[2]);
        Assert.Contains("「baresoil」（编号 1）没有声明", problems[3]);
        Assert.Contains("「deepwater」（编号 3）没有声明", problems[4]);
        Assert.Contains("「grass」（编号 0）与地形「riverwater」（编号 2）的主色都是 #506636", problems[5]);
    }
}
