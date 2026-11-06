using Tinderhearth.Rules.Foundation.Config;
using Tinderhearth.Rules.UI;
using Xunit;

namespace Tinderhearth.Rules.Tests.UI;

/// <summary>手环里背包那一页怎么排：几行几列跟着背包网格、格距、整页放不放得进手环的内容区。</summary>
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
    public void 格与格之间放得下两圈选中框()
    {
        // 选中框画在格子外面一圈；隔得比两圈窄，挨着的两格各画一圈就叠在一起。
        Assert.True(BagPageLayout.CellGap >= 2 * UIMetrics.SelectOutset);
    }

    [Fact]
    public void 格子那一片的尺寸由几行几列_格子边长与格距算出来()
    {
        Assert.Equal((6 * UIMetrics.Cell) + (5 * BagPageLayout.CellGap), BagPageLayout.GridWidth(6));
        Assert.Equal((2 * UIMetrics.Cell) + BagPageLayout.CellGap, BagPageLayout.GridHeight(2));
        Assert.Equal(UIMetrics.Cell, BagPageLayout.GridHeight(1));
        Assert.Equal(BagPageLayout.GridWidth(6) + (2 * BagPageLayout.FramePadding), BagPageLayout.FrameWidth(6));
        Assert.Throws<ArgumentOutOfRangeException>(() => BagPageLayout.GridWidth(0));
    }

    [Fact]
    public void 配置里每一级的背包整页都放得进手环的内容区()
    {
        var levels = ShippedConfig().BackpackGridByLevel;
        Assert.NotEmpty(levels);

        foreach (var grid in levels)
        {
            Assert.True(BagPageLayout.PageHeight(grid.Height) <= WristbandLayout.ContentHeight,
                $"背包 {grid} 的那一页要 {BagPageLayout.PageHeight(grid.Height)} 高，手环的内容区只有 {WristbandLayout.ContentHeight}");
            Assert.True(BagPageLayout.FrameWidth(grid.Width) <= WristbandLayout.ContentWidth,
                $"背包 {grid} 那一圈厚框要 {BagPageLayout.FrameWidth(grid.Width)} 宽，手环的内容区只有 {WristbandLayout.ContentWidth}");
        }
    }
}
