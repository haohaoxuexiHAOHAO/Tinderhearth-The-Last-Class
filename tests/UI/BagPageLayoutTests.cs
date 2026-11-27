using Tinderhearth.Rules.Foundation.Config;
using Tinderhearth.Rules.UI;
using Xunit;

namespace Tinderhearth.Rules.Tests.UI;

/// <summary>游戏菜单里背包那一页怎么排：格子框挨着格子框、随身栏那一排占整行、大东西那一块多大、图放在哪、整页放不放得进内容区。</summary>
/// <remarks>
/// 放不下不报错：格子那一片会顶出内容区、压到外框上，或者最后一行被截掉，而那只在背包升到那一级之后才看得到。
/// 所以按配置里每一级的网格各判一次，不等玩家升级。
/// </remarks>
public class BagPageLayoutTests
{
    private static GameConfig ShippedConfig()
    {
        var path = Path.Combine(RepoFiles.Root(), "data", "config", "game.json");
        return GameConfig.Parse(File.ReadAllText(path));
    }

    [Fact]
    public void 格子框挨着格子框_那一片多大只由几格算出来()
    {
        Assert.Equal(6 * UIMetrics.Cell, BagPageLayout.GridWidth(6));
        Assert.Equal(UIMetrics.Cell, BagPageLayout.GridHeight(1));
        Assert.Equal(BagPageLayout.GridWidth(6) + (2 * BagPageLayout.FramePadding), BagPageLayout.FrameWidth(6));
        Assert.Equal(new PixelPoint(2 * UIMetrics.Cell, 3 * UIMetrics.Cell), BagPageLayout.CellOrigin(2, 3));
        Assert.Throws<ArgumentOutOfRangeException>(() => BagPageLayout.GridWidth(0));
    }

    [Fact]
    public void 随身栏那几格不够一行也占一整行_网格从下一行开始()
    {
        Assert.Equal(1, BagPageLayout.CarryRows(6, 6));
        Assert.Equal(1, BagPageLayout.CarryRows(4, 6));
        Assert.Equal(2, BagPageLayout.CarryRows(7, 6));
        Assert.Equal(0, BagPageLayout.CarryRows(0, 6));
        Assert.Equal(1 + 3, BagPageLayout.PageRows(3, 6, 6));
    }

    [Fact]
    public void 大东西那一块是几张格子框那么大_图照占地的大小放在正中()
    {
        Assert.Equal(new PixelSize(2 * UIMetrics.Cell, 2 * UIMetrics.Cell), BagPageLayout.BlockSize(2, 2));
        Assert.Equal(new PixelSize(2 * UIMetrics.BaseUnit, 2 * UIMetrics.BaseUnit), BagPageLayout.PictureSize(2, 2));
        Assert.Equal(new PixelPoint(UIMetrics.CellInset, UIMetrics.CellInset), BagPageLayout.PictureOffset(1, 1));

        var block = BagPageLayout.BlockSize(1, 3);
        var picture = BagPageLayout.PictureSize(1, 3);
        var at = BagPageLayout.PictureOffset(1, 3);
        Assert.Equal(block.Width - picture.Width, 2 * at.X);
        Assert.Equal(block.Height - picture.Height, 2 * at.Y);
    }

    [Fact]
    public void 一格那一块上的预览正好是当前操作格那张件的原尺寸()
    {
        // 那张件比一格地大一圈；1 格那一块上不拉，大的那几块按九宫格拉开。
        var previewSide = UIMetrics.Cell - (2 * BagPageLayout.PreviewInset);
        Assert.Equal(WorldUILayout.CellCursorSide(UIMetrics.BaseUnit), previewSide);
    }

    [Fact]
    public void 配置里每一级的背包整页都放得进游戏菜单的内容区()
    {
        var levels = ShippedConfig().BackpackGridByLevel;
        Assert.NotEmpty(levels);

        foreach (var grid in levels)
        {
            // 页上的行数算上随身栏那一排：它画在网格上面，占自己的一行。
            var rows = BagPageLayout.PageRows(grid.Height, CarrySlotBar.SlotCount, grid.Width);
            Assert.True(BagPageLayout.PageHeight(rows) <= GameMenuLayout.ContentHeight,
                $"背包 {grid} 的那一页要 {BagPageLayout.PageHeight(rows)} 高，游戏菜单的内容区只有 {GameMenuLayout.ContentHeight}");
            Assert.True(BagPageLayout.FrameWidth(grid.Width) <= GameMenuLayout.ContentWidth,
                $"背包 {grid} 那一圈厚框要 {BagPageLayout.FrameWidth(grid.Width)} 宽，游戏菜单的内容区只有 {GameMenuLayout.ContentWidth}");
        }
    }
}
