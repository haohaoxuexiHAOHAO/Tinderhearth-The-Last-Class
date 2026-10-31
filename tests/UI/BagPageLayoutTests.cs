using Tinderhearth.Rules.Foundation.Config;
using Tinderhearth.Rules.UI;
using Xunit;

namespace Tinderhearth.Rules.Tests.UI;

/// <summary>手环里背包那一页怎么排：一行几格、格距、整页放不放得进手环的内容区。</summary>
/// <remarks>
/// 放不下不报错：格子那一片会顶出内容区、压到外框上，或者最后一行被截掉，而那只在背包升到那一级之后才看得到。
/// 所以按配置里每一级的格数各判一次，不等玩家升级。
/// </remarks>
public class BagPageLayoutTests
{
    private static GameConfig ShippedConfig()
    {
        var path = Path.Combine(RepoFiles.Root(), "data", "config", "game.json");
        return GameConfig.Parse(File.ReadAllText(path));
    }

    [Fact]
    public void 一行四格_格数多了往下加行()
    {
        Assert.Equal(4, BagPageLayout.SlotsPerRow);
        Assert.Equal(1, BagPageLayout.RowsFor(1));
        Assert.Equal(1, BagPageLayout.RowsFor(4));
        Assert.Equal(2, BagPageLayout.RowsFor(5));
        Assert.Equal(4, BagPageLayout.RowsFor(16));
        Assert.Throws<ArgumentOutOfRangeException>(() => BagPageLayout.RowsFor(0));
    }

    [Fact]
    public void 格与格之间放得下两圈选中框()
    {
        // 选中框画在格子外面一圈；隔得比两圈窄，挨着的两格各画一圈就叠在一起。
        Assert.True(BagPageLayout.CellGap >= 2 * UIMetrics.SelectOutset);
    }

    [Fact]
    public void 格子那一片的尺寸由格子边长与格距算出来()
    {
        Assert.Equal((4 * UIMetrics.Cell) + (3 * BagPageLayout.CellGap), BagPageLayout.GridWidth);
        Assert.Equal((2 * UIMetrics.Cell) + BagPageLayout.CellGap, BagPageLayout.GridHeight(5));
        Assert.Equal(BagPageLayout.GridWidth + (2 * BagPageLayout.FramePadding), BagPageLayout.FrameWidth);
    }

    [Fact]
    public void 配置里每一级的背包整页都放得进手环的内容区()
    {
        var levels = ShippedConfig().BackpackSlotsByLevel;
        Assert.NotEmpty(levels);

        foreach (var slots in levels)
        {
            Assert.True(BagPageLayout.PageHeight(slots) <= WristbandLayout.ContentHeight,
                $"背包 {slots} 格的那一页要 {BagPageLayout.PageHeight(slots)} 高，手环的内容区只有 {WristbandLayout.ContentHeight}");
        }

        Assert.True(BagPageLayout.FrameWidth <= WristbandLayout.ContentWidth,
            $"背包那一圈厚框要 {BagPageLayout.FrameWidth} 宽，手环的内容区只有 {WristbandLayout.ContentWidth}");
    }
}
