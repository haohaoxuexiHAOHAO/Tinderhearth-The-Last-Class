namespace Tinderhearth.Rules.Economy;

/// <summary>基地网格上的一格，按格坐标记。</summary>
/// <remarks>
/// 它与引擎那一侧瓦片层的格坐标一一对应；规则层不引用 Godot，所以这里自己有一个。
/// 写成文字时与引擎的格坐标同一个样子（「(3, 4)」），日志里两边对得上。
/// </remarks>
/// <param name="X">第几列。</param>
/// <param name="Y">第几行，往下增大。</param>
public readonly record struct GridCell(int X, int Y)
{
    public override string ToString() => $"({X}, {Y})";
}
