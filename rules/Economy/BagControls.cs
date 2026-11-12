using Tinderhearth.Rules.Progression;

namespace Tinderhearth.Rules.Economy;

/// <summary>背包页上那几个控件此刻的状态（当前排序键、当前筛选条件），以及按下去对背包做什么。</summary>
/// <remarks>
/// 排序、筛选、一键整理、挪格与丢弃都经这一处，于是那几条硬约束只判一次：排序与整理只动网格、不碰随身栏那一排，
/// 被筛掉的那几堆什么都不碰。规则在设计仓 design/背包系统.md 的「分堆的代价由排序、筛选与一键整理来付」一节。
///
/// 状态只在这一趟游戏里，不进存档：进来时排序键是按类别、筛选是全部都看。与随身栏选中位不存同一个理由（设计仓
/// spec/prd-items-in-bag.md 的「非目标」一节）：它们是看东西的方式，跨过一次退出不丢任何东西。
///
/// 页上的格按「先随身栏那一排、再网格从左上角数起」排一个号（<see cref="PlaceOf"/>）。挪格与丢弃碰到被筛掉的那一格当场抛：
/// 界面上被筛掉的格点不到，走到这里就是界面那一侧接错了；被一堆盖着的格算那一堆的。照网格画的那一页归设计仓台账 UI-67。
/// </remarks>
public sealed class BagControls
{
    /// <summary>进来时的排序键。</summary>
    public const BagSortKey DefaultSortKey = BagSortKey.Category;

    /// <summary>当前排序键：上一次按的那个键，一键整理也按它排。</summary>
    public BagSortKey SortKey { get; private set; } = DefaultSortKey;

    /// <summary>当前筛选条件。</summary>
    public BagFilter Filter { get; private set; } = BagFilter.All;

    /// <summary>页上第 <paramref name="index"/> 格（从 0 数）是背包里哪一处：先随身栏那一排，再网格。</summary>
    public static BagPlace PlaceOf(Bag bag, int index)
    {
        ArgumentNullException.ThrowIfNull(bag);
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, bag.CarryCount + bag.SlotCount);
        return index < bag.CarryCount ? BagPlace.Carry(index) : BagPlace.Grid(index - bag.CarryCount);
    }

    /// <summary>按一个键排网格，并把它记成当前排序键。</summary>
    public void SortBy(Bag bag, BagSortKey key)
    {
        ArgumentNullException.ThrowIfNull(bag);
        SortKey = key;
        bag.Sort(key, Filter);
    }

    /// <summary>一键整理网格，按当前排序键排。</summary>
    public void Tidy(Bag bag)
    {
        ArgumentNullException.ThrowIfNull(bag);
        bag.Tidy(SortKey, Filter);
    }

    /// <summary>页上第 <paramref name="index"/> 格在当前筛选下看不看得见。空格永远看得见；被一堆盖着的格跟着那一堆。</summary>
    public bool Shows(Bag bag, int index)
    {
        ArgumentNullException.ThrowIfNull(bag);
        return bag.AnchorOf(PlaceOf(bag, index)) is not { } at || Filter.Shows(bag.StackAt(at)!.Value, bag.Items);
    }

    /// <summary>
    /// 挪一堆：拿起页上第 <paramref name="grabbed"/> 格上的那一堆，放到第 <paramref name="pointed"/> 格，拿起时点的那一格对准它。
    /// 随身栏那一排也挪得进挪得出 —— 玩家就是这样往手上放东西的。
    /// </summary>
    /// <returns>
    /// 挪过去了、合进同标识那一堆了、放不下（压到别的堆、出了网格或那一堆满了，不互换），或者占不止一格的东西要进随身栏；
    /// 后两种什么都没动。规则见 <see cref="Bag.Move(BagPlace, BagPlace)"/>。
    /// </returns>
    public BagMoveResult Move(Bag bag, int grabbed, int pointed)
    {
        RequireShown(bag, grabbed);
        RequireShown(bag, pointed);
        return bag.Move(PlaceOf(bag, grabbed), PlaceOf(bag, pointed));
    }

    /// <summary>丢弃那一整堆。确认在界面那一侧，这里只管确认之后那一下。</summary>
    public BagDiscardResult Discard(Bag bag, int index)
    {
        RequireShown(bag, index);
        return bag.TryDiscardAt(PlaceOf(bag, index));
    }

    /// <summary>按类别筛换到下一档：全部 → 在册的每一类（大类在前、小类紧跟）→ 回到全部。品质那一半不动。</summary>
    public void NextCategory(ItemCatalog items) =>
        Filter = Filter with { CategoryId = Next(CategoryChoices(items), Filter.CategoryId) };

    /// <summary>按品质筛换到下一档：全部 → 在册的每一档（由低到高）→ 回到全部。类别那一半不动。</summary>
    public void NextQuality(ItemCatalog items) =>
        Filter = Filter with { QualityId = Next(QualityChoices(items), Filter.QualityId) };

    /// <summary>按类别筛的那几档，<c>null</c> 是全部，排在最前。</summary>
    public static IReadOnlyList<string?> CategoryChoices(ItemCatalog items) => [null, .. BagOrder.CategoriesInOrder(items)];

    /// <summary>按品质筛的那几档，<c>null</c> 是全部，排在最前。</summary>
    public static IReadOnlyList<string?> QualityChoices(ItemCatalog items)
    {
        ArgumentNullException.ThrowIfNull(items);
        return [null, .. items.Qualities.Select(q => q.Id)];
    }

    /// <summary>下一档；现在那一档不在册（mod 换掉了那一档）就回到全部。</summary>
    private static string? Next(IReadOnlyList<string?> choices, string? current)
    {
        var at = choices.ToList().IndexOf(current);
        return at < 0 ? null : choices[(at + 1) % choices.Count];
    }

    private void RequireShown(Bag bag, int index)
    {
        if (!Shows(bag, index))
        {
            throw new InvalidOperationException(
                $"背包{PlaceOf(bag, index)}被当前筛选筛掉了 —— 界面上它点不到，被筛掉的那几堆什么都不碰");
        }
    }
}
