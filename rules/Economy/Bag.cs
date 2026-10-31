using Tinderhearth.Rules.Foundation.Config;
using Tinderhearth.Rules.Progression;

namespace Tinderhearth.Rules.Economy;

/// <summary>背包里的一堆：同一种物品、同一个品质，件数不超过那种物品的堆上限。</summary>
/// <param name="ItemId">哪一种物品。</param>
/// <param name="QualityId">哪一档品质；那种物品声明了无品质时是 <c>null</c>，只按种类分堆。</param>
/// <param name="Count">几件，至少 1：一件都没有的格是空格，不是一堆零件。</param>
/// <param name="AcquiredSequence">
/// 这一堆最近一次进东西是背包第几次进东西，越大越近。按「最近获得」排序读它（设计仓 design/背包系统.md 的
/// 「分堆的代价由排序、筛选与一键整理来付」一节）。
/// </param>
public readonly record struct ItemStack(string ItemId, string? QualityId, int Count, long AcquiredSequence)
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
/// </remarks>
public sealed class Bag
{
    /// <summary>开新档时背包是第几级。升级跟内容与经济那一侧走（设计仓台账 GP-23）。</summary>
    public const int StartingLevel = 1;

    private readonly ItemStack?[] _slots;
    private readonly ItemCatalog _items;
    private long _lastSequence;

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
        _items = items;
    }

    /// <summary>开新档时的背包：第一级的格数，一件东西都没有。</summary>
    public static Bag NewGame(GameConfig config, ItemCatalog items)
    {
        ArgumentNullException.ThrowIfNull(config);
        return new Bag(config.BackpackSlotsAt(StartingLevel), items);
    }

    /// <summary>几格。</summary>
    public int SlotCount => _slots.Length;

    /// <summary>每一格装的那一堆，按格号排；<c>null</c> 是空格。</summary>
    public IReadOnlyList<ItemStack?> Slots => _slots;

    /// <summary>还剩几个空格。</summary>
    public int EmptySlotCount => _slots.Count(s => s is null);

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

    /// <summary>算出每一格各放几件：先按格号合进没满的同标识堆，再按格号占空格。放不下就是 <c>null</c>。</summary>
    private List<(int Slot, int Take)>? Plan(string itemId, string? qualityId, int count)
    {
        var limit = Check(itemId, qualityId, count);
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

    /// <summary>核调用方给的这一份：物品在册、件数为正、品质那一位与物品的声明对得上。返回那种物品的堆上限。</summary>
    private int Check(string itemId, string? qualityId, int count)
    {
        if (count < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(count), $"一次至少放 1 件，实际 {count}");
        }

        if (!_items.Items.TryGetValue(itemId, out var item))
        {
            throw new ArgumentException($"物品「{itemId}」不在册，放不进背包", nameof(itemId));
        }

        if (item.HasQuality && qualityId is null)
        {
            throw new ArgumentException($"物品「{itemId}」声明了有品质，放进背包时要带一个品质", nameof(qualityId));
        }

        if (!item.HasQuality && qualityId is not null)
        {
            throw new ArgumentException($"物品「{itemId}」声明了无品质，不能带品质「{qualityId}」", nameof(qualityId));
        }

        if (qualityId is not null && !_items.Qualities.Any(q => q.Id == qualityId))
        {
            throw new ArgumentException($"品质「{qualityId}」不在册", nameof(qualityId));
        }

        return item.StackLimit;
    }
}
