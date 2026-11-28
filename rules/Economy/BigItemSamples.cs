using Tinderhearth.Rules.Progression;

namespace Tinderhearth.Rules.Economy;

/// <summary>开发期调试键往背包里放的大东西：在册物品里占不止一格的那几样，按标识排，一次一件，最后一件之后回到第一件。</summary>
/// <remarks>
/// 这是开发期的脚手架：还没有别的路子拿到大东西，作者要在背包页里看大图、试挪大块，就由这个键给。有了正式的来路，
/// 它跟着那个调试键一起删。不写死是哪几样：占不止一格的都算，以后加了别的大东西，这个键照样轮得到它。
/// 按标识排的理由同 <see cref="SeedBatches"/>。放不放得下由背包判（<see cref="Bag.TryAdd"/>）。
/// </remarks>
public static class BigItemSamples
{
    /// <summary>占不止一格的那几样，按标识排。一样都没有时是空的。</summary>
    public static IReadOnlyList<string> Kinds(ItemCatalog items)
    {
        ArgumentNullException.ThrowIfNull(items);

        return
        [
            .. items.Items.Values
                .Where(item => item.Footprint.Width * item.Footprint.Height > 1)
                .Select(item => item.Id)
                .Order(StringComparer.Ordinal),
        ];
    }

    /// <summary>
    /// 第几件（从 0 数）是哪一样、哪一档品质、几件：一次一件；有品质的给寻常那一档，与没变异的收成同一档。
    /// </summary>
    /// <param name="items">在册物品。</param>
    /// <param name="index">第几件。越界当场抛，不静默绕回 —— 绕回由 <see cref="Next"/> 一处负责。</param>
    public static (string ItemId, string? QualityId, int Count) Sample(ItemCatalog items, int index)
    {
        var kinds = Kinds(items);
        if (index < 0 || index >= kinds.Count)
        {
            throw new ArgumentOutOfRangeException(
                nameof(index), $"占不止一格的东西一共 {kinds.Count} 样（从 0 数），实际要第 {index} 样");
        }

        var item = items.Items[kinds[index]];
        return (item.Id, item.HasQuality ? Harvesting.UnmutatedQualityId : null, 1);
    }

    /// <summary>给完第几件之后下一回给第几件：往后一件，最后一件之后回到第一件。</summary>
    /// <param name="index">刚给完的是第几件。</param>
    /// <param name="kinds">一共几样，至少 1。</param>
    public static int Next(int index, int kinds)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(kinds, 1);
        if (index < 0 || index >= kinds)
        {
            throw new ArgumentOutOfRangeException(nameof(index), $"一共 {kinds} 样（从 0 数），实际是第 {index} 样");
        }

        return (index + 1) % kinds;
    }
}
