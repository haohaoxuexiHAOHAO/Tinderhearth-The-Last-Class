using Tinderhearth.Rules.Foundation.Config;
using Tinderhearth.Rules.Progression;

namespace Tinderhearth.Rules.Economy;

/// <summary>背包里的一堆：同一种物品、同一个品质，件数不超过那种物品的堆上限。</summary>
/// <param name="ItemId">哪一种物品。</param>
/// <param name="QualityId">哪一档品质；那种物品声明了无品质时是 <c>null</c>，只按种类分堆。</param>
/// <param name="Count">几件，至少 1：一件都没有的格是空格，不是一堆零件。</param>
/// <param name="AcquiredSequence">
/// 这一堆最近一次进东西的先后序号，从 1 数起，越大越近；只拿来比先后，不是一个计数。一次放进去的那一份分成了几堆，
/// 那几堆是同一个序号。按「最近获得」排序读它（设计仓 design/背包系统.md 的「分堆的代价由排序、筛选与一键整理来付」一节）。
/// </param>
public readonly record struct ItemStack(string ItemId, string? QualityId, int Count, int AcquiredSequence)
{
    /// <summary>这一堆与给定的种类和品质是不是同一个堆标识。</summary>
    public bool Holds(string itemId, string? qualityId) =>
        string.Equals(ItemId, itemId, StringComparison.Ordinal) && string.Equals(QualityId, qualityId, StringComparison.Ordinal);
}

/// <summary>一份东西放不进背包的原因。两种说法分得开，玩家才知道该腾东西还是挪一挪。</summary>
public enum BagRefusal
{
    /// <summary>一格空的都没有：背包满了，得先腾出东西。</summary>
    Full,

    /// <summary>有空格，却凑不出一块放得下它整块占地的空地：挪一挪就放得下。</summary>
    NoRoomForSize,
}

/// <summary>背包里的一个位置：随身栏那一排的第几格，或者网格里的第几格，都从 0 数。</summary>
/// <param name="InCarry">在不在随身栏那一排。</param>
/// <param name="Index">随身栏那一排的第几格，或者网格里从左上角数起、一行一行往下的第几格。</param>
public readonly record struct BagPlace(bool InCarry, int Index)
{
    /// <summary>随身栏那一排的第几格（从 0 数）。</summary>
    public static BagPlace Carry(int index) => new(InCarry: true, index);

    /// <summary>网格里的第几格（从 0 数）。</summary>
    public static BagPlace Grid(int cell) => new(InCarry: false, cell);

    /// <summary>写成「随身栏第几格」或「网格第几格」，从 1 数。</summary>
    public override string ToString() => InCarry ? $"随身栏第 {Index + 1} 格" : $"网格第 {Index + 1} 格";
}

/// <summary>挪一堆的结果。</summary>
public enum BagMoveResult
{
    /// <summary>挪过去了；目标格有东西就两格互换。</summary>
    Moved,

    /// <summary>占不止一格的东西放不进随身栏那一排，什么都没动。</summary>
    TooBigForCarry,
}

/// <summary>主角的背包：一张网格加上随身栏那一排，一堆按物品的占地占一块，堆的标识是「种类 ＋ 品质」，满了只拒不丢。</summary>
/// <remarks>
/// 规则在设计仓 design/背包系统.md 的「一堆放在哪：先合堆，再找第一块够大的空地，不能旋转」与「随身栏是背包上单独一排，
/// 只放占 1 格的东西」两节：先合进没满的同标识堆（先随身栏、再网格），占 1 格的新东西先找随身栏的空格、再找网格，大的从上到下、
/// 从左到右找第一块放得下它整块占地的空地；一次放进来的那一份放不下就整份拒收。东西不能旋转，这里没有任何转向的入口。
///
/// 格号从左上角那一格数起、一行一行往下，一堆记在它左上角那一格（<see cref="Slots"/>）；随身栏那一排另记（<see cref="Carry"/>），
/// 它不属于网格，排序与整理不碰它。网格多大、随身栏几格由调用方给，堆上限与占地读物品定义；给错的那一份当场抛。
///
/// 它是世界状态的一部分，跟着结算的那份副本走，所以 <see cref="Clone"/> 出来的与原件不共用任何会变的东西。
/// </remarks>
public sealed class Bag
{
    /// <summary>开新档时背包是第几级。升级跟内容与经济那一侧走（设计仓台账 GP-23）。</summary>
    public const int StartingLevel = 1;

    /// <summary>没被任何一堆盖着的格，在 <see cref="_coveredBy"/> 里记这个数。</summary>
    private const int Free = -1;

    /// <summary>随身栏那一排每一格的那一堆；空格是 <c>null</c>。那里的东西都只占 1 格。</summary>
    private readonly ItemStack?[] _carry;

    /// <summary>网格每一格记的那一堆：一堆记在它左上角那一格，别的格是 <c>null</c>。</summary>
    private readonly ItemStack?[] _anchors;

    /// <summary>网格每一格被哪一堆盖着：那一堆左上角那一格的格号；空着是 <see cref="Free"/>。</summary>
    private readonly int[] _coveredBy;

    /// <summary>最近一次进东西用掉的那个序号；一次都没进过是 0。</summary>
    /// <remarks>
    /// 它只拿来让新进的东西排在已有的后面，所以不进存档：读回来时取各堆里最大的那个，排出来的先后一样
    /// （设计仓 design/存档系统.md 的「派生量不单独存」一节）。
    /// </remarks>
    private int _lastSequence;

    /// <summary>没有随身栏那一排的背包。</summary>
    /// <param name="width">网格几格宽，至少 1。</param>
    /// <param name="height">网格几格高，至少 1。</param>
    /// <param name="items">在册物品：堆上限、占地与有无品质读这里。</param>
    public Bag(int width, int height, ItemCatalog items)
        : this(width, height, carryCells: 0, items)
    {
    }

    /// <param name="width">网格几格宽，至少 1。</param>
    /// <param name="height">网格几格高，至少 1。</param>
    /// <param name="carryCells">
    /// 随身栏那一排几格，不小于 0。主角的背包与技能位同数，由调用方给：规则层的经营这一侧不依赖界面那一侧。
    /// </param>
    /// <param name="items">在册物品：堆上限、占地与有无品质读这里。</param>
    public Bag(int width, int height, int carryCells, ItemCatalog items)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (width < 1 || height < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(width), $"背包的网格至少 1×1，实际 {width}×{height}");
        }

        ArgumentOutOfRangeException.ThrowIfNegative(carryCells);

        GridWidth = width;
        GridHeight = height;
        Items = items;
        _carry = new ItemStack?[carryCells];
        _anchors = new ItemStack?[width * height];
        _coveredBy = new int[width * height];
        Array.Fill(_coveredBy, Free);
    }

    /// <summary>开新档时的背包：网格第一级那么大，随身栏那一排 <paramref name="carryCells"/> 格，一件东西都没有。</summary>
    public static Bag NewGame(GameConfig config, ItemCatalog items, int carryCells)
    {
        ArgumentNullException.ThrowIfNull(config);
        var grid = config.BackpackGridAt(StartingLevel);
        return new Bag(grid.Width, grid.Height, carryCells, items);
    }

    /// <summary>从存档里读回来的网格建一份没有随身栏那一排的背包，见另一个重载。</summary>
    public static Bag Restore(int width, int height, IReadOnlyList<ItemStack?> anchors, ItemCatalog items) =>
        Restore([], width, height, anchors, items);

    /// <summary>
    /// 从存档里读回来的那几堆建一份背包：随身栏那一排按格排，网格里每一堆记在它左上角那一格。哪一堆对不上 —— 物品不在册、
    /// 品质与物品的声明不符、件数不在 1 到堆上限之间、序号不为正、随身栏里放着占不止一格的东西、那一块出了网格、与别的堆压在一起
    /// —— 一次全报，一行一处：随身栏的格从 1 数，网格里的一堆写成它左上角那一格的 (列, 行)，从 0 数，与存档里记的一样。
    /// 之后新进的东西排在各堆里最大的那个序号后面。
    /// </summary>
    /// <param name="carry">随身栏那一排，按格排；<c>null</c> 是空格。项数就是随身栏几格。</param>
    /// <param name="width">网格几格宽。</param>
    /// <param name="height">网格几格高。</param>
    /// <param name="anchors">网格每一格记的那一堆，按格号排；<c>null</c> 是这一格不是哪一堆的左上角。项数必须是宽乘高。</param>
    /// <param name="items">在册物品。</param>
    public static Bag Restore(
        IReadOnlyList<ItemStack?> carry, int width, int height, IReadOnlyList<ItemStack?> anchors, ItemCatalog items)
    {
        ArgumentNullException.ThrowIfNull(carry);
        ArgumentNullException.ThrowIfNull(anchors);
        var bag = new Bag(width, height, carry.Count, items);
        if (anchors.Count != bag.SlotCount)
        {
            throw new ArgumentException($"{width}×{height} 的网格有 {bag.SlotCount} 格，给的却是 {anchors.Count} 格", nameof(anchors));
        }

        var problems = new List<string>();
        for (var i = 0; i < carry.Count; i++)
        {
            if (carry[i] is not { } stack)
            {
                continue;
            }

            if (bag.StackProblems(stack, $"随身栏第 {i + 1} 格", problems) is { } item)
            {
                if (!IsOneCell(item.Footprint))
                {
                    problems.Add($"随身栏第 {i + 1} 格：「{stack.ItemId}」占 {item.Footprint} 格，随身栏只放占 1 格的东西");
                    continue;
                }

                bag._carry[i] = stack;
                bag._lastSequence = Math.Max(bag._lastSequence, stack.AcquiredSequence);
            }
        }

        for (var i = 0; i < anchors.Count; i++)
        {
            var at = $"网格 {bag.CellName(i)}";
            if (anchors[i] is not { } stack || bag.StackProblems(stack, at, problems) is not { } item)
            {
                continue;
            }

            if (!bag.InGrid(i, item.Footprint))
            {
                problems.Add($"{at}：「{stack.ItemId}」占 {item.Footprint} 格，从这里往右下摆出了 {width}×{height} 的网格");
                continue;
            }

            var clash = bag.CellsOf(i, item.Footprint).FirstOrDefault(c => bag._coveredBy[c] != Free, Free);
            if (clash != Free)
            {
                var other = bag._coveredBy[clash];
                problems.Add(
                    $"{at}：「{stack.ItemId}」与左上角在 {bag.CellName(other)} 的「{bag._anchors[other]!.Value.ItemId}」压在一起");
                continue;
            }

            bag.Put(i, stack);
            bag._lastSequence = Math.Max(bag._lastSequence, stack.AcquiredSequence);
        }

        return problems.Count == 0
            ? bag
            : throw new InvalidDataException($"背包有 {problems.Count} 处对不上：\n  " + string.Join("\n  ", problems));
    }

    /// <summary>在册物品：堆上限、占地与有无品质读这里。</summary>
    public ItemCatalog Items { get; }

    /// <summary>网格几格宽。</summary>
    public int GridWidth { get; }

    /// <summary>网格几格高。</summary>
    public int GridHeight { get; }

    /// <summary>网格一共几格。随身栏那一排不算在里面，见 <see cref="CarryCount"/>。</summary>
    public int SlotCount => _anchors.Length;

    /// <summary>网格每一格记的那一堆，按格号排：一堆记在它左上角那一格，空格与被别的堆盖着的格是 <c>null</c>。</summary>
    public IReadOnlyList<ItemStack?> Slots => _anchors;

    /// <summary>随身栏那一排几格。</summary>
    public int CarryCount => _carry.Length;

    /// <summary>随身栏那一排每一格的那一堆，按格排；空格是 <c>null</c>。</summary>
    public IReadOnlyList<ItemStack?> Carry => _carry;

    /// <summary>还剩几格空着：随身栏那一排与网格合起来，没被任何一堆占着的格。</summary>
    public int EmptySlotCount => _carry.Count(s => s is null) + _coveredBy.Count(c => c == Free);

    /// <summary>一件东西都没有。</summary>
    public bool IsEmpty => _carry.All(s => s is null) && _anchors.All(s => s is null);

    /// <summary>这一处记的那一堆：随身栏那一格的，或者网格里左上角在这一格的；没有是 <c>null</c>。</summary>
    public ItemStack? StackAt(BagPlace place)
    {
        RequirePlace(place, nameof(place));
        return place.InCarry ? _carry[place.Index] : _anchors[place.Index];
    }

    /// <summary>整份复制：副本与原件不共用任何会变的东西。</summary>
    public Bag Clone()
    {
        var copy = new Bag(GridWidth, GridHeight, CarryCount, Items) { _lastSequence = _lastSequence };
        _carry.CopyTo(copy._carry, 0);
        _anchors.CopyTo(copy._anchors, 0);
        _coveredBy.CopyTo(copy._coveredBy, 0);
        return copy;
    }

    /// <summary>这一份放不放得下：先合进没满的同标识堆，再找空地，全放得下才算。不改背包。</summary>
    public bool CanAdd(string itemId, string? qualityId, int count) => RefusalFor(itemId, qualityId, count) is null;

    /// <summary>这一份为什么放不下；放得下是 <c>null</c>。不改背包。</summary>
    /// <remarks>
    /// 随身栏那一排与网格里一格空的都没有是满了；还有空格、却凑不出一块够它整块占地的空地，是放不下这么大的 ——
    /// 随身栏空着而东西占不止一格，也是后一种。一份要分成几块时，按放到哪一块卡住的那一刻算。
    /// </remarks>
    public BagRefusal? RefusalFor(string itemId, string? qualityId, int count) => Plan(itemId, qualityId, count).Refusal;

    /// <summary>这一种这一品质是不是满载：没有可合的同标识堆，也没有放得下一件的空地。</summary>
    public bool IsFullFor(string itemId, string? qualityId) => !CanAdd(itemId, qualityId, 1);

    /// <summary>放进一份。放得下就全放进去、返回 <c>true</c>；放不下就一件都不放、返回 <c>false</c>，背包一件不变。</summary>
    /// <remarks>为什么放不下问 <see cref="RefusalFor"/>：放不下时背包没变，问出来的就是这一次的原因。</remarks>
    public bool TryAdd(string itemId, string? qualityId, int count)
    {
        var (plan, _) = Plan(itemId, qualityId, count);
        if (plan is null)
        {
            return false;
        }

        var stamp = ++_lastSequence;
        foreach (var (place, take) in plan)
        {
            var stack = new ItemStack(itemId, qualityId, (StackAt(place)?.Count ?? 0) + take, stamp);
            if (place.InCarry)
            {
                _carry[place.Index] = stack;
            }
            else if (_anchors[place.Index] is null)
            {
                Put(place.Index, stack);
            }
            else
            {
                _anchors[place.Index] = stack;
            }
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

        trial._carry.CopyTo(_carry, 0);
        trial._anchors.CopyTo(_anchors, 0);
        trial._coveredBy.CopyTo(_coveredBy, 0);
        _lastSequence = trial._lastSequence;
        return true;
    }

    /// <summary>从网格里左上角在第 <paramref name="cell"/> 格（从 0 数）的那一堆拿走几件，见另一个重载。</summary>
    public bool TryRemoveAt(int cell, int count) => TryRemoveAt(BagPlace.Grid(cell), count);

    /// <summary>从这一处记的那一堆拿走几件。拿完那一格（网格里是那一块）空出来。</summary>
    /// <returns>这一处没有一堆、或者件数不够，就一件都不拿、返回 <c>false</c>。</returns>
    /// <remarks>位置越界、件数不为正是调用方的错，当场抛。拿走不改那一堆的先后序号：「最近获得」问的是什么时候进来的。</remarks>
    public bool TryRemoveAt(BagPlace place, int count)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
        if (StackAt(place) is not { } stack || stack.Count < count)
        {
            return false;
        }

        if (stack.Count == count)
        {
            Remove(place);
        }
        else if (place.InCarry)
        {
            _carry[place.Index] = stack with { Count = stack.Count - count };
        }
        else
        {
            _anchors[place.Index] = stack with { Count = stack.Count - count };
        }

        return true;
    }

    /// <summary>挪网格里的两格，见另一个重载。</summary>
    public BagMoveResult Move(int from, int to) => Move(BagPlace.Grid(from), BagPlace.Grid(to));

    /// <summary>挪格：这两处互换，目标空着时就是搬过去。占不止一格的东西进不了随身栏那一排，那一下什么都不动。</summary>
    /// <remarks>
    /// 同标识的两堆挪到一起也只是互换，不合堆；挪格不改哪一堆的先后序号。位置越界是调用方的错，当场抛。
    /// 网格里现在只挪得动占 1 格的东西：照块挪、合堆与压到别的就不动还没有，归设计仓台账 GP-170。
    /// </remarks>
    public BagMoveResult Move(BagPlace from, BagPlace to)
    {
        var moving = StackAt(from);
        var other = StackAt(to);
        if ((to.InCarry && moving is { } m0 && !IsOneCell(FootprintOf(m0)))
            || (from.InCarry && other is { } o0 && !IsOneCell(FootprintOf(o0))))
        {
            return BagMoveResult.TooBigForCarry;
        }

        RequireOneCellOrFree(from);
        RequireOneCellOrFree(to);

        Remove(from);
        Remove(to);
        if (other is { } o)
        {
            Place(from, o);
        }

        if (moving is { } m)
        {
            Place(to, m);
        }

        return BagMoveResult.Moved;
    }

    /// <summary>丢弃网格里左上角在第 <paramref name="cell"/> 格（从 0 数）的那一整堆，见另一个重载。</summary>
    public BagDiscardResult TryDiscardAt(int cell) => TryDiscardAt(BagPlace.Grid(cell));

    /// <summary>丢弃这一处记的那一整堆。</summary>
    /// <remarks>
    /// 那一样声明了不可丢弃时一件都不丢，说得出是这一种（物品定义那一格，设计仓 design/物品系统.md）。丢之前那一道确认
    /// 在界面那一侧，这里只管确认之后那一下。位置越界是调用方的错，当场抛。
    /// </remarks>
    public BagDiscardResult TryDiscardAt(BagPlace place)
    {
        if (StackAt(place) is not { } stack)
        {
            return BagDiscardResult.NothingThere;
        }

        if (!Items.Items[stack.ItemId].Discardable)
        {
            return BagDiscardResult.NotDiscardable;
        }

        Remove(place);
        return BagDiscardResult.Discarded;
    }

    /// <summary>
    /// 按一个键把网格排一遍：筛选看得见的那些格（空格也算看得见）里，看得见的那几堆从最前面的一格起挨个摆，剩下的变空。
    /// 随身栏那一排不在网格里，排序不碰它。
    /// </summary>
    /// <param name="key">按什么排。</param>
    /// <param name="filter">筛选条件。被筛掉的那几堆留在原来那几格，排序越过它们。</param>
    /// <remarks>
    /// 随身栏那一排为什么不动在设计仓 design/背包系统.md 的「分堆的代价由排序、筛选与一键整理来付」一节。
    /// 现在只排得动占 1 格的东西：照「第一块够大的空地」摆、摆不下就不动还没有，归设计仓台账 GP-171。
    /// </remarks>
    public void Sort(BagSortKey key, BagFilter filter) => Arrange(key, filter, merge: false);

    /// <summary>
    /// 一键整理：把网格里同标识的散堆并起来，再按 <paramref name="key"/> 排。动哪几格与 <see cref="Sort"/> 一样。
    /// </summary>
    /// <remarks>
    /// 散堆是没装满的那几堆：同一种同一品质的散堆并成满堆加至多一个零头，装满的那几堆不动它们的件数。
    /// 总数一件不变、一件都不丢、不跨容器 —— 它只动摆法。整理两次与整理一次一样。并出来的那几堆记的是参与合并的
    /// 那几堆里最近的那个序号：「最近获得」问的是这一堆里最近进来的那一件。
    /// </remarks>
    public void Tidy(BagSortKey key, BagFilter filter) => Arrange(key, filter, merge: true);

    private void Arrange(BagSortKey key, BagFilter filter, bool merge)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var places = Enumerable.Range(0, _anchors.Length)
            .Where(i => _coveredBy[i] == Free || (_anchors[i] is { } stack && filter.Shows(stack, Items)))
            .ToList();
        foreach (var place in places)
        {
            RequireOneCellOrFree(BagPlace.Grid(place));
        }

        var stacks = places.Where(i => _anchors[i] is not null).Select(i => _anchors[i]!.Value).ToList();
        if (merge)
        {
            stacks = Merge(stacks);
        }

        stacks.Sort(BagOrder.For(key, Items));
        foreach (var place in places)
        {
            Clear(place);
        }

        for (var n = 0; n < stacks.Count; n++)
        {
            Put(places[n], stacks[n]);
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

    /// <summary>
    /// 算出这一份怎么放：每一处是一堆的位置与放几件。先合进没满的同标识堆（先随身栏、再网格），再一块一块找空地：
    /// 占 1 格的先找随身栏的空格、再找网格，大的直接找网格。放不下时那一份计划是 <c>null</c>，并说出原因。
    /// </summary>
    private (List<(BagPlace Place, int Take)>? Plan, BagRefusal? Refusal) Plan(string itemId, string? qualityId, int count)
    {
        if (ProblemOf(itemId, qualityId, count, Items) is string problem)
        {
            throw count < 1 ? new ArgumentOutOfRangeException(nameof(count), problem) : new ArgumentException(problem);
        }

        var item = Items.Items[itemId];
        var plan = new List<(BagPlace Place, int Take)>();
        var remaining = count;

        void MergeInto(BagPlace place, ItemStack? at)
        {
            if (remaining > 0 && at is { } stack && stack.Holds(itemId, qualityId) && stack.Count < item.StackLimit)
            {
                var take = Math.Min(item.StackLimit - stack.Count, remaining);
                plan.Add((place, take));
                remaining -= take;
            }
        }

        for (var i = 0; i < _carry.Length; i++)
        {
            MergeInto(BagPlace.Carry(i), _carry[i]);
        }

        for (var i = 0; i < _anchors.Length; i++)
        {
            MergeInto(BagPlace.Grid(i), _anchors[i]);
        }

        var carryFree = _carry.Select(s => s is null).ToArray();
        var covered = (int[])_coveredBy.Clone();
        while (remaining > 0)
        {
            var slot = IsOneCell(item.Footprint) ? Array.IndexOf(carryFree, true) : Free;
            BagPlace place;
            if (slot != Free)
            {
                carryFree[slot] = false;
                place = BagPlace.Carry(slot);
            }
            else
            {
                var anchor = FirstFit(covered, item.Footprint);
                if (anchor == Free)
                {
                    var anyFree = carryFree.Contains(true) || covered.Contains(Free);
                    return (null, anyFree ? BagRefusal.NoRoomForSize : BagRefusal.Full);
                }

                foreach (var cell in CellsOf(anchor, item.Footprint))
                {
                    covered[cell] = anchor;
                }

                place = BagPlace.Grid(anchor);
            }

            var take = Math.Min(item.StackLimit, remaining);
            plan.Add((place, take));
            remaining -= take;
        }

        return (plan, null);
    }

    /// <summary>从上到下、从左到右第一个放得下这一块的左上角；没有是 <see cref="Free"/>。</summary>
    private int FirstFit(int[] covered, ItemFootprint footprint)
    {
        for (var row = 0; row + footprint.Height <= GridHeight; row++)
        {
            for (var column = 0; column + footprint.Width <= GridWidth; column++)
            {
                var anchor = (row * GridWidth) + column;
                if (CellsOf(anchor, footprint).All(c => covered[c] == Free))
                {
                    return anchor;
                }
            }
        }

        return Free;
    }

    /// <summary>左上角在这一格的这一块整个落在网格里吗。</summary>
    private bool InGrid(int anchor, ItemFootprint footprint) =>
        (anchor % GridWidth) + footprint.Width <= GridWidth && (anchor / GridWidth) + footprint.Height <= GridHeight;

    /// <summary>左上角在这一格的这一块盖着哪几格，按格号排。调用方先保证它整个落在网格里。</summary>
    private IEnumerable<int> CellsOf(int anchor, ItemFootprint footprint)
    {
        for (var row = 0; row < footprint.Height; row++)
        {
            for (var column = 0; column < footprint.Width; column++)
            {
                yield return anchor + (row * GridWidth) + column;
            }
        }
    }

    /// <summary>把一堆摆进这一处。网格里先保证那一块全空着、整个落在网格里；随身栏里先保证它只占 1 格。</summary>
    private void Place(BagPlace place, ItemStack stack)
    {
        if (place.InCarry)
        {
            _carry[place.Index] = stack;
        }
        else
        {
            Put(place.Index, stack);
        }
    }

    /// <summary>拿走这一处记的那一堆。这一处没有一堆时什么都不做。</summary>
    private void Remove(BagPlace place)
    {
        if (place.InCarry)
        {
            _carry[place.Index] = null;
        }
        else
        {
            Clear(place.Index);
        }
    }

    /// <summary>把一堆摆在网格里左上角那一格，盖上它那一块。调用方先保证那一块全空着、整个落在网格里。</summary>
    private void Put(int anchor, ItemStack stack)
    {
        _anchors[anchor] = stack;
        foreach (var cell in CellsOf(anchor, FootprintOf(stack)))
        {
            _coveredBy[cell] = anchor;
        }
    }

    /// <summary>拿走网格里左上角在这一格的那一堆，空出它那一块。这一格不是哪一堆的左上角时什么都不做。</summary>
    private void Clear(int anchor)
    {
        if (_anchors[anchor] is not { } stack)
        {
            return;
        }

        foreach (var cell in CellsOf(anchor, FootprintOf(stack)))
        {
            _coveredBy[cell] = Free;
        }

        _anchors[anchor] = null;
    }

    private ItemFootprint FootprintOf(ItemStack stack) => Items.Items[stack.ItemId].Footprint;

    /// <summary>网格里这一格写成 (列, 行)，从 0 数。</summary>
    private string CellName(int cell) => $"({cell % GridWidth}, {cell / GridWidth})";

    private static bool IsOneCell(ItemFootprint footprint) => footprint is { Width: 1, Height: 1 };

    /// <summary>读回来的一堆自己对不对；对就交出它的物品定义，不对就把问题记下、交出 <c>null</c>。</summary>
    private ItemDefinition? StackProblems(ItemStack stack, string at, List<string> problems)
    {
        if (ProblemOf(stack.ItemId, stack.QualityId, stack.Count, Items) is string problem)
        {
            problems.Add($"{at}：{problem}");
            return null;
        }

        var item = Items.Items[stack.ItemId];
        if (stack.Count > item.StackLimit)
        {
            problems.Add($"{at}：「{stack.ItemId}」有 {stack.Count} 件，超过它的堆上限 {item.StackLimit}");
        }

        if (stack.AcquiredSequence < 1)
        {
            problems.Add($"{at}：序号 {stack.AcquiredSequence} 不为正");
        }

        return item;
    }

    private void RequirePlace(BagPlace place, string name)
    {
        var count = place.InCarry ? _carry.Length : _anchors.Length;
        if (place.Index < 0 || place.Index >= count)
        {
            throw new ArgumentOutOfRangeException(
                name, $"背包的{(place.InCarry ? "随身栏" : "网格")}只有 {count} 格（从 0 数），没有第 {place.Index} 格");
        }
    }

    /// <summary>网格里这一格要么空着，要么是一堆占 1 格的东西：网格里的挪格与排序现在只动得了这样的格。随身栏那一排总是。</summary>
    private void RequireOneCellOrFree(BagPlace place)
    {
        if (place.InCarry)
        {
            return;
        }

        var owner = _coveredBy[place.Index];
        if (owner != Free && FootprintOf(_anchors[owner]!.Value) is var footprint && !IsOneCell(footprint))
        {
            throw new InvalidOperationException(
                $"背包{place}被一块占 {footprint} 格的东西盖着，现在只挪得动、排得动占 1 格的东西");
        }
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
