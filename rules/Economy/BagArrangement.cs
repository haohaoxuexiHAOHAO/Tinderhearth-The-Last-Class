using Tinderhearth.Rules.Progression;

namespace Tinderhearth.Rules.Economy;

/// <summary>背包按什么排。有哪几个键的家在设计仓 design/背包系统.md 的「分堆的代价由排序、筛选与一键整理来付」一节。</summary>
public enum BagSortKey
{
    /// <summary>按品质：好的在前；没有品质这条轴的东西排在有品质的后面。</summary>
    Quality,

    /// <summary>按类别：同一类的挨在一起，一个大类下面的几个小类也挨在一起。</summary>
    Category,

    /// <summary>按最近获得：最近进来的在前。</summary>
    Recent,
}

/// <summary>丢一堆的结果。</summary>
public enum BagDiscardResult
{
    /// <summary>整堆丢掉了，那一格变空。</summary>
    Discarded,

    /// <summary>那一格是空的，没东西可丢。</summary>
    NothingThere,

    /// <summary>那一样声明了不可丢弃（任务物品这一类），一件都没丢。</summary>
    NotDiscardable,
}

/// <summary>筛选条件：只看哪一类、哪一档品质。两样都空着就是全部都看。</summary>
/// <param name="CategoryId">只看这一类，连同它下面的小类；<c>null</c> 是不按类别筛。</param>
/// <param name="QualityId">只看这一档品质；<c>null</c> 是不按品质筛。按品质筛时，没有品质这条轴的东西看不见。</param>
/// <remarks>
/// 筛选只决定看不看得见，不挪任何东西：被筛掉的那几堆待在原来那几格，筛选态下的排序与整理也不碰它们
/// （<see cref="Bag.Sort"/>）。空格永远看得见，它是挪格的落点。
/// </remarks>
public sealed record BagFilter(string? CategoryId, string? QualityId)
{
    /// <summary>不筛：全部都看。</summary>
    public static readonly BagFilter All = new(null, null);

    /// <summary>这一堆看不看得见。</summary>
    public bool Shows(ItemStack stack, ItemCatalog items)
    {
        ArgumentNullException.ThrowIfNull(items);

        if (QualityId is not null && !string.Equals(stack.QualityId, QualityId, StringComparison.Ordinal))
        {
            return false;
        }

        return CategoryId is null || IsWithin(items.Items[stack.ItemId].CategoryId, CategoryId, items);
    }

    /// <summary>这一类是不是要的那一类、或者在它下面。类别的层级读进来时核过不成圈，所以往上走一定走得到头。</summary>
    private static bool IsWithin(string category, string wanted, ItemCatalog items)
    {
        for (string? at = category; at is not null; at = items.Categories[at].ParentId)
        {
            if (string.Equals(at, wanted, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>排序的先后：每个键先比它自己那一项，比不出来再依次比其余几项，于是同一批东西不管原来怎么摆都排成同一个样子。</summary>
/// <remarks>
/// 排出来的顺序只由每一堆自己的内容定，不读它原来在第几格：同键排两次结果相同，整理两次与整理一次一样。
/// 品质与类别按在册的先后比，不按标识的字母：品质那一轴由低到高排好了，类别按从大类往下走的那一串逐段比，
/// 于是同一个大类下面的小类挨在一起。
/// </remarks>
internal static class BagOrder
{
    /// <summary>某个键的比较。</summary>
    public static Comparison<ItemStack> For(BagSortKey key, ItemCatalog items)
    {
        ArgumentNullException.ThrowIfNull(items);

        var categoryRank = CategoriesInOrder(items)
            .Select((id, rank) => (id, rank))
            .ToDictionary(e => e.id, e => e.rank, StringComparer.Ordinal);
        var qualityRank = items.Qualities
            .Select((tier, rank) => (tier.Id, rank))
            .ToDictionary(e => e.Id, e => e.rank, StringComparer.Ordinal);

        // 没有品质这条轴的算 -1：按品质从高到低排时自然落在最后。
        int Quality(ItemStack s) => s.QualityId is { } quality ? qualityRank[quality] : -1;
        int Category(ItemStack s) => categoryRank[items.Items[s.ItemId].CategoryId];

        return key switch
        {
            BagSortKey.Quality => (a, b) => First(
                Quality(b).CompareTo(Quality(a)),
                Category(a).CompareTo(Category(b)),
                string.CompareOrdinal(a.ItemId, b.ItemId),
                b.Count.CompareTo(a.Count),
                b.AcquiredSequence.CompareTo(a.AcquiredSequence)),
            BagSortKey.Category => (a, b) => First(
                Category(a).CompareTo(Category(b)),
                string.CompareOrdinal(a.ItemId, b.ItemId),
                Quality(b).CompareTo(Quality(a)),
                b.Count.CompareTo(a.Count),
                b.AcquiredSequence.CompareTo(a.AcquiredSequence)),
            BagSortKey.Recent => (a, b) => First(
                b.AcquiredSequence.CompareTo(a.AcquiredSequence),
                Category(a).CompareTo(Category(b)),
                string.CompareOrdinal(a.ItemId, b.ItemId),
                Quality(b).CompareTo(Quality(a)),
                b.Count.CompareTo(a.Count)),
            _ => throw new ArgumentOutOfRangeException(nameof(key), $"没有这个排序键：{key}"),
        };
    }

    /// <summary>
    /// 在册的类别按从大类往下走的那一串逐段比出来的先后：大类在前，它下面的小类紧跟着它，再到下一个大类。
    /// </summary>
    public static IReadOnlyList<string> CategoriesInOrder(ItemCatalog items)
    {
        ArgumentNullException.ThrowIfNull(items);

        List<string> PathOf(string id)
        {
            var path = new List<string>();
            for (string? at = id; at is not null; at = items.Categories[at].ParentId)
            {
                path.Insert(0, at);
            }

            return path;
        }

        var paths = items.Categories.Keys.ToDictionary(id => id, PathOf, StringComparer.Ordinal);
        return [.. items.Categories.Keys.Order(Comparer<string>.Create((a, b) => ComparePaths(paths[a], paths[b])))];
    }

    /// <summary>两串逐段比；一串是另一串的开头时，短的在前（大类排在它的小类前面）。</summary>
    private static int ComparePaths(IReadOnlyList<string> a, IReadOnlyList<string> b)
    {
        for (var i = 0; i < Math.Min(a.Count, b.Count); i++)
        {
            var step = string.CompareOrdinal(a[i], b[i]);
            if (step != 0)
            {
                return step;
            }
        }

        return a.Count.CompareTo(b.Count);
    }

    /// <summary>第一个比得出先后的那一项。</summary>
    private static int First(params int[] results) => results.FirstOrDefault(r => r != 0);
}
