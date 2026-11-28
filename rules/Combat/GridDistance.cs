using Tinderhearth.Rules.Economy;

namespace Tinderhearth.Rules.Combat;

/// <summary>两格隔几格：横、竖、斜各算 1 格，不管中间隔着墙还是人。</summary>
/// <remarks>施放距离、闪避的距离倍率与「周围几格」都这样数（工作区根 desc/战斗规则.md 的「格子」），所以「周围 3 格内」是一块方形。</remarks>
public static class GridDistance
{
    /// <summary>从这一格到那一格隔几格；同一格是 0。</summary>
    public static int Between(GridCell a, GridCell b) => Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
}
