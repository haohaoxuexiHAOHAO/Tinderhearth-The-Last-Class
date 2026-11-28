namespace Tinderhearth.Rules.UI;

/// <summary>开发期的一个调试键：在哪个场景里响应、占哪个物理位、按下去做什么。</summary>
/// <param name="Scene">在哪个场景里响应，例如「基地」。</param>
/// <param name="Symbol">占哪个物理位。与绑定表同一套符号，冲突测试才比得了。</param>
/// <param name="Does">按下去做什么，一句话。</param>
public sealed record DebugKey(string Scene, InputSymbol Symbol, string Does);

/// <summary>开发期那几个调试键，全登在这一处。</summary>
/// <remarks>
/// 它们直接读按键、不进输入映射：它们是工具键不是玩法绑定，进了 <see cref="InputBindings.Table"/> 就会出现在
/// 玩家能重绑的那份清单里，而玩家根本不该看见它们。可它们占的也是物理位 —— 一个调试键与一条绑定撞在同一个位上，
/// 按一下就同时干两件事，而绑定表那一侧看不见它。所以它们登在绑定表旁边，「同一个物理位不被两个动作抢」那条测试
/// 把两边合起来判；直接读按键的地方从这里取键，不各写一个字面量。
///
/// 加一个调试键就在这里加一个字段；有测试盯着每个字段都在 <see cref="All"/> 里。
/// </remarks>
public static class DebugKeys
{
    /// <summary>基地：结束这一天，与睡觉走同一个入口。</summary>
    public static readonly DebugKey BaseEndDay = new("基地", InputSymbol.KeyN, "结束这一天，与睡觉走同一个入口");

    /// <summary>基地：往背包里放下一批种子，每样一满堆，最后一批之后回到第一批。</summary>
    public static readonly DebugKey BaseGiveSeeds = new("基地", InputSymbol.KeyG, "往背包里放下一批种子");

    /// <summary>基地：往背包里放下一件大东西，占不止一格的那几样轮着给，最后一样之后回到第一样。</summary>
    public static readonly DebugKey BaseGiveBigItem = new("基地", InputSymbol.KeyV, "往背包里放下一件大东西");

    /// <summary>全部调试键。冲突测试读的是这一份。</summary>
    public static readonly IReadOnlyList<DebugKey> All = [BaseEndDay, BaseGiveSeeds, BaseGiveBigItem];
}
