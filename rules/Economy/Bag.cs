using Tinderhearth.Rules.Foundation.Config;
using Tinderhearth.Rules.Progression;

namespace Tinderhearth.Rules.Economy;

/// <summary>背包里的一堆：同一种物品、同一个品质，件数不超过那种物品的堆上限。</summary>
/// <param name="ItemId">哪一种物品。</param>
/// <param name="QualityId">哪一档品质；那种物品声明了无品质时是 <c>null</c>，只按种类分堆。</param>
/// <param name="Count">几件，至少 1：一件都没有的格是空格，不是一堆零件。</param>
/// <param name="AcquiredSequence">
/// 这一堆最近一次进东西的先后序号，从 1 数起，越大越近；只拿来比先后，不是一个计数。一次放进去的那一份分进了几格，
/// 那几格是同一个序号。按「最近获得」排序读它（设计仓 design/背包系统.md 的「分堆的代价由排序、筛选与一键整理来付」一节）。
/// </param>
public readonly record struct ItemStack(string ItemId, string? QualityId, int Count, int AcquiredSequence)
{
    /// <summary>这一堆与给定的种类和品质是不是同一个堆标识。</summary>
    public bool Holds(string itemId, string? qualityId) =>
        string.Equals(ItemId, itemId, StringComparison.Ordinal) && string.Equals(QualityId, qualityId, StringComparison.Ordinal);
}

/// <summary>主角的背包：一格一堆，堆的标识是「种类 ＋ 品质」，满了只拒不丢。</summary>
/// <remarks>
/// 规则在设计仓 design/背包系统.md 的「一格一堆，堆的标识是「种类 ＋ 品质」」与「满了怎么办：不自动丢弃」两节，本类不复述理由：
/// 同种同品质先合进没满的那一堆、合满了再占空格；同种不同品质各占一格；声明了无品质的只按种类分。
/// **满载是两个条件都不成立**：没有可合的同标识堆，也没有空格 —— 格子用尽但有半满的同标识堆时照样放得进去。
///
/// **一次放进来的那一份放不下就整份拒收，背包一件不变。** 先算出每一格各放几件，全放得下才动手；放不下的那一份
/// 留在它原来那里（地里那一株、世界里那一件），由调用方处理，背包不替谁丢东西。
///
/// 格数由调用方给（开新档时按背包等级从配置读，<see cref="NewGame"/>），堆上限按种类读物品定义。
/// 物品不在册、品质那一位与物品的声明对不上，是调用方的错，当场抛 —— 不当成「放不下」。
///
/// 它是世界状态的一部分，跟着结算的那份副本走，所以 <see cref="Clone"/> 出来的与原件不共用任何会变的东西。
/// </remarks>
public sealed class Bag
{
    /// <summary>开新档时背包是第几级。升级跟内容与经济那一侧走（设计仓台账 GP-23）。</summary>
    public const int StartingLevel = 1;

    private readonly ItemStack?[] _slots;

    /// <summary>最近一次进东西用掉的那个序号；一次都没进过是 0。</summary>
    /// <remarks>
    /// 它只拿来让新进的东西排在已有的后面，所以不进存档：读回来时取各堆里最大的那个，排出来的先后一样
    /// （设计仓 design/存档系统.md 的「派生量不单独存」一节）。
    /// </remarks>
    private int _lastSequence;

    /// <param name="slotCount">几格，至少 1。</param>
    /// <param name="items">在册物品：堆上限与有无品质读这里。</param>
    public Bag(int slotCount, ItemCatalog items)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (slotCount < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(slotCount), $"背包至少要有 1 格，实际 {slotCount}");
        }

        _slots = new ItemStack?[slotCount];
        Items = items;
    }

    /// <summary>开新档时的背包：第一级的格数，一件东西都没有。</summary>
    public static Bag NewGame(GameConfig config, ItemCatalog items)
    {
        ArgumentNullException.ThrowIfNull(config);
        return new Bag(config.BackpackSlotsAt(StartingLevel), items);
    }

    /// <summary>
    /// 从存档里读回来的那几格建一份背包。哪一格对不上 —— 物品不在册、品质那一位与物品的声明不符、件数不在 1 到堆上限之间、
    /// 序号不为正 —— 一次全报，一行一处，格号从 1 数起。之后新进的东西排在各堆里最大的那个序号后面。
    /// </summary>
    /// <param name="slots">每一格，按格号排；<c>null</c> 是空格。项数就是格数。</param>
    /// <param name="items">在册物品。</param>
    public static Bag Restore(IReadOnlyList<ItemStack?> slots, ItemCatalog items)
    {
        ArgumentNullException.ThrowIfNull(slots);
        ArgumentNullException.ThrowIfNull(items);

        var problems = new List<string>();
        for (var i = 0; i < slots.Count; i++)
        {
            if (slots[i] is not { } stack)
            {
                continue;
            }

            if (ProblemOf(stack.ItemId, stack.QualityId, stack.Count, items) is string problem)
            {
                problems.Add($"第 {i + 1} 格：{problem}");
            }
            else if (stack.Count > items.Items[stack.ItemId].StackLimit)
            {
                problems.Add($"第 {i + 1} 格：「{stack.ItemId}」有 {stack.Count} 件，超过它的堆上限 {items.Items[stack.ItemId].StackLimit}");
            }

            if (stack.AcquiredSequence < 1)
            {
                problems.Add($"第 {i + 1} 格：序号 {stack.AcquiredSequence} 不为正");
            }
        }

        if (problems.Count > 0)
        {
            throw new InvalidDataException($"背包有 {problems.Count} 处对不上：\n  " + string.Join("\n  ", problems));
        }

        var bag = new Bag(slots.Count, items);
        for (var i = 0; i < slots.Count; i++)
        {
            bag._slots[i] = slots[i];
            bag._lastSequence = Math.Max(bag._lastSequence, slots[i]?.AcquiredSequence ?? 0);
        }

        return bag;
    }

    /// <summary>在册物品：堆上限与有无品质读这里。</summary>
    public ItemCatalog Items { get; }

    /// <summary>几格。</summary>
    public int SlotCount => _slots.Length;

    /// <summary>每一格装的那一堆，按格号排；<c>null</c> 是空格。</summary>
    public IReadOnlyList<ItemStack?> Slots => _slots;

    /// <summary>还剩几个空格。</summary>
    public int EmptySlotCount => _slots.Count(s => s is null);

    /// <summary>整份复制：副本与原件不共用任何会变的东西。</summary>
    public Bag Clone()
    {
        var copy = new Bag(SlotCount, Items) { _lastSequence = _lastSequence };
        _slots.CopyTo(copy._slots, 0);
        return copy;
    }

    /// <summary>这一份放不放得下：先合进没满的同标识堆，再占空格，全放得下才算。不改背包。</summary>
    public bool CanAdd(string itemId, string? qualityId, int count) => Plan(itemId, qualityId, count) is not null;

    /// <summary>这一种这一品质是不是满载：没有可合的同标识堆，也没有空格。</summary>
    public bool IsFullFor(string itemId, string? qualityId) => !CanAdd(itemId, qualityId, 1);

    /// <summary>放进一份。放得下就全放进去、返回 <c>true</c>；放不下就一件都不放、返回 <c>false</c>，背包一件不变。</summary>
    public bool TryAdd(string itemId, string? qualityId, int count)
    {
        var plan = Plan(itemId, qualityId, count);
        if (plan is null)
        {
            return false;
        }

        var stamp = ++_lastSequence;
        foreach (var (slot, take) in plan)
        {
            var before = _slots[slot]?.Count ?? 0;
            _slots[slot] = new ItemStack(itemId, qualityId, before + take, stamp);
        }

        return true;
    }

    /// <summary>一次放进好几份：全放得下才动手，有一份放不下就一件都不放，背包一件不变。</summary>
    /// <remarks>
    /// 按给的先后一份一份放，与一份一份调 <see cref="TryAdd"/> 放出来的摆法一样；先在一份副本上放一遍，全成了才换上去。
    /// 调用方给错的（物品不在册、品质那一位对不上、件数不为正）照样当场抛，不当成放不下。
    /// </remarks>
    public bool TryAddAll(IReadOnlyList<(string ItemId, string? QualityId, int Count)> parts)
    {
        ArgumentNullException.ThrowIfNull(parts);

        var trial = Clone();
        foreach (var (itemId, qualityId, count) in parts)
        {
            if (!trial.TryAdd(itemId, qualityId, count))
            {
                return false;
            }
        }

        trial._slots.CopyTo(_slots, 0);
        _lastSequence = trial._lastSequence;
        return true;
    }

    /// <summary>从第 <paramref name="slot"/> 格（从 0 数）拿走几件。拿完那一格变成空格。</summary>
    /// <returns>那一格是空的、或者件数不够，就一件都不拿、返回 <c>false</c>。</returns>
    /// <remarks>格号越界、件数不为正是调用方的错，当场抛。拿走不改那一堆的先后序号：「最近获得」问的是什么时候进来的。</remarks>
    public bool TryRemoveAt(int slot, int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(slot);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(slot, _slots.Length);
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);

        if (_slots[slot] is not { } stack || stack.Count < count)
        {
            return false;
        }

        _slots[slot] = stack.Count == count ? null : stack with { Count = stack.Count - count };
        return true;
    }

    /// <summary>挪格：第 <paramref name="from"/> 格与第 <paramref name="to"/> 格（都从 0 数）互换，目标格空着时就是搬过去。</summary>
    /// <remarks>
    /// 同标识的两堆挪到一起也只是互换，不合堆：合堆是一键整理的事，挪格只动摆法。格号越界是调用方的错，当场抛。
    /// 挪格不改哪一堆的先后序号：「最近获得」问的是什么时候进来的，不是什么时候挪过。
    /// </remarks>
    public void Move(int from, int to)
    {
        RequireSlot(from, nameof(from));
        RequireSlot(to, nameof(to));
        (_slots[from], _slots[to]) = (_slots[to], _slots[from]);
    }

    /// <summary>丢弃第 <paramref name="slot"/> 格（从 0 数）那一整堆。</summary>
    /// <remarks>
    /// 那一样声明了不可丢弃时一件都不丢，说得出是这一种（物品定义那一格，设计仓 design/物品系统.md）。丢之前那一道确认
    /// 在界面那一侧，这里只管确认之后那一下。格号越界是调用方的错，当场抛。
    /// </remarks>
    public BagDiscardResult TryDiscardAt(int slot)
    {
        RequireSlot(slot, nameof(slot));

        if (_slots[slot] is not { } stack)
        {
            return BagDiscardResult.NothingThere;
        }

        if (!Items.Items[stack.ItemId].Discardable)
        {
            return BagDiscardResult.NotDiscardable;
        }

        _slots[slot] = null;
        return BagDiscardResult.Discarded;
    }

    /// <summary>
    /// 按一个键排一遍。只动前 <paramref name="keepLeading"/> 格之后、筛选看得见的那些格（空格也算看得见），
    /// 看得见的那几堆从那些格里最前面的一格起挨个摆，剩下的变空。
    /// </summary>
    /// <param name="key">按什么排。</param>
    /// <param name="keepLeading">开头几格一格都不动：随身栏就是背包开头那几格，玩家手上那一格不许被排序换掉。</param>
    /// <param name="filter">筛选条件。被筛掉的那几堆留在原来那几格，排序越过它们。</param>
    /// <remarks>
    /// 开头那几格为什么不动：随身栏按格号读背包开头那几格，排一次就换掉玩家手上那一格，而他没按任何键（设计仓
    /// design/背包系统.md 的「分堆的代价由排序、筛选与一键整理来付」一节）。不筛的时候它把东西都挪到前面、空格留在最后。
    /// </remarks>
    public void Sort(BagSortKey key, int keepLeading, BagFilter filter) => Arrange(key, keepLeading, filter, merge: false);

    /// <summary>
    /// 一键整理：把同标识的散堆并起来，再按 <paramref name="key"/> 排。动哪几格与 <see cref="Sort"/> 一样。
    /// </summary>
    /// <remarks>
    /// 散堆是没装满的那几堆：同一种同一品质的散堆并成满堆加至多一个零头，装满的那几堆不动它们的件数。
    /// 总数一件不变、一件都不丢、不跨容器 —— 它只动摆法。整理两次与整理一次一样。并出来的那几堆记的是参与合并的
    /// 那几堆里最近的那个序号：「最近获得」问的是这一堆里最近进来的那一件。
    /// </remarks>
    public void Tidy(BagSortKey key, int keepLeading, BagFilter filter) => Arrange(key, keepLeading, filter, merge: true);

    private void Arrange(BagSortKey key, int keepLeading, BagFilter filter, bool merge)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentOutOfRangeException.ThrowIfNegative(keepLeading);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(keepLeading, _slots.Length);

        var places = Enumerable.Range(keepLeading, _slots.Length - keepLeading)
            .Where(i => _slots[i] is not { } stack || filter.Shows(stack, Items))
            .ToList();
        var stacks = places.Where(i => _slots[i] is not null).Select(i => _slots[i]!.Value).ToList();
        if (merge)
        {
            stacks = Merge(stacks);
        }

        stacks.Sort(BagOrder.For(key, Items));
        for (var n = 0; n < places.Count; n++)
        {
            _slots[places[n]] = n < stacks.Count ? stacks[n] : null;
        }
    }

    /// <summary>同一种同一品质的散堆并成满堆加至多一个零头；装满的那几堆原样留着。</summary>
    private List<ItemStack> Merge(List<ItemStack> stacks)
    {
        var merged = new List<ItemStack>();
        foreach (var group in stacks.GroupBy(s => (s.ItemId, s.QualityId)))
        {
            var limit = Items.Items[group.Key.ItemId].StackLimit;
            merged.AddRange(group.Where(s => s.Count >= limit));

            var scattered = group.Where(s => s.Count < limit).ToList();
            if (scattered.Count == 0)
            {
                continue;
            }

            var latest = scattered.Max(s => s.AcquiredSequence);
            for (var left = scattered.Sum(s => s.Count); left > 0; left -= limit)
            {
                merged.Add(new ItemStack(group.Key.ItemId, group.Key.QualityId, Math.Min(limit, left), latest));
            }
        }

        return merged;
    }

    private void RequireSlot(int slot, string name)
    {
        if (slot < 0 || slot >= _slots.Length)
        {
            throw new ArgumentOutOfRangeException(name, $"背包只有 {_slots.Length} 格（从 0 数），没有第 {slot} 格");
        }
    }

    /// <summary>算出每一格各放几件：先按格号合进没满的同标识堆，再按格号占空格。放不下就是 <c>null</c>。</summary>
    private List<(int Slot, int Take)>? Plan(string itemId, string? qualityId, int count)
    {
        if (ProblemOf(itemId, qualityId, count, Items) is string problem)
        {
            throw count < 1 ? new ArgumentOutOfRangeException(nameof(count), problem) : new ArgumentException(problem);
        }

        var limit = Items.Items[itemId].StackLimit;
        var plan = new List<(int Slot, int Take)>();
        var remaining = count;

        for (var i = 0; i < _slots.Length && remaining > 0; i++)
        {
            if (_slots[i] is { } stack && stack.Holds(itemId, qualityId) && stack.Count < limit)
            {
                var take = Math.Min(limit - stack.Count, remaining);
                plan.Add((i, take));
                remaining -= take;
            }
        }

        for (var i = 0; i < _slots.Length && remaining > 0; i++)
        {
            if (_slots[i] is null)
            {
                var take = Math.Min(limit, remaining);
                plan.Add((i, take));
                remaining -= take;
            }
        }

        return remaining == 0 ? plan : null;
    }

    /// <summary>这一份对不对：件数为正、物品在册、品质那一位与物品的声明对得上。对就是 <c>null</c>。</summary>
    private static string? ProblemOf(string itemId, string? qualityId, int count, ItemCatalog items)
    {
        if (count < 1)
        {
            return $"件数至少 1，实际 {count}";
        }

        if (!items.Items.TryGetValue(itemId, out var item))
        {
            return $"物品「{itemId}」不在册";
        }

        if (item.HasQuality && qualityId is null)
        {
            return $"物品「{itemId}」声明了有品质，却没带品质";
        }

        if (!item.HasQuality && qualityId is not null)
        {
            return $"物品「{itemId}」声明了无品质，却带了品质「{qualityId}」";
        }

        return qualityId is not null && !items.Qualities.Any(q => q.Id == qualityId)
            ? $"品质「{qualityId}」不在册"
            : null;
    }
}
