namespace Tinderhearth.Rules.UI;

/// <summary>随身栏接上背包之前装什么：已载入的作物按标识排好，每批装满一条随身栏。</summary>
/// <remarks>
/// 这是脚手架，随身栏换成背包的投影之后整个删掉，归设计仓台账 UI-35。按标识排是为了每次运行结果一样：
/// 字典的枚举顺序不保证稳定，同一份内容在两次运行里落在不同格子上，这种不稳定查起来很贵。
///
/// 作物比格数多时分成几批，开发期换批键挨批往下换，最后一批之后回到第一批；不足一批的那几格留空，
/// 空格本身是合法状态，不补假数据。
/// </remarks>
public static class SeedBatches
{
    /// <summary>一共几批。一种都没有时也算一批，那一批全空。</summary>
    public static int CountFor(int seedCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(seedCount);
        return Math.Max(1, (seedCount + CarrySlotBar.SlotCount - 1) / CarrySlotBar.SlotCount);
    }

    /// <summary>换批键按一下之后是第几批：往后一批，最后一批之后回到第一批。</summary>
    public static int Next(int batch, int seedCount)
    {
        RequireInRange(batch, seedCount);
        return (batch + 1) % CountFor(seedCount);
    }

    /// <summary>第几批（0 起）装哪几格：按标识排、每格一件，不足一批的补空格。</summary>
    /// <remarks>批号越界当场报错，不静默绕回 —— 绕回由 <see cref="Next"/> 一处负责。</remarks>
    public static IReadOnlyList<CarrySlot> Batch(IEnumerable<string> seedIds, int batch)
    {
        ArgumentNullException.ThrowIfNull(seedIds);

        var sorted = seedIds.Order(StringComparer.Ordinal).ToList();
        RequireInRange(batch, sorted.Count);

        var slots = sorted
            .Skip(batch * CarrySlotBar.SlotCount)
            .Take(CarrySlotBar.SlotCount)
            .Select(id => new CarrySlot(id, 1))
            .ToList();
        while (slots.Count < CarrySlotBar.SlotCount)
        {
            slots.Add(CarrySlot.Empty);
        }

        return slots;
    }

    private static void RequireInRange(int batch, int seedCount)
    {
        var count = CountFor(seedCount);
        if (batch < 0 || batch >= count)
        {
            throw new ArgumentOutOfRangeException(
                nameof(batch), $"种子一共 {count} 批（从 0 数），实际要第 {batch} 批");
        }
    }
}
