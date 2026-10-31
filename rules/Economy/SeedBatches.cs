using Tinderhearth.Rules.Progression;

namespace Tinderhearth.Rules.Economy;

/// <summary>开发期调试键往背包里放的那一批种子：种子按标识排，一批几种由调用方给，每种一满堆。</summary>
/// <remarks>
/// 这是开发期的脚手架：还没有商店，种子由调试键给。能买种子之后它跟着那个调试键一起删。
/// 设计仓台账 GP-43
///
/// 按标识排是为了每次运行结果一样：字典的枚举顺序不保证稳定，同一份内容在两次运行里落在不同格子上，这种不稳定查起来很贵。
/// 种子比一批多时分成几批，调试键挨批往下给，最后一批之后回到第一批；一批几种取随身栏几格，由调用方给 ——
/// 经营这一侧不依赖界面那一侧。放不放得下由背包判，整批放不下就一件不放（<see cref="Bag.TryAddAll"/>）。
/// </remarks>
public static class SeedBatches
{
    /// <summary>一共几批。一种种子都没有时也算一批，那一批是空的。</summary>
    public static int CountFor(int seedKinds, int kindsPerBatch)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(seedKinds);
        ArgumentOutOfRangeException.ThrowIfLessThan(kindsPerBatch, 1);

        return Math.Max(1, (seedKinds + kindsPerBatch - 1) / kindsPerBatch);
    }

    /// <summary>给完这一批之后下一回是第几批：往后一批，最后一批之后回到第一批。</summary>
    public static int Next(int batch, int seedKinds, int kindsPerBatch)
    {
        RequireInRange(batch, seedKinds, kindsPerBatch);
        return (batch + 1) % CountFor(seedKinds, kindsPerBatch);
    }

    /// <summary>第几批（从 0 数）里是哪几样种子、各几件：种子按标识排，每样一满堆（那一样的堆上限）。</summary>
    /// <param name="crops">在册的作物；一种作物一份种子。</param>
    /// <param name="batch">第几批。越界当场抛，不静默绕回 —— 绕回由 <see cref="Next"/> 一处负责。</param>
    /// <param name="kindsPerBatch">一批几种。</param>
    /// <param name="items">在册物品：每样种子的堆上限读这里。</param>
    public static IReadOnlyList<(string ItemId, string? QualityId, int Count)> Batch(
        IEnumerable<CropDefinition> crops, int batch, int kindsPerBatch, ItemCatalog items)
    {
        ArgumentNullException.ThrowIfNull(crops);
        ArgumentNullException.ThrowIfNull(items);

        var seeds = crops.Select(c => c.SeedItemId).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        RequireInRange(batch, seeds.Count, kindsPerBatch);

        return
        [
            .. seeds
                .Skip(batch * kindsPerBatch)
                .Take(kindsPerBatch)
                .Select(id => (id, (string?)null, items.Items[id].StackLimit)),
        ];
    }

    private static void RequireInRange(int batch, int seedKinds, int kindsPerBatch)
    {
        var count = CountFor(seedKinds, kindsPerBatch);
        if (batch < 0 || batch >= count)
        {
            throw new ArgumentOutOfRangeException(
                nameof(batch), $"种子一共 {count} 批（从 0 数），实际要第 {batch} 批");
        }
    }
}
