namespace Tinderhearth.Rules.Ui;

/// <summary>
/// 界面导航栈（`UI-6`）。压入、弹出、逐层返回。
/// </summary>
/// <remarks>
/// 为什么它在规则层而不是引擎层：**「按返回键会回到哪」是规则，不是表现。** 放在引擎层就只能
/// 靠实机点击验证，而这里的每条行为都能用单元测试盯住 —— 包括最容易被写错的那条：
/// **栈空时返回键不能被吞掉**（否则玩家在没开面板时按返回，游戏毫无反应，看起来像卡死）。
///
/// 它只管**标识**不管节点：栈里存的是 <see cref="UiSurface"/> 的 id，引擎层拿 id 去显示或
/// 隐藏对应的 Control。这样规则层不引用 Godot（[ADR-0007] 的程序集边界）。
/// </remarks>
public sealed class NavigationStack
{
    private readonly List<UiSurface> _stack = [];

    /// <summary>栈里有几层。</summary>
    public int Depth => _stack.Count;

    /// <summary>最上面那一层，空栈时为 <c>null</c>。</summary>
    public UiSurface? Top => _stack.Count > 0 ? _stack[^1] : null;

    /// <summary>当前打开的全部层，自下而上。</summary>
    public IReadOnlyList<UiSurface> Surfaces => _stack;

    /// <summary>
    /// 世界现在该不该暂停：**栈里有任何一层就暂停**。
    /// </summary>
    /// <remarks>
    /// 正典的判据是「任何弹出界面并接管输入的东西一律暂停世界」（[玩法定位 · 弹界面接管输入就
    /// 暂停世界，世界里的动作不暂停]），而进了这个栈的东西按定义就是这样的东西 —— 所以这里直接
    /// 读栈深，不去问各层「你要不要暂停」。
    ///
    /// **判据落在「界面接管输入」上，不落在「玩家操作不了」上。** 硬直、失衡、被击倒同样让玩家
    /// 操作不了，它们绝不暂停 —— 那是战斗本身，不进这个栈。发生在世界里的动作（喝药、用道具、
    /// 采集、技能）同理：它们走各自的读条与后摇，不是界面。
    /// </remarks>
    public bool WorldShouldPause => _stack.Count > 0;

    /// <summary>压入一层。已经在栈里的层会被提到栈顶，不重复压。</summary>
    public void Push(UiSurface surface)
    {
        _stack.Remove(surface);
        _stack.Add(surface);
    }

    /// <summary>弹出栈顶。空栈时返回 <c>null</c>，不抛异常。</summary>
    public UiSurface? Pop()
    {
        if (_stack.Count == 0)
        {
            return null;
        }

        var top = _stack[^1];
        _stack.RemoveAt(_stack.Count - 1);
        return top;
    }

    /// <summary>关掉指定层，不管它在第几层。不在栈里时什么也不做。</summary>
    public bool Close(UiSurface surface) => _stack.Remove(surface);

    /// <summary>全部关掉。场景切换时用。</summary>
    public void Clear() => _stack.Clear();

    /// <summary>
    /// 处理返回键。返回是否**消费**了这次输入。
    /// </summary>
    /// <remarks>
    /// 栈空时返回 <c>false</c>，输入交还给上层（在关卡里那通常意味着「打开暂停菜单」）。
    /// 这条是本类存在的主要理由之一 —— 吞掉输入的表现是「按了没反应」，玩家会以为卡死。
    /// </remarks>
    public bool HandleBack() => Pop() is not null;

    /// <summary>某一层现在是不是可见的（在栈里就算可见，面板允许叠放）。</summary>
    public bool IsOpen(UiSurface surface) => _stack.Contains(surface);
}
