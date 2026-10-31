using Tinderhearth.Rules.Foundation.Text;

namespace Tinderhearth.Rules.Economy;

/// <summary>农场里一个动作被拒时，即时提示那一行写什么：每一种被拒各一条文本键，不合成一句「不能」。</summary>
/// <remarks>
/// 几种被拒的补救办法不一样（播种那三种的分别写在 <see cref="PlantResult"/> 上），合成一句之后玩家不知道该换格子、
/// 换东西还是先锄地。字在代码仓 data/text/ 下的文本表里，这里只有键；有一条测试核工程里那一份一条不缺。
/// </remarks>
public static class FarmNotices
{
    /// <summary>手上那一格是空的。</summary>
    public const string SowNothingSelectedKey = "ui.notice.sow_nothing_selected";

    /// <summary>手上拿着的不是种子。</summary>
    public const string SowNotASeedKey = "ui.notice.sow_not_a_seed";

    /// <summary>这一格不是锄好的空地。</summary>
    /// <remarks>动作按格子状态派发，已锄的格才派到播种，所以游戏里到不了这一条（见 <see cref="Planting.TryPlant"/>）；规则层答得出它，这里也给它一句。</remarks>
    public const string SowCellNotReadyKey = "ui.notice.sow_cell_not_ready";

    /// <summary>收上来的东西背包放不下，那一株留在地里。</summary>
    public const string HarvestBagFullKey = "ui.notice.harvest_bag_full";

    /// <summary>上面那几条键，给核文本表用。</summary>
    public static readonly IReadOnlyList<string> TextKeys =
        [SowNothingSelectedKey, SowNotASeedKey, SowCellNotReadyKey, HarvestBagFullKey];

    /// <summary>播种被拒那一行；播下去了是 <c>null</c>，不出提示。</summary>
    public static string? ForSowing(PlantResult result, TextCatalog text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return result switch
        {
            PlantResult.Planted => null,
            PlantResult.NothingSelected => text[SowNothingSelectedKey],
            PlantResult.NotASeed => text[SowNotASeedKey],
            PlantResult.CellNotReady => text[SowCellNotReadyKey],
            _ => throw new ArgumentOutOfRangeException(nameof(result), result, "播种多了一种结果，这里没有给它一句"),
        };
    }

    /// <summary>收获被拒那一行；收成了、或者这一格本来就不是待收，都不出提示。</summary>
    /// <remarks>不是待收的那一格，动作按格子状态派发、到不了收获，规则层答得出它，但那一下不是被拒。</remarks>
    public static string? ForHarvest(HarvestStatus status, TextCatalog text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return status switch
        {
            HarvestStatus.Stored or HarvestStatus.NotHarvestable => null,
            HarvestStatus.BagFull => text[HarvestBagFullKey],
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, "收获多了一种结果，这里没有给它一句"),
        };
    }
}
