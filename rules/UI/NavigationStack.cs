namespace Tinderhearth.Rules.UI;

/// <summary>
/// 界面导航栈：压入、弹出、逐层返回。栈里存的是界面标识，不是节点。
/// </summary>
/// <remarks>
/// 「按返回键会回到哪」是规则不是表现，所以放在这一层，每条行为都能用单元测试盯住。最容易写错的
/// 那条是栈空时不能把返回键吞掉 —— 吞掉的表现是玩家没开面板时按返回，游戏毫无反应，像卡死了。
///
/// 栈里存 <see cref="UISurface"/> 的 id，引擎层拿 id 去显示或隐藏对应的 Control。这样规则层不
/// 引用 Godot，测试能在没有引擎的进程里跑。
/// </remarks>
public sealed class NavigationStack
{
    private readonly List<UISurface> _stack = [];

    /// <summary>栈里有几层。</summary>
    public int Depth => _stack.Count;

    /// <summary>最上面那一层，空栈时为 <c>null</c>。</summary>
    public UISurface? Top => _stack.Count > 0 ? _stack[^1] : null;

    /// <summary>当前打开的全部层，自下而上。</summary>
    public IReadOnlyList<UISurface> Surfaces => _stack;

    /// <summary>
    /// 世界现在该不该暂停：栈里有任何一层就暂停。
    /// </summary>
    /// <remarks>
    /// 弹出界面接管输入时世界一律暂停，而进了这个栈的东西按定义就是这种东西，所以这里直接读栈深，
    /// 不去问各层「你要不要暂停」。
    ///
    /// 分界线在「界面接管了输入」，不在「玩家操作不了」。硬直、失衡、被击倒同样让玩家操作不了，
    /// 它们绝不暂停，那是战斗本身。喝药、用道具、采集、技能同理：走各自的读条与后摇，不是界面。
    ///
    /// 这条规则在设计仓 canon/gameplay/玩法定位.md 的「跨系统约定」一节。
    /// </remarks>
    public bool WorldShouldPause => _stack.Count > 0;

    /// <summary>压入一层。已经在栈里的层会被提到栈顶，不重复压。</summary>
    public void Push(UISurface surface)
    {
        _stack.Remove(surface);
        _stack.Add(surface);
    }

    /// <summary>弹出栈顶。空栈时返回 <c>null</c>，不抛异常。</summary>
    public UISurface? Pop()
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
    public bool Close(UISurface surface) => _stack.Remove(surface);

    /// <summary>全部关掉。场景切换时用。</summary>
    public void Clear() => _stack.Clear();

    /// <summary>
    /// 处理返回键。返回这次输入有没有被消费掉。
    /// </summary>
    /// <remarks>
    /// 栈空时返回 <c>false</c>，输入交还给上层（在关卡里那通常意味着打开暂停菜单）。吞掉输入的
    /// 表现是「按了没反应」，玩家会以为卡死，这是本类存在的主要理由之一。
    /// </remarks>
    public bool HandleBack() => Pop() is not null;

    /// <summary>某一层现在是不是可见的（在栈里就算可见，面板允许叠放）。</summary>
    public bool IsOpen(UISurface surface) => _stack.Contains(surface);
}
