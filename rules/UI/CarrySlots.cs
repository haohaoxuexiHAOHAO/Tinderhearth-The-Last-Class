namespace Tinderhearth.Rules.UI;

/// <summary>
/// 随身栏里的一格（`UI-34`）。
/// </summary>
/// <param name="ItemId">物品标识。空串表示这一格是空的。</param>
/// <param name="Count">这一格里有几件。</param>
/// <remarks>
/// **它只存标识与件数，不存物品的定义。** 定义是内容、会被 mod 换掉（[ADR-0022]），所以
/// 图标、名字与「它能不能播」都要调用方拿标识去查 —— 与 `rules/Economy/Plot.cs` 那条
/// 「只存作物标识」同源，也与存档那条「存的一律是标识」同源。
///
/// **这不是一个容器。** 随身栏显示的是背包前几格的投影（`UI-35` 接背包之后就是字面意义上的投影），
/// 所以本记录**不提供任何增减件数的方法** —— 少一件是背包那一侧的事。给它加一个
/// `Take()` 会立刻造出第二份存储，而两份存储对不上时没有任何东西判得出来。
/// </remarks>
public sealed record CarrySlot(string ItemId, int Count)
{
    /// <summary>空格。**它是一个合法的选中对象**，等于「手上没东西」。</summary>
    public static readonly CarrySlot Empty = new(string.Empty, 0);

    /// <summary>这一格是不是空的。件数为零也算空 —— 有标识没件数是「刚用完」，玩家看到的是空格。</summary>
    public bool IsEmpty => string.IsNullOrEmpty(ItemId) || Count <= 0;

    /// <summary>
    /// 要不要在角上画件数。
    /// </summary>
    /// <remarks>
    /// **只有一件时不画数字。** 画了的话一屏六格全是「1」，那一行字没有携带任何信息，
    /// 却占掉 16px 图标本来就不多的可读面积。
    /// </remarks>
    public bool ShowsCount => Count > 1;
}

/// <summary>
/// 随身栏那条六格，以及「现在选中第几格」（`UI-34`）。
/// </summary>
/// <remarks>
/// **它持有的状态只有一个：选中位。** 格子里装什么是别处的投影（见 <see cref="Slots"/>），
/// 所以本类**进正式存档的字段恰好一个** —— 那条两层判据（跨不跨存档位、跨不跨一次睡眠）在设计仓
/// `design/存档系统.md`，选中位跨睡眠保留，所以归正式存档；具体进哪个分片等存档落地时定
/// （待办台账 `GP-54`）。
///
/// **格数与技能位同源**（<see cref="SlotCount"/>）。这不是巧合也不是抄了一个 6：
/// HUD 那一块的宽度按「几个 16px 图标横排」算，两块同宽这件事因此**由算式保证**，
/// 而不是靠两处各写一个常量、再指望它们一直相等。
///
/// **本类不判「这一格能不能拿去做某件事」。** 选中的那一格是不是种子、够不够用、
/// 那一格的状态允不允许，全归用它的那一侧（播种在 `GP-128`、用道具在 `GP-129`）——
/// 随身栏只回答「玩家手上是哪一格」。
/// </remarks>
public sealed class CarrySlotBar
{
    private readonly List<CarrySlot> _slots;
    private int _selected;

    /// <summary>装一条随身栏。**格数对不上直接抛**，不静默补齐或截断。</summary>
    /// <remarks>
    /// 静默补齐的后果是「投影少给了一格」看起来像「那一格是空的」，而那两件事的补救办法完全不同。
    /// </remarks>
    public CarrySlotBar(IReadOnlyList<CarrySlot> slots)
    {
        if (slots.Count != SlotCount)
        {
            throw new ArgumentException(
                $"随身栏应有 {SlotCount} 格，实际 {slots.Count} 格", nameof(slots));
        }

        _slots = [.. slots];
        _selected = 0;
    }

    /// <summary>六格全空的一条随身栏。**接内容之前它就该是这个样子**，不是一个错误状态。</summary>
    public static CarrySlotBar Empty() =>
        new([.. Enumerable.Repeat(CarrySlot.Empty, SlotCount)]);

    /// <summary>
    /// 格数。**与技能位同一个数**，理由在类注释。
    /// </summary>
    public static int SlotCount => InputActions.Skills.Count;

    /// <summary>六格的内容，顺序即编号。</summary>
    public IReadOnlyList<CarrySlot> Slots => _slots;

    /// <summary>
    /// 现在选中第几格，从 0 起。**这是本类唯一进存档的东西。**
    /// </summary>
    public int SelectedIndex => _selected;

    /// <summary>
    /// 选中的那一格。**它可能是空的** —— 那等于「手上没东西」，不是一个缺陷。
    /// </summary>
    public CarrySlot Selected => _slots[_selected];

    /// <summary>
    /// 选中第 <paramref name="index"/> 格。**空格子也选得中。**
    /// </summary>
    /// <remarks>
    /// 拒绝选中空格看着更「智能」，代价是玩家按下去没反应而他不知道为什么 ——
    /// 而「手上什么都不拿」本来就是一个他可能想要的状态（例如只想走过去看看，不想手滑锄一下）。
    ///
    /// 越界返回 <c>false</c> 而不抛：按键映射错了是可以碰到的情况，而抛异常会把一次按错变成崩溃。
    /// **越界时选中位不变** —— 不静默钳到两端，那会让「按了第 7 格」看起来像「按了第 6 格」。
    /// </remarks>
    public bool Select(int index)
    {
        if (index < 0 || index >= SlotCount)
        {
            return false;
        }

        _selected = index;
        return true;
    }

    /// <summary>
    /// 往后挪一格，到末尾绕回第一格。**手柄那一侧用它。**
    /// </summary>
    /// <remarks>
    /// **绕回而不是钳住**：钳住的话玩家在末尾继续按会「没反应」，而他分不清是到头了还是键坏了。
    /// 六格绕一圈最多按五下就到得了任何一格，经营侧不赶时间，这个代价落在最便宜的地方。
    /// </remarks>
    public void SelectNext() => _selected = (_selected + 1) % SlotCount;

    /// <summary>往前挪一格，到开头绕回最后一格。</summary>
    public void SelectPrevious() => _selected = (_selected + SlotCount - 1) % SlotCount;

    /// <summary>
    /// 换一份内容，**选中位不动**。
    /// </summary>
    /// <remarks>
    /// 选中位不跟着内容走，因为随身栏是投影而不是快照：背包里那一堆用完了、或者玩家排了一次序，
    /// 变的是格子里装什么，不是「他手上是第几格」。**这一条是 `UI-35` 换来源时不许改的行为之一。**
    /// </remarks>
    public void Fill(IReadOnlyList<CarrySlot> slots)
    {
        if (slots.Count != SlotCount)
        {
            throw new ArgumentException(
                $"随身栏应有 {SlotCount} 格，实际 {slots.Count} 格", nameof(slots));
        }

        _slots.Clear();
        _slots.AddRange(slots);
    }

    /// <summary>
    /// 载入存档时恢复选中位。
    /// </summary>
    /// <remarks>
    /// **越界一律退回第一格，而且说得出它退过**（返回 <c>false</c>）。存档里的值可能来自一个
    /// 格数不同的旧版本，而那时抛异常等于让玩家的档打不开 —— [ADR-0015] 对旧版本的口径是补默认值，
    /// 不是报错。**这与 <see cref="Select"/> 的越界处理刻意不同**：那一条的输入来自当场的按键，
    /// 这一条的输入来自一个没人修得了的文件。
    /// </remarks>
    public bool Restore(int index)
    {
        if (index < 0 || index >= SlotCount)
        {
            _selected = 0;
            return false;
        }

        _selected = index;
        return true;
    }
}
