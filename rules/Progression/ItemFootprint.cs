namespace Tinderhearth.Rules.Progression;

/// <summary>一件物品在背包与仓库的网格里占多大一块：宽几格、高几格，量纲是格。</summary>
/// <remarks>
/// 规则在设计仓 design/物品系统.md 字段表的「占地」一行：至少 1×1、是一块长方形、不能旋转，所以只有宽与高两个
/// 正整数，不存形状、不存朝向。为什么是长方形、为什么不能旋转在设计仓 decisions/ADR-0034-背包与仓库改成网格.md。
///
/// 两格都用 <c>required</c>，理由同 <see cref="ItemDefinition"/>：缺了要报错，不能拿 0 填上。
/// </remarks>
public sealed record ItemFootprint
{
    /// <summary>宽几格，至少 1。</summary>
    public required int Width
    {
        get;
        init => field = AtLeastOne(value, nameof(Width));
    }

    /// <summary>高几格，至少 1。</summary>
    public required int Height
    {
        get;
        init => field = AtLeastOne(value, nameof(Height));
    }

    /// <summary>写成「宽×高」，与设计仓里占地的写法一样。</summary>
    public override string ToString() => $"{Width}×{Height}";

    private static int AtLeastOne(int value, string name) =>
        value >= 1
            ? value
            : throw new ArgumentOutOfRangeException(name, $"物品定义的 Footprint.{name} 至少是 1 格，实际 {value}");
}
