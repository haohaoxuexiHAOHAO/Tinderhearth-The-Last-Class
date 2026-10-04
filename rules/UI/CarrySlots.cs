namespace Tinderhearth.Rules.UI;

/// <summary>
/// 随身栏里的一格。
/// </summary>
/// <param name="ItemId">物品标识。空串表示这一格是空的。</param>
/// <param name="Count">这一格里有几件。</param>
/// <remarks>
/// 只存标识与件数，不存物品的定义。定义在数据文件里、会被 mod 换掉，所以图标、名字与「它能不能
/// 播」都要调用方拿标识去查。<c>rules/Economy/Plot.cs</c> 只存作物标识、存档里只存标识，是同一条。
///
/// 这不是一个容器。随身栏显示的是背包前几格的投影，所以本记录不提供任何增减件数的方法 ——
/// 少一件是背包那一侧的事。给它加一个 <c>Take()</c> 会立刻造出第二份存储，而两份存储一旦对不上，
/// 没有任何东西发现得了。
/// </remarks>
public sealed record CarrySlot(string ItemId, int Count)
{
    /// <summary>空格。它是一个合法的选中对象，等于「手上没东西」。</summary>
    public static readonly CarrySlot Empty = new(string.Empty, 0);

    /// <summary>这一格是不是空的。件数为零也算空 —— 有标识没件数是「刚用完」，玩家看到的是空格。</summary>
    public bool IsEmpty => string.IsNullOrEmpty(ItemId) || Count <= 0;

    /// <summary>
    /// 要不要在角上画件数。
    /// </summary>
    /// <remarks>
    /// 只有一件时不画数字。画了的话一整条全是「1」，那行字不携带任何信息，
    /// 却占掉 16px 图标本来就不多的可读面积。
    /// </remarks>
    public bool ShowsCount => Count > 1;
}

/// <summary>
/// 随身栏那一条格子，加上「现在选中第几格」。
/// </summary>
/// <remarks>
/// 它持有的状态只有选中位。格子里装什么是别处的投影（见 <see cref="Slots"/>），所以进存档的字段
/// 也只有选中位 —— 它跨一次睡眠要保留，所以归正式存档；进哪个分片等存档那边落地时再定。
///
/// 格数与技能位取同一个数（<see cref="SlotCount"/>）。这不是巧合也不是抄了一个数：HUD 那一块的
/// 宽度按「几个 16px 图标横排」算，两块同宽于是由算式保证，不靠两处各写一个常量再指望它们相等。
///
/// 本类不判「这一格能不能拿去做某件事」。选中的是不是种子、够不够用、那一格的状态允不允许，全归
/// 用它的那一侧。随身栏只回答「玩家手上是哪一格」。
/// </remarks>
public sealed class CarrySlotBar
{
    private readonly List<CarrySlot> _slots;
    private int _selected;

    /// <summary>装一条随身栏。格数对不上直接抛，不静默补齐或截断。</summary>
    /// <remarks>
    /// 静默补齐的后果是「投影少给了一格」看起来像「那一格是空的」，而这两件事的补救办法完全不同。
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

    /// <summary>全空的一条随身栏。接上背包之前它就该是这个样子，不是一个错误状态。</summary>
    public static CarrySlotBar Empty() =>
        new([.. Enumerable.Repeat(CarrySlot.Empty, SlotCount)]);

    /// <summary>
    /// 格数。与技能位同一个数，理由在类注释。
    /// </summary>
    public static int SlotCount => InputActions.Skills.Count;

    /// <summary>每一格的内容，顺序即编号。</summary>
    public IReadOnlyList<CarrySlot> Slots => _slots;

    /// <summary>
    /// 现在选中第几格，从 0 起。这是本类唯一进存档的东西。
    /// </summary>
    public int SelectedIndex => _selected;

    /// <summary>
    /// 选中的那一格。它可能是空的 —— 那等于「手上没东西」，不是一个缺陷。
    /// </summary>
    public CarrySlot Selected => _slots[_selected];

    /// <summary>
    /// 选中第 <paramref name="index"/> 格。空格子也选得中。
    /// </summary>
    /// <remarks>
    /// 拒绝选中空格看着更聪明，代价是玩家按下去没反应而他不知道为什么。而「手上什么都不拿」本来
    /// 就是他可能想要的状态，比如只想走过去看看，不想手滑锄一下。
    ///
    /// 越界返回 <c>false</c> 而不抛：按键映射配错了是碰得到的情况，抛异常会把一次按错变成崩溃。
    /// 越界时选中位不变，不静默钳到两端 —— 钳了会让「按了不存在的那一格」看起来像「按了最后一格」。
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
    /// 往后挪一格，到末尾绕回第一格。手柄那一侧用它。
    /// </summary>
    /// <remarks>
    /// 绕回而不是钳住：钳住的话玩家在末尾继续按会「没反应」，而他分不清是到头了还是键坏了。
    /// 绕一圈最多按几下就到得了任何一格，经营侧也不赶时间，这点代价落在最便宜的地方。
    /// </remarks>
    public void SelectNext() => _selected = (_selected + 1) % SlotCount;

    /// <summary>往前挪一格，到开头绕回最后一格。</summary>
    public void SelectPrevious() => _selected = (_selected + SlotCount - 1) % SlotCount;

    /// <summary>
    /// 换一份内容，选中位不动。
    /// </summary>
    /// <remarks>
    /// 选中位不跟着内容走，因为随身栏是投影不是快照：背包里那一堆用完了、或者玩家排了一次序，
    /// 变的是格子里装什么，不是「他手上是第几格」。换数据来源的时候这条行为不许改。
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
    /// 越界一律退回第一格，而且说得出它退过（返回 <c>false</c>）。存档里的值可能来自一个格数不同
    /// 的旧版本，那时抛异常等于让玩家的档打不开 —— 旧存档的缺字段与坏值一律补默认值，不报错。
    ///
    /// 与 <see cref="Select"/> 的越界处理刻意不同：那一条的输入来自当场的按键，
    /// 这一条的输入来自一个玩家没法改的文件。
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
