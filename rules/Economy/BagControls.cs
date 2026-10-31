using Tinderhearth.Rules.Progression;

namespace Tinderhearth.Rules.Economy;

/// <summary>背包页上那几个控件此刻的状态（当前排序键、当前筛选条件），以及按下去对背包做什么。</summary>
/// <remarks>
/// 排序、筛选、一键整理、挪格与丢弃都经这一处，于是那几条硬约束只判一次：开头那几格（随身栏）排序与整理不碰，
/// 被筛掉的那几堆什么都不碰。规则在设计仓 design/背包系统.md 的「分堆的代价由排序、筛选与一键整理来付」一节。
///
/// 状态只在这一趟游戏里，不进存档：进来时排序键是按类别、筛选是全部都看。与随身栏选中位不存同一个理由（设计仓
/// spec/prd-items-in-bag.md 的「非目标」一节）：它们是看东西的方式，跨过一次退出不丢任何东西。按类别当起点，
/// 是因为没挑过键就点整理时，玩家多半要的是同一类的东西摆在一起。
///
/// 挪格与丢弃碰到被筛掉的那一格当场抛：界面上被筛掉的格点不到，走到这里就是界面那一侧接错了，
/// 悄悄照做的话「被筛掉的不动」就破了而没有人知道。
/// </remarks>
public sealed class BagControls
{
    /// <summary>进来时的排序键。</summary>
    public const BagSortKey DefaultSortKey = BagSortKey.Category;

    /// <param name="keepLeading">开头几格排序与整理不碰：随身栏的格数（随身栏就是背包开头那几格）。</param>
    public BagControls(int keepLeading)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(keepLeading);
        KeepLeading = keepLeading;
    }

    /// <summary>开头几格排序与整理不碰。</summary>
    public int KeepLeading { get; }

    /// <summary>当前排序键：上一次按的那个键，一键整理也按它排。</summary>
    public BagSortKey SortKey { get; private set; } = DefaultSortKey;

    /// <summary>当前筛选条件。</summary>
    public BagFilter Filter { get; private set; } = BagFilter.All;

    /// <summary>按一个键排，并把它记成当前排序键。</summary>
    public void SortBy(Bag bag, BagSortKey key)
    {
        ArgumentNullException.ThrowIfNull(bag);
        SortKey = key;
        bag.Sort(key, KeepLeading, Filter);
    }

    /// <summary>一键整理，按当前排序键排。</summary>
    public void Tidy(Bag bag)
    {
        ArgumentNullException.ThrowIfNull(bag);
        bag.Tidy(SortKey, KeepLeading, Filter);
    }

    /// <summary>第 <paramref name="slot"/> 格在当前筛选下看不看得见。空格永远看得见。</summary>
    public bool Shows(Bag bag, int slot)
    {
        ArgumentNullException.ThrowIfNull(bag);
        ArgumentOutOfRangeException.ThrowIfNegative(slot);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(slot, bag.SlotCount);
        return bag.Slots[slot] is not { } stack || Filter.Shows(stack, bag.Items);
    }

    /// <summary>挪格：两格互换，目标格空着时就是搬过去。开头那几格也挪得进挪得出 —— 玩家就是这样往手上放东西的。</summary>
    public void Move(Bag bag, int from, int to)
    {
        RequireShown(bag, from);
        RequireShown(bag, to);
        bag.Move(from, to);
    }

    /// <summary>丢弃那一整堆。确认在界面那一侧，这里只管确认之后那一下。</summary>
    public BagDiscardResult Discard(Bag bag, int slot)
    {
        RequireShown(bag, slot);
        return bag.TryDiscardAt(slot);
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

    private void RequireShown(Bag bag, int slot)
    {
        if (!Shows(bag, slot))
        {
            throw new InvalidOperationException(
                $"背包第 {slot} 格（从 0 数）被当前筛选筛掉了 —— 界面上它点不到，被筛掉的那几堆什么都不碰");
        }
    }
}
